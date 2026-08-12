#nullable enable
using System;
using System.Collections.Generic; 
using System.Linq;
using System.Threading.Tasks;
using LBPUnion.ProjectLighthouse.Types.Entities.Interaction;
using LBPUnion.ProjectLighthouse.Types.Entities.Level;
using Microsoft.EntityFrameworkCore;

namespace LBPUnion.ProjectLighthouse.Database;

public partial class DatabaseContext
{
    public async Task RemoveSlot(SlotEntity slot, bool saveChanges = true)
    {
        this.Slots.Remove(slot);

        if (saveChanges) await this.SaveChangesAsync();
    }

    public async Task HeartPlaylist(int userId, PlaylistEntity heartedPlaylist)
    {
        HeartedPlaylistEntity? heartedList = await this.HeartedPlaylists.FirstOrDefaultAsync(p => p.UserId == userId && p.PlaylistId == heartedPlaylist.PlaylistId);
        if (heartedList != null) return;

        this.HeartedPlaylists.Add(new HeartedPlaylistEntity
        {
            PlaylistId = heartedPlaylist.PlaylistId,
            UserId = userId,
        });

        await this.SaveChangesAsync();
    }

    public async Task UnheartPlaylist(int userId, PlaylistEntity heartedPlaylist)
    {
        HeartedPlaylistEntity? heartedList = await this.HeartedPlaylists.FirstOrDefaultAsync(p => p.UserId == userId && p.PlaylistId == heartedPlaylist.PlaylistId);
        if (heartedList != null) this.HeartedPlaylists.Remove(heartedList);

        await this.SaveChangesAsync();
    }

    public async Task HeartLevel(int userId, SlotEntity heartedSlot)
    {
        HeartedLevelEntity? heartedLevel = await this.HeartedLevels.FirstOrDefaultAsync(q => q.UserId == userId && q.SlotId == heartedSlot.SlotId);
        if (heartedLevel != null) return;

        this.HeartedLevels.Add
        (
            new HeartedLevelEntity
            {
                SlotId = heartedSlot.SlotId,
                UserId = userId,
            }
        );

        await this.SaveChangesAsync();
    }

    public async Task UnheartLevel(int userId, SlotEntity heartedSlot)
    {
        HeartedLevelEntity? heartedLevel = await this.HeartedLevels.FirstOrDefaultAsync(q => q.UserId == userId && q.SlotId == heartedSlot.SlotId);
        if (heartedLevel != null) this.HeartedLevels.Remove(heartedLevel);

        await this.SaveChangesAsync();
    }

    public async Task QueueLevel(int userId, SlotEntity queuedSlot)
    {
        QueuedLevelEntity? queuedLevel = await this.QueuedLevels.FirstOrDefaultAsync(q => q.UserId == userId && q.SlotId == queuedSlot.SlotId);
        if (queuedLevel != null) return;

        this.QueuedLevels.Add
        (
            new QueuedLevelEntity
            {
                SlotId = queuedSlot.SlotId,
                UserId = userId,
            }
        );

        await this.SaveChangesAsync();
    }

    public async Task UnqueueLevel(int userId, SlotEntity queuedSlot)
    {
        QueuedLevelEntity? queuedLevel = await this.QueuedLevels.FirstOrDefaultAsync(q => q.UserId == userId && q.SlotId == queuedSlot.SlotId);
        if (queuedLevel != null) this.QueuedLevels.Remove(queuedLevel);

        await this.SaveChangesAsync();
    }

    public async Task RecordRecentlyPlayedLevel(int userId, int slotId)
    {
        RecentlyPlayedEntity? recentlyPlayed =
            await this.RecentlyPlayed.FirstOrDefaultAsync(r =>
                r.UserId == userId);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        //Top recently played level for the user.
        if (recentlyPlayed == null)
        {
            this.RecentlyPlayed.Add(new RecentlyPlayedEntity
            {
                UserId = userId,
                SlotIds = new List<int> { slotId },
                LastPlayedAt = new List<long> { now },
            });

            await this.SaveChangesAsync();
            return;
        }

        //LBP3 can send multiple gameState packets for one level, and if the player is already connected, it doesnt reqrite the entry with this
        if (recentlyPlayed.SlotIds.Count > 0 &&
            recentlyPlayed.SlotIds[0] == slotId)
        {
            return;
        }

        //If the level already exists in the history it removes the slot id and the timestamp
        int existingIndex = recentlyPlayed.SlotIds.IndexOf(slotId);

        if (existingIndex >= 0)
        {
            recentlyPlayed.SlotIds.RemoveAt(existingIndex);

            if (existingIndex < recentlyPlayed.LastPlayedAt.Count)
                recentlyPlayed.LastPlayedAt.RemoveAt(existingIndex);
        }

        //The newest added levels go to the start of the list
        recentlyPlayed.SlotIds.Insert(0, slotId);
        recentlyPlayed.LastPlayedAt.Insert(0, now);

        //Keeps a max of 20 levels
        if (recentlyPlayed.SlotIds.Count > 20)
        {
            recentlyPlayed.SlotIds.RemoveRange(
                20,
                recentlyPlayed.SlotIds.Count - 20);
        }

        if (recentlyPlayed.LastPlayedAt.Count > 20)
        {
            recentlyPlayed.LastPlayedAt.RemoveRange(
                20,
                recentlyPlayed.LastPlayedAt.Count - 20);
        }

        await this.SaveChangesAsync();
    }
}