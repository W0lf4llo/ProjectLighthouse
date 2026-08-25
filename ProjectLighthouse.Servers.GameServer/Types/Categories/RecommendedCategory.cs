#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LBPUnion.ProjectLighthouse.Configuration;
using LBPUnion.ProjectLighthouse.Database;
using LBPUnion.ProjectLighthouse.Filter;
using LBPUnion.ProjectLighthouse.Types.Entities.Level;
using LBPUnion.ProjectLighthouse.Types.Entities.Token;
using LBPUnion.ProjectLighthouse.Types.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LBPUnion.ProjectLighthouse.Servers.GameServer.Types.Categories;

public class RecommendedCategory : SlotCategory
{
    public override string Name { get; set; } = "Recommended For You";
    public override string Description { get; set; } = "Stuff we think you'll like";
    public override string IconHash { get; set; } = "g820625";
    public override string Endpoint { get; set; } = "recommended";
    public override string Tag => "recommended";

    public sealed class ScoredSlot
    {
        public SlotEntity Slot { get; set; } = null!;
        public int SearchScore { get; set; }
        public int PrevSearchScore { get; set; }
        public int Hearts { get; set; }
        public int Likes { get; set; }
    }

    public IQueryable<ScoredSlot> GetScoredItems(DatabaseContext database, GameTokenEntity token, SlotQueryBuilder queryBuilder)
    {
        RecommendedCategoryConfig config = CategoryConfiguration.Instance.Recommended;

        IQueryable<int> seedUserIds = database.HeartedProfiles
            .AsNoTracking()
            .Where(heartedProfile => heartedProfile.UserId == token.UserId)
            .Select(heartedProfile => heartedProfile.HeartedUserId)
            .Distinct();

        IQueryable<int> seedTasteSlotIds = database.HeartedLevels
            .AsNoTracking()
            .Where(heartedLevel => seedUserIds.Contains(heartedLevel.UserId))
            .Select(heartedLevel => heartedLevel.SlotId)
            .Distinct();

        IQueryable<int> neighborUserIds = database.HeartedLevels
            .AsNoTracking()
            .Where(heartedLevel => seedTasteSlotIds.Contains(heartedLevel.SlotId))
            .Where(heartedLevel => heartedLevel.UserId != token.UserId && !seedUserIds.Contains(heartedLevel.UserId))
            .GroupBy(heartedLevel => heartedLevel.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                Overlap = group
                    .Select(heartedLevel => heartedLevel.SlotId)
                    .Distinct()
                    .Count(),
            })
            .Where(user => user.Overlap >= config.MinimumNeighborOverlap)
            .OrderByDescending(user => user.Overlap)
            .ThenBy(user => user.UserId)
            .Take(config.MaxNeighbors)
            .Select(user => user.UserId);

        var directContributions = database.HeartedLevels
            .AsNoTracking()
            .Where(heartedLevel => seedUserIds.Contains(heartedLevel.UserId))
            .Select(heartedLevel => new
            {
                heartedLevel.SlotId,
                heartedLevel.UserId,
            })
            .Distinct()
            .Select(heartedLevel => new
            {
                heartedLevel.SlotId,
                SearchScore = 1,
                PrevSearchScore = 1,
            });

        var neighborContributions =
            from heartedLevel in database.HeartedLevels.AsNoTracking()
            join neighborUserId in neighborUserIds
                on heartedLevel.UserId equals neighborUserId
            select new
            {
                heartedLevel.SlotId,
                heartedLevel.UserId,
            };

        var distinctNeighborContributions = neighborContributions
            .Distinct()
            .Select(heartedLevel => new
            {
                heartedLevel.SlotId,
                SearchScore = 1,
                PrevSearchScore = 0,
            });

        var creatorContributions = database.Slots
            .AsNoTracking()
            .Where(slot => seedUserIds.Contains(slot.CreatorId))
            .Select(slot => new
            {
                slot.SlotId,
                SearchScore = 1,
                PrevSearchScore = 0,
            });

        var scores = directContributions
            .Concat(distinctNeighborContributions)
            .Concat(creatorContributions)
            .GroupBy(contribution => contribution.SlotId)
            .Select(group => new
            {
                SlotId = group.Key,
                SearchScore = group.Sum(contribution => contribution.SearchScore),
                PrevSearchScore = group.Sum(contribution => contribution.PrevSearchScore),
            });

        IQueryable<ScoredSlot> recommendations =
            from slot in database.Slots
                .AsNoTracking()
                .Where(queryBuilder.Build())
                .Where(slot => !database.VisitedLevels.Any(visitedLevel =>
                    visitedLevel.UserId == token.UserId &&
                    visitedLevel.SlotId == slot.SlotId))

            join score in scores
                on slot.SlotId equals score.SlotId

            let hearts = database.HeartedLevels.Count(heartedLevel =>
                heartedLevel.SlotId == slot.SlotId)

            let likes = database.RatedLevels.Count(rating =>
                rating.SlotId == slot.SlotId &&
                rating.Rating == 1)

            orderby
                score.SearchScore descending,
                score.PrevSearchScore descending,
                hearts descending,
                likes descending,
                slot.SlotId descending

            select new ScoredSlot
            {
                Slot = slot,
                SearchScore = score.SearchScore,
                PrevSearchScore = score.PrevSearchScore,
                Hearts = hearts,
                Likes = likes,
            };

        return recommendations;
    }

    public override IQueryable<SlotEntity> GetItems(DatabaseContext database, GameTokenEntity token, SlotQueryBuilder queryBuilder) =>
        this.GetScoredItems(database, token, queryBuilder)
            .Select(recommendation => recommendation.Slot);

    public static ILbpSerializable CreateSerializableSlot(ScoredSlot recommendation, GameTokenEntity token)
    {
        SlotBase serialized = SlotBase.CreateFromEntity(recommendation.Slot, token);

        if (serialized is GameUserSlot userSlot)
        {
            userSlot.SearchScore = recommendation.SearchScore;
            userSlot.PrevSearchScore = recommendation.PrevSearchScore;
        }

        return serialized;
    }

    public override async Task<GameCategory> Serialize(DatabaseContext database, GameTokenEntity token, SlotQueryBuilder queryBuilder, int numResults = 1)
    {
        IQueryable<ScoredSlot> recommendations = this.GetScoredItems(database, token, queryBuilder);

        List<ILbpSerializable> serializedSlots = (await recommendations
                .Take(numResults)
                .ToListAsync())
            .Select(recommendation => CreateSerializableSlot(recommendation, token))
            .ToList();

        int totalSlots = await recommendations.CountAsync();

        return GameCategory.CreateFromEntity(this, new GenericSerializableList(serializedSlots, totalSlots, numResults + 1));
    }
}