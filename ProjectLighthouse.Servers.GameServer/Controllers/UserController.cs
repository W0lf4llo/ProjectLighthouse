using System.Text.Json;
using LBPUnion.ProjectLighthouse.Configuration;
using LBPUnion.ProjectLighthouse.Database;
using LBPUnion.ProjectLighthouse.Extensions;
using LBPUnion.ProjectLighthouse.Files;
using LBPUnion.ProjectLighthouse.Helpers;
using LBPUnion.ProjectLighthouse.Logging;
using LBPUnion.ProjectLighthouse.Servers.GameServer.Helpers;
using LBPUnion.ProjectLighthouse.Servers.GameServer.Types.Users;
using LBPUnion.ProjectLighthouse.Types.Entities.Level;
using LBPUnion.ProjectLighthouse.Types.Entities.Profile;
using LBPUnion.ProjectLighthouse.Types.Entities.Token;
using LBPUnion.ProjectLighthouse.Types.Levels;
using LBPUnion.ProjectLighthouse.Types.Logging;
using LBPUnion.ProjectLighthouse.Types.Filter;
using LBPUnion.ProjectLighthouse.Types.Serialization;
using LBPUnion.ProjectLighthouse.Types.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LBPUnion.ProjectLighthouse.Servers.GameServer.Controllers;

[ApiController]
[Authorize]
[Route("LITTLEBIGPLANETPS3_XML/")]
[Produces("text/xml")]
public class UserController : ControllerBase
{
    private readonly DatabaseContext database;

    public UserController(DatabaseContext database)
    {
        this.database = database;
    }

    [HttpGet("user/{username}")]
    public async Task<IActionResult> GetUser(string username)
    {
        UserEntity? user = await this.database.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return this.NotFound();

        return this.Ok(GameUser.CreateFromEntity(user, this.GetToken().GameVersion));
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUserAlt([FromQuery(Name = "u")] string[] userList)
    {
        List<MinimalUserProfile> minimalUserList = new();
        foreach (string username in userList)
        {
            MinimalUserProfile? profile = await this.database.Users.Where(u => u.Username == username)
                .Select(u => new MinimalUserProfile
                {
                    UserHandle = new NpHandle(u.Username, u.IconHash),
                })
                .FirstOrDefaultAsync();
            if (profile == null) continue;
            minimalUserList.Add(profile);
        }

        return this.Ok(new MinimalUserListResponse(minimalUserList));
    }

    [HttpPost("updateUser")]
    public async Task<IActionResult> UpdateUser()
    {
        GameTokenEntity token = this.GetToken();

        UserEntity? user = await this.database.UserFromGameToken(token);
        if (user == null) return this.Forbid();

        UserUpdate? update = await this.DeserializeBody<UserUpdate>("updateUser", "user");

        if (update == null) return this.BadRequest();

        if (update.Biography != null)
        {
            // Deny request if in read-only mode
            if (ServerConfiguration.Instance.UserGeneratedContentLimits.ReadOnlyMode) return this.BadRequest();

            if (update.Biography.Length > 512) return this.BadRequest();

            string filteredBio = CensorHelper.FilterMessage(update.Biography, FilterLocation.UserBiography, user.Username);

            user.Biography = filteredBio;
        }

        if (update.Location != null) user.Location = update.Location;

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (string? resource in new[] { update.IconHash, update.YayHash, update.MehHash, update.BooHash, })
        {
            if (string.IsNullOrWhiteSpace(resource)) continue;

            // Deny request if in read-only mode
            if (ServerConfiguration.Instance.UserGeneratedContentLimits.ReadOnlyMode) return this.BadRequest();

            if (!FileHelper.ResourceExists(resource) && !resource.StartsWith('g')) return this.BadRequest();

            if (!GameResourceHelper.IsValidTexture(resource)) return this.BadRequest();
        }

        if (!string.IsNullOrWhiteSpace(update.PlanetHash) && !GameResourceHelper.IsValidLevel(update.PlanetHash))
            return this.BadRequest();

        if (update.IconHash != null) user.IconHash = update.IconHash;

        if (update.YayHash != null) user.YayHash = update.YayHash;

        if (update.MehHash != null) user.MehHash = update.MehHash;

        if (update.BooHash != null) user.BooHash = update.BooHash;

        if (update.Slots != null)
        {
            update.Slots = update.Slots.Where(s => s.Type == SlotType.User)
                .Where(s => s.Location != null)
                .Where(s => s.SlotId != 0).ToList();
            foreach (UserUpdateSlot? updateSlot in update.Slots)
            {
                SlotEntity? slot = await this.database.Slots.FirstOrDefaultAsync(s => s.SlotId == updateSlot.SlotId);
                if (slot == null) continue;

                if (slot.CreatorId != token.UserId) continue;

                slot.Location = updateSlot.Location!;
            }
        }

        if (update.PlanetHashLBP2CC != null) user.PlanetHashLBP2CC = update.PlanetHashLBP2CC;

        if (update.PlanetHash != null)
        {
            switch (token.GameVersion)
            {
                case GameVersion.LittleBigPlanet2: // LBP2 planets will apply to LBP3
                    {
                        user.PlanetHashLBP2 = update.PlanetHash;
                        user.PlanetHashLBP3 = update.PlanetHash;
                        break;
                    }
                case GameVersion.LittleBigPlanet3: // LBP3 and vita can only apply to their own games, only set 1 here
                    {
                        user.PlanetHashLBP3 = update.PlanetHash;
                        break;
                    }
                case GameVersion.LittleBigPlanetVita:
                    {
                        user.PlanetHashLBPVita = update.PlanetHash;
                        break;
                    }
                case GameVersion.LittleBigPlanet1:
                case GameVersion.LittleBigPlanetPSP:
                case GameVersion.Unknown:
                default: // The rest do not support custom earths.
                    {
                        string bodyString = await this.ReadBodyAsync();
                        Logger.Warn($"User with invalid gameVersion '{token.GameVersion}' tried to set earth hash: \n" +
                                    $"body: '{bodyString}'",
                            LogArea.Resources);
                        break;
                    }
            }
        }

        await this.database.SaveChangesAsync();

        return this.Ok();
    }

    [HttpPost("update_my_pins")]
    [Produces("text/json")]
    public async Task<IActionResult> UpdateMyPins()
    {
        GameTokenEntity token = this.GetToken();

        UserEntity? user = await this.database.UserFromGameToken(token);
        if (user == null) return this.Forbid();

        string bodyString = await this.ReadBodyAsync();

        Pins? pinJson;

        try
        {
            pinJson = JsonSerializer.Deserialize<Pins>(bodyString);
        }
        catch (JsonException)
        {
            return this.BadRequest();
        }

        if (pinJson == null)
            return this.BadRequest();

        const uint yaysProgressType = 1333342859u;
        const uint votaratorProgressType = 2778528358u;
        const uint teamPickedProgressType = 792777243u;

        if (token.GameVersion == GameVersion.LittleBigPlanet2)
        {
            if (!PinUploadParser.TryParse(
                    pinJson,
                    PinDefinitions.PinGame.Lbp2,
                    out PinUploadParser.ParsedPinUpload parsed))
            {
                return this.BadRequest();
            }

            //
            // Persist LBP2 progress.
            //
            // IMPORTANT:
            // The key is ProgressType, not PinId.
            //
            // update_my_pins is treated as a non-destructive merge for now:
            // a lower client value will not overwrite a higher value already
            // stored by Lighthouse.
            //
            Dictionary<uint, UserPinProgressEntity> existingProgress =
                await this.database.UserPinProgress
                    .Where(p =>
                        p.UserId == token.UserId &&
                        p.GameVersion == GameVersion.LittleBigPlanet2)
                    .ToDictionaryAsync(p => p.ProgressType);

            foreach (PinUploadParser.ProgressEntry entry in parsed.Progress)
            {
                //
                // Yays are handled separately below.
                //
                // Do not let the generic pin merger directly overwrite
                // the server-managed Yays value.
                //
                if (entry.ProgressType == yaysProgressType || entry.ProgressType == votaratorProgressType || entry.ProgressType == teamPickedProgressType)
                {
                    continue;
                }

                if (existingProgress.TryGetValue(
                        entry.ProgressType,
                        out UserPinProgressEntity? entity))
                {
                    bool lowerIsBetter = PinDefinitions.IsLowerProgressBetter(
                        PinDefinitions.PinGame.Lbp2,
                        entry.ProgressType);

                    bool clientHasBetterValue = lowerIsBetter
                        ? entry.Value < entity.Value
                        : entry.Value > entity.Value;

                    if (clientHasBetterValue)
                        entity.Value = entry.Value;

                    continue;
                }

                UserPinProgressEntity newEntity = new()
                {
                    UserId = token.UserId,
                    GameVersion = GameVersion.LittleBigPlanet2,
                    ProgressType = entry.ProgressType,
                    Value = entry.Value,
                };

                this.database.UserPinProgress.Add(newEntity);
                existingProgress.Add(entry.ProgressType, newEntity);
            }
            //
            // Authoritative LBP2 Yays progress.
            //
            // The imported baseline represents progress the player already had
            // before Lighthouse could account for it, such as progress earned
            // on the official servers or another custom server.
            //
            // Lighthouse then adds only the active Yays it can actually verify.
            //
            // A higher client value is allowed to raise the imported baseline,
            // which lets a player visit another server such as Refresh and
            // return without losing newly-earned progress.
            //
            // A lower client value never lowers the imported baseline.
            //
            double? uploadedYays = parsed.Progress
                .Where(p => p.ProgressType == yaysProgressType)
                .Select(p => (double?)p.Value)
                .FirstOrDefault();

            existingProgress.TryGetValue(
                yaysProgressType,
                out UserPinProgressEntity? yaysProgress);

            //
            // Count distinct LBP2 levels that this user currently rates Yay.
            //
            // Rating == 1 is Lighthouse's positive/Yay rating.
            //
            // Restrict this to LBP2 slots so ratings from other games cannot
            // accidentally alter the LBP2 pin state.
            //
            int lighthouseYayQualifications = await this.database.RatedLevels
    .Where(r =>
        r.UserId == token.UserId &&
        r.YaysQualified &&
        r.Slot.GameVersion == GameVersion.LittleBigPlanet2)
    .Select(r => r.SlotId)
    .Distinct()
    .CountAsync();

            UserPinBaselineEntity? yaysBaseline =
                await this.database.UserPinBaselines
                    .FirstOrDefaultAsync(b =>
                        b.UserId == token.UserId &&
                        b.GameVersion == GameVersion.LittleBigPlanet2 &&
                        b.ProgressType == yaysProgressType);

            if (yaysBaseline == null)
            {
                //
                // First time Lighthouse takes ownership of this user's Yays.
                //
                // Start with the best value we know:
                //
                //   1. Previously stored pin progress
                //   2. Current uploaded client progress
                //
                // Then subtract Lighthouse's own active contribution so it
                // doesn't get counted twice.
                //
                double importedProgress =
                    yaysProgress?.Value ?? 0;

                if (uploadedYays.HasValue &&
                    uploadedYays.Value > importedProgress)
                {
                    importedProgress = uploadedYays.Value;
                }

                double baselineValue =
                    Math.Max(
                        0,
                        importedProgress - lighthouseYayQualifications);

                yaysBaseline = new UserPinBaselineEntity
                {
                    UserId = token.UserId,
                    GameVersion = GameVersion.LittleBigPlanet2,
                    ProgressType = yaysProgressType,
                    BaselineValue = baselineValue,
                };

                this.database.UserPinBaselines.Add(yaysBaseline);
            }

            double canonicalYays =
                yaysBaseline.BaselineValue +
                lighthouseYayQualifications;

            //
            // Store the canonical result in the normal pin progress table so it
            // automatically goes into our authoritative update_my_pins response.
            //
            if (yaysProgress != null)
            {
                yaysProgress.Value = canonicalYays;
            }
            else
            {
                yaysProgress = new UserPinProgressEntity
                {
                    UserId = token.UserId,
                    GameVersion = GameVersion.LittleBigPlanet2,
                    ProgressType = yaysProgressType,
                    Value = canonicalYays,
                };

                this.database.UserPinProgress.Add(yaysProgress);
                existingProgress.Add(yaysProgressType, yaysProgress);
            }

            //
            // Authoritative LBP2 Votarator progress.
            //
            // Votarator counts unique community levels that the player Yayed
            // while the level had fewer than 10 LBP2 plays.
            //
            // Unlike normal Yays, qualification is remembered on the
            // RatedLevelEntity so a level becoming popular later cannot
            // invalidate progress.
            //
            double? uploadedVotarator = parsed.Progress
                .Where(p => p.ProgressType == votaratorProgressType)
                .Select(p => (double?)p.Value)
                .FirstOrDefault();

            existingProgress.TryGetValue(
                votaratorProgressType,
                out UserPinProgressEntity? votaratorProgress);

            //
            // Count unique Lighthouse levels that qualified for Votarator.
            //
            int lighthouseVotaratorQualifications =
                await this.database.RatedLevels
                    .Where(r =>
                        r.UserId == token.UserId &&
                        r.VotaratorQualified &&
                        r.Slot.GameVersion == GameVersion.LittleBigPlanet2)
                    .Select(r => r.SlotId)
                    .Distinct()
                    .CountAsync();

            //
            // Imported progress from Sony/another custom server is stored in
            // the same generic baseline table used by Yays.
            //
            UserPinBaselineEntity? votaratorBaseline =
                await this.database.UserPinBaselines
                    .FirstOrDefaultAsync(b =>
                        b.UserId == token.UserId &&
                        b.GameVersion == GameVersion.LittleBigPlanet2 &&
                        b.ProgressType == votaratorProgressType);

            if (votaratorBaseline == null)
            {
                //
                // First time Lighthouse takes ownership of Votarator.
                //
                // Keep whatever historical value was already stored/uploaded.
                //
                // Do NOT subtract Lighthouse qualifications here:
                // the tests showed LBP2 was not increasing Votarator locally,
                // so these qualifying Lighthouse ratings have not already been
                // included in the client's uploaded total.
                //
                double importedProgress =
                    votaratorProgress?.Value ?? 0;

                if (uploadedVotarator.HasValue &&
                    uploadedVotarator.Value > importedProgress)
                {
                    importedProgress = uploadedVotarator.Value;
                }

                votaratorBaseline = new UserPinBaselineEntity
                {
                    UserId = token.UserId,
                    GameVersion = GameVersion.LittleBigPlanet2,
                    ProgressType = votaratorProgressType,
                    BaselineValue = importedProgress,
                };

                this.database.UserPinBaselines.Add(votaratorBaseline);
            }

            double canonicalVotarator =
                votaratorBaseline.BaselineValue +
                lighthouseVotaratorQualifications;

            //
            // Put the calculated value into the generic pin progress table.
            // It will automatically be included in responseProgress below.
            //
            if (votaratorProgress != null)
            {
                votaratorProgress.Value = canonicalVotarator;
            }
            else
            {
                votaratorProgress = new UserPinProgressEntity
                {
                    UserId = token.UserId,
                    GameVersion = GameVersion.LittleBigPlanet2,
                    ProgressType = votaratorProgressType,
                    Value = canonicalVotarator,
                };

                this.database.UserPinProgress.Add(votaratorProgress);

                existingProgress.Add(
                    votaratorProgressType,
                    votaratorProgress);
            }

            double? uploadedTeamPicked = parsed.Progress
    .Where(p => p.ProgressType == teamPickedProgressType)
    .Select(p => (double?)p.Value)
    .FirstOrDefault();

            existingProgress.TryGetValue(
                teamPickedProgressType,
                out UserPinProgressEntity? teamPickedProgress);

            int lighthouseTeamPicks =
                await this.database.TeamPickQualifications
                    .Where(q =>
                        q.UserId == token.UserId &&
                        q.GameVersion == GameVersion.LittleBigPlanet2)
                    .Select(q => q.SlotId)
                    .Distinct()
                    .CountAsync();

            UserPinBaselineEntity? teamPickedBaseline =
                await this.database.UserPinBaselines
                    .FirstOrDefaultAsync(b =>
                        b.UserId == token.UserId &&
                        b.GameVersion == GameVersion.LittleBigPlanet2 &&
                        b.ProgressType == teamPickedProgressType);

            if (teamPickedBaseline == null)
            {
                //
                // Import any Team Picked progress the player already had before
                // Lighthouse began tracking individual Team Picks.
                //
                double importedProgress =
                    teamPickedProgress?.Value ?? 0;

                if (uploadedTeamPicked.HasValue &&
                    uploadedTeamPicked.Value > importedProgress)
                {
                    importedProgress = uploadedTeamPicked.Value;
                }

                //
                // Existing TeamPickQualifications may already represent some of
                // that imported progress, so subtract them to avoid double-counting.
                //
                double baselineValue =
                    Math.Max(
                        0,
                        importedProgress - lighthouseTeamPicks);

                teamPickedBaseline = new UserPinBaselineEntity
                {
                    UserId = token.UserId,
                    GameVersion = GameVersion.LittleBigPlanet2,
                    ProgressType = teamPickedProgressType,
                    BaselineValue = baselineValue,
                };

                this.database.UserPinBaselines.Add(teamPickedBaseline);
            }

            double canonicalTeamPicked =
                teamPickedBaseline.BaselineValue +
                lighthouseTeamPicks;

            if (teamPickedProgress != null)
            {
                teamPickedProgress.Value = canonicalTeamPicked;
            }
            else
            {
                teamPickedProgress = new UserPinProgressEntity
                {
                    UserId = token.UserId,
                    GameVersion = GameVersion.LittleBigPlanet2,
                    ProgressType = teamPickedProgressType,
                    Value = canonicalTeamPicked,
                };

                this.database.UserPinProgress.Add(teamPickedProgress);

                existingProgress.Add(
                    teamPickedProgressType,
                    teamPickedProgress);
            }
            //
            // Persist awarded pins.
            //
            // Awards are keyed by PinId.
            // As with progress, do not let a lower client count destroy a
            // higher value Lighthouse already knows about.
            //
            Dictionary<uint, UserPinAwardEntity> existingAwards =
                await this.database.UserPinAwards
                    .Where(a =>
                        a.UserId == token.UserId &&
                        a.GameVersion == GameVersion.LittleBigPlanet2)
                    .ToDictionaryAsync(a => a.PinId);

            foreach (PinUploadParser.AwardEntry entry in parsed.Awards)
            {
                if (existingAwards.TryGetValue(entry.PinId, out UserPinAwardEntity? entity))
                {
                    if (entry.Count > entity.AwardCount)
                        entity.AwardCount = entry.Count;

                    continue;
                }

                UserPinAwardEntity newEntity = new()
                {
                    UserId = token.UserId,
                    GameVersion = GameVersion.LittleBigPlanet2,
                    PinId = entry.PinId,
                    AwardCount = entry.Count,
                };

                this.database.UserPinAwards.Add(newEntity);
                existingAwards.Add(entry.PinId, newEntity);
            }

            //
            // profile_pins is optional.
            //
            // If it's absent, progress/awards above are STILL saved.
            //
            // If it is present but contains an invalid/foreign/duplicate pin,
            // leave the user's existing equipped pins untouched.
            //
            if (parsed.HasProfilePins && parsed.ProfilePinsSafeToApply)
            {
                string newPins = string.Join(",", parsed.ProfilePins);

                if (!string.Equals(user.Pins, newPins))
                    user.Pins = newPins;
            }

            await this.database.SaveChangesAsync();

            //
            // Build the authoritative LBP2 pin state that will be returned
            // to the game.
            //
            // Sony/MM responses use the same alternating-array layout:
            //
            // progress:
            //   ProgressType, Value, ProgressType, Value...
            //
            // awards:
            //   PinId, Count, PinId, Count...
            //
            List<object> responseProgress = [];

            foreach (UserPinProgressEntity progress in
                     existingProgress.Values.OrderBy(p => p.ProgressType))
            {
                responseProgress.Add(progress.ProgressType);
                responseProgress.Add(progress.Value);
            }

            List<object> responseAwards = [];

            foreach (UserPinAwardEntity award in
                     existingAwards.Values
                         .Where(a => a.AwardCount > 0)
                         .OrderBy(a => a.PinId))
            {
                responseAwards.Add(award.PinId);
                responseAwards.Add(award.AwardCount);
            }

            return new JsonResult(new
            {
                progress = responseProgress,
                awards = responseAwards,
            })
            {
                ContentType = "text/json",
            };
        }
        //
        // EXISTING LIGHTHOUSE BEHAVIOR
        //
        // Do not change LBP3/Vita yet. We're implementing and testing
        // LBP2 first.
        //
        if (pinJson.ProfilePins == null)
            return this.BadRequest();

        List<long> legacyProfilePins = [];

        foreach (JsonElement element in pinJson.ProfilePins)
        {
            if (!element.TryGetInt64(out long pinId))
                return this.BadRequest();

            legacyProfilePins.Add(pinId);
        }

        string legacyCurrentPins = user.Pins;
        string legacyNewPins = string.Join(",", legacyProfilePins.Distinct());

        if (string.Equals(legacyCurrentPins, legacyNewPins))
            return this.Ok("[{\"StatusCode\":200}]");

        user.Pins = legacyNewPins;

        await this.database.SaveChangesAsync();

        return this.Ok("[{\"StatusCode\":200}]");
    }
}