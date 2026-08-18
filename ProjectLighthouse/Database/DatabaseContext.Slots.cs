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

    public async Task RecordRecentlyPlayedLevel(int userId, int slotId, bool saveChanges = true)
    {
        RecentlyPlayedEntity? recentlyPlayed = await this.RecentlyPlayed.FirstOrDefaultAsync(r => r.UserId == userId);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        //Level at the top of the recently played category for the user
        if (recentlyPlayed == null)
        {
            this.RecentlyPlayed.Add(new RecentlyPlayedEntity
            {
                UserId = userId,
                SlotIds = new List<int> { slotId },
                LastPlayedAt = new List<long> { now },
            });

            if (saveChanges)await this.SaveChangesAsync();

            return;
        }

        //This makes it so the users most recently played level isnt rewritten if it they're in said level
        if (recentlyPlayed.SlotIds.Count > 0 && recentlyPlayed.SlotIds[0] == slotId)
        {
            return;
        }

        //If the level already existed in the users history, it removes its old timestamp and position, then moves it to the top of the list.
        int existingIndex = recentlyPlayed.SlotIds.IndexOf(slotId);

        if (existingIndex >= 0)
        {
            recentlyPlayed.SlotIds.RemoveAt(existingIndex);

            if (existingIndex < recentlyPlayed.LastPlayedAt.Count)
                recentlyPlayed.LastPlayedAt.RemoveAt(existingIndex);
        }

        //The most recently played level is at the top
        recentlyPlayed.SlotIds.Insert(0, slotId);
        recentlyPlayed.LastPlayedAt.Insert(0, now);

        //Max of 30 levels
        if (recentlyPlayed.SlotIds.Count > 30)
        {
            recentlyPlayed.SlotIds.RemoveRange(30, recentlyPlayed.SlotIds.Count - 30);
        }

        if (recentlyPlayed.LastPlayedAt.Count > 30)
        {
            recentlyPlayed.LastPlayedAt.RemoveRange(30, recentlyPlayed.LastPlayedAt.Count - 30);
        }

        if (saveChanges)await this.SaveChangesAsync();
    }
}
