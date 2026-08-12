#nullable enable
using System;
using System.Text.Json;
using LBPUnion.ProjectLighthouse.Configuration;
using LBPUnion.ProjectLighthouse.Database;
using LBPUnion.ProjectLighthouse.Extensions;
using LBPUnion.ProjectLighthouse.Helpers;
using LBPUnion.ProjectLighthouse.Logging;
using LBPUnion.ProjectLighthouse.Types.Entities.Profile;
using LBPUnion.ProjectLighthouse.Types.Entities.Token;
using LBPUnion.ProjectLighthouse.Types.Logging;
using LBPUnion.ProjectLighthouse.Types.Matchmaking;
using LBPUnion.ProjectLighthouse.Types.Matchmaking.MatchCommands;
using LBPUnion.ProjectLighthouse.Types.Matchmaking.Rooms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LBPUnion.ProjectLighthouse.Types.Users;

namespace LBPUnion.ProjectLighthouse.Servers.GameServer.Controllers.Matching;

[ApiController]
[Authorize]
[Route("LITTLEBIGPLANETPS3_XML/")]
[Produces("text/xml")]
public class MatchController : ControllerBase
{
    private readonly DatabaseContext database;

    public MatchController(DatabaseContext database)
    {
        this.database = database;
    }

    [HttpPost("gameState")]
    [Produces("text/plain")]
    public async Task<IActionResult> GameState()
    {
        GameTokenEntity token = this.GetToken();
        string bodyString = await this.ReadBodyAsync();

        Logger.Info(
            $"Server has received gameState, GameVersion={token.GameVersion}, Platform={token.Platform}, Body={bodyString}",
            LogArea.Match);

        if (string.IsNullOrWhiteSpace(bodyString))
            return this.Ok("VALID");

        try
        {
            int jsonStart = bodyString.IndexOf('{');
            int jsonEnd = bodyString.LastIndexOf('}');

            if (jsonStart < 0 || jsonEnd < jsonStart)
                return this.Ok("VALID");

            string json = bodyString[jsonStart..(jsonEnd + 1)];

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("currentLevel", out JsonElement currentLevel))
                return this.Ok("VALID");

            if (currentLevel.ValueKind != JsonValueKind.Array ||
                currentLevel.GetArrayLength() < 2)
                return this.Ok("VALID");

            string? levelType = currentLevel[0].GetString();

            if (!currentLevel[1].TryGetInt32(out int slotId))
                return this.Ok("VALID");

            Logger.Info(
                $"Parsed gameState: GameVersion={token.GameVersion}, LevelType={levelType}, SlotId={slotId}",
                LogArea.Match);

            //Makes it so that this is a LBP3 only feature
            if (token.GameVersion != GameVersion.LittleBigPlanet3)
                return this.Ok("VALID");

            //Makes it so that only community/user levels belong in Recently Played.
            if (!string.Equals(levelType, "user", StringComparison.OrdinalIgnoreCase))
                return this.Ok("VALID");

            if (slotId <= 0)
                return this.Ok("VALID");

            //This checks the supplied slotId to see if its valid.
            bool slotExists = await this.database.Slots
                .AnyAsync(s => s.SlotId == slotId);

            if (!slotExists)
            {
                Logger.Info(
                    $"Ignoring recently played SlotId={slotId} since it doesn't exist.",
                    LogArea.Match);

                return this.Ok("VALID");
            }

            await this.database.RecordRecentlyPlayedLevel(
                token.UserId,
                slotId);

            Logger.Info(
                $"Successfully updated Recently Played for UserId={token.UserId}, SlotId={slotId}",
                LogArea.Match);
        }
        catch (JsonException e)
        {
            Logger.Error(
                $"Failed to parse the gameState JSON: {e.Message}",
                LogArea.Match);
        }
        catch (Exception e)
        {
            //Makes it so that this recently played implementation doesnt cause /gameState to cry and break.
            Logger.Error(
                $"Failed to update Recently Played: {e.Message}",
                LogArea.Match);
        }
        return this.Ok("VALID");
}

    [HttpPost("match")]
    [Produces("text/plain")]
    public async Task<IActionResult> Match()
    {
        GameTokenEntity token = this.GetToken();

        UserEntity? user = await this.database.UserFromGameToken(token);
        if (user == null) return this.Forbid();

        await LastContactHelper.SetLastContact(this.database, user, token.GameVersion, token.Platform);

        // Do not allow matchmaking if it has been disabled
        if (!ServerConfiguration.Instance.Matchmaking.MatchmakingEnabled) return this.BadRequest();

        #region Parse match data

        // Example POST /match: [UpdateMyPlayerData,["Player":"FireGamer9872"]]

        string bodyString = await this.ReadBodyAsync();

        if (bodyString.Length == 0 || bodyString[0] != '[') return this.BadRequest();

        Logger.Debug("Received match data: " + bodyString, LogArea.Match);

        IMatchCommand? matchData;
        try
        {
            matchData = MatchHelper.Deserialize(bodyString);
        }
        catch(Exception e)
        {
            Logger.Error($"Exception while parsing matchData: body='{bodyString}'", LogArea.Match);
            Logger.Error(e.ToDetailedException(), LogArea.Match);

            return this.BadRequest();
        }

        if (matchData == null)
        {
            Logger.Error($"Could not parse match data: {nameof(matchData)} is null, body='{bodyString}'", LogArea.Match);
            return this.BadRequest();
        }

        Logger.Info($"Parsed match from {user.Username} (type: {matchData.GetType()})", LogArea.Match);

        #endregion

        #region Process match data

        switch (matchData)
        {
            case UpdateMyPlayerData playerData:
            {
                Room? room = RoomHelper.FindRoomByUser(user.UserId, token.GameVersion, token.Platform, true);

                if (playerData.RoomState != null)
                    if (room != null && Equals(room.HostId, user.UserId))
                        room.State = (RoomState)playerData.RoomState;
                break;
            }
            case FindBestRoom diveInData:
            {
                FindBestRoomResponse? response = RoomHelper.FindBestRoom(this.database,
                    user,
                    token.GameVersion,
                    diveInData.RoomSlot,
                    token.Platform);

                if (response == null) return this.NotFound();

                string serialized = JsonSerializer.Serialize(response, typeof(FindBestRoomResponse));
                foreach (Player player in response.Players)
                    MatchHelper.AddUserRecentlyDivedIn(user.UserId, player.User.UserId);

                return this.Ok($"[{{\"StatusCode\":200}},{serialized}]");
            }
            case CreateRoom createRoom:
            {
                List<int> users = new();
                foreach (string playerUsername in createRoom.Players)
                {
                    UserEntity? player = await this.database.Users.FirstOrDefaultAsync(u => u.Username == playerUsername);
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                    if (player != null) users.Add(player.UserId);
                    else return this.BadRequest();
                }

                // Create a new one as requested
                RoomHelper.CreateRoom(users, token.GameVersion, token.Platform, createRoom.RoomSlot);
                break;
            }
            case UpdatePlayersInRoom updatePlayersInRoom:
            {
                Room? room = RoomHelper.Rooms.FirstOrDefault(r => r.HostId == user.UserId);

                if (room != null)
                {
                    List<UserEntity> users = new();
                    foreach (string playerUsername in updatePlayersInRoom.Players)
                    {
                        UserEntity? player = await this.database.Users.FirstOrDefaultAsync(u => u.Username == playerUsername);
                        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                        if (player != null) users.Add(player);
                        else return this.BadRequest();
                    }

                    room.PlayerIds = users.Select(u => u.UserId).ToList();
                    await RoomHelper.CleanupRooms(this.database, null, room);
                }
                break;
            }
        }

        #endregion

        return this.Ok("[{\"StatusCode\":200}]");
    }
}