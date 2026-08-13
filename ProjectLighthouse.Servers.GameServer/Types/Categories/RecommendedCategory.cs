#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LBPUnion.ProjectLighthouse.Database;
using LBPUnion.ProjectLighthouse.Filter;
using LBPUnion.ProjectLighthouse.Types.Entities.Level;
using LBPUnion.ProjectLighthouse.Types.Entities.Token;
using LBPUnion.ProjectLighthouse.Types.Levels;
using LBPUnion.ProjectLighthouse.Types.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LBPUnion.ProjectLighthouse.Servers.GameServer.Types.Categories;

public class RecommendedCategory : Category
{
    //MaxNeighbors essentially sets a cap to the number of users who have a similar taste to what you have for the recommendation algorithm
    //This also ensures that Lighthouse doesnt completely die when it tries to find good recommendations.
    private const int MaxNeighbors = 250;
    //MinimumNeighborOverlap is the minimum number of hearted levels required by another player before they're considered to have a "good taste" as you
    //This is set at 2 levels, so if you and another player have 2 hearted levels that are the same, you'll see more similar results.
    private const int MinimumNeighborOverlap = 2;
    //This is the maximum number of levels that can be reviewed during a single recommendation request.
    private const int MaxCandidatePool = 1000;

    public override string Name { get; set; } =
        "Recommended For You";

    public override string Description { get; set; } =
        "Stuff we think you'll like";

    public override string IconHash { get; set; } =
        "g820625";

    public override string Endpoint { get; set; } =
        "recommended";

    public override string Tag => "recommended";

    public override string[] Types { get; } =
    {
        "slot",
        "adventure",
    };

    //This represents one recommendation after the algorithm calculates how relevant it is to the user
    //The PrevSearchScore here represents the first-stage of the users recommendation score
    //It goes up when a hearted user hearts a level
    //The SearchScore is the final score after the recommendation algorithm,
    //Mostly from users with a similar taste.
    public sealed record ScoredSlot(
        SlotEntity Slot,
        double SearchScore,
        double PrevSearchScore,
        int Hearts,
        int Likes);

    //This will generate a more personalized recommendation tab based on the user..
    //It'll firstly look at the levels hearted by players that you currently heart..
    //This will become the PrevSearchScore.
    //
    //Then it finds users with a similar taste, look at levels they heartedm and it'll contribute to the SearchScore.
    public async Task<List<ScoredSlot>> GetScoredItems(
        DatabaseContext database,
        GameTokenEntity token,
        SlotQueryBuilder queryBuilder)
    {
        //Gets the players that the user currently has hearted, this is REQUIRED if the tag 'recommended' is used.
        List<int> seedUserIds = await database.HeartedProfiles
    .AsNoTracking()
    .Where(h => h.UserId == token.UserId)
    .Select(h => h.HeartedUserId)
    .Distinct()
    .ToListAsync();

if (seedUserIds.Count == 0)
{
    return new List<ScoredSlot>();
}

        //Finds levels hearted by the players you hearted
        //Each hearted player will act as a recommendation source for you.
        var seedHearts = await database.HeartedLevels
            .AsNoTracking()
            .Where(h => seedUserIds.Contains(h.UserId))
            .Select(h => new
            {
                h.UserId,
                h.SlotId,
            })
            .ToListAsync();

        Dictionary<int, int> directScores = seedHearts
            .GroupBy(h => h.SlotId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(h => h.UserId)
                    .Distinct()
                    .Count());

        List<int> seedTasteSlotIds =
            directScores.Keys.ToList();

        //This will find users who seem to have a similar taste to you, it'll look for several hearts of the same levels
        Dictionary<int, int> neighborScores = new();

        if (seedTasteSlotIds.Count > 0)
        {
            var neighbors = await database.HeartedLevels
                .AsNoTracking()
                .Where(h => seedTasteSlotIds.Contains(h.SlotId))
                .Where(h =>
                    h.UserId != token.UserId &&
                    !seedUserIds.Contains(h.UserId))
                .GroupBy(h => h.UserId)
                .Select(group => new
                {
                    UserId = group.Key,

                    //Number of levels the user shares with the use person who hearted them.
                    Overlap = group
                        .Select(h => h.SlotId)
                        .Distinct()
                        .Count(),
                })
                .Where(user =>
                    user.Overlap >= MinimumNeighborOverlap)
                .OrderByDescending(user => user.Overlap)
                .ThenBy(user => user.UserId)
                .Take(MaxNeighbors)
                .ToListAsync();

            List<int> neighborIds = neighbors
                .Select(n => n.UserId)
                .ToList();

            //Finds hearted levels by users of the same taste
            if (neighborIds.Count > 0)
            {
                var neighborScoreRows = await database.HeartedLevels
                    .AsNoTracking()
                    .Where(h => neighborIds.Contains(h.UserId))
                    .GroupBy(h => h.SlotId)
                    .Select(group => new
                    {
                        SlotId = group.Key,

                        Score = group
                            .Select(h => h.UserId)
                            .Distinct()
                            .Count(),
                    })
                    .OrderByDescending(result => result.Score)
                    .Take(MaxCandidatePool)
                    .ToListAsync();

                neighborScores = neighborScoreRows
                    .ToDictionary(
                        result => result.SlotId,
                        result => result.Score);
            }
        }

        //User that hearts a player may also sugguest that they'd want to see levels created by them.
        List<int> heartedCreatorSlotIds = await database.Slots
            .AsNoTracking()
            .Where(s => seedUserIds.Contains(s.CreatorId))
            .Where(queryBuilder.Build())
            .Where(s => !database.VisitedLevels.Any(v => v.UserId == token.UserId && v.SlotId == s.SlotId))
            .OrderByDescending(s => s.SlotId)
            .Select(s => s.SlotId)
            .Take(MaxCandidatePool)
            .ToListAsync();

        HashSet<int> heartedCreatorSlotSet =
            heartedCreatorSlotIds.ToHashSet();

        //Combines 3 sources for recommendation candidates..
        //Sources are levels hearted by players that the user hearts
        //Levels hearted by your 'taste neighbors'
        //Abd keveks created directly by players you heart
        HashSet<int> allCandidateIds = directScores.Keys
            .Concat(neighborScores.Keys)
            .Concat(heartedCreatorSlotIds)
            .ToHashSet();
        if (allCandidateIds.Count == 0)
        {
            return new List<ScoredSlot>();
        }

        //Calculates a score before loading all the level metadata.
        //PrevSearchScore - number of directly hearted players that support a level
        //SearchScore - taste neighbors and added points for directly hearted creator.
        List<int> candidateIds = allCandidateIds
            .Select(slotId =>
            {
                int prevSearchScore =
                    directScores.GetValueOrDefault(slotId);

                int neighborScore =
                    neighborScores.GetValueOrDefault(slotId);

                int creatorBonus =
                    heartedCreatorSlotSet.Contains(slotId)
                        ? 1
                        : 0;

                int searchScore =
                    prevSearchScore +
                    neighborScore +
                    creatorBonus;

                return new
                {
                    SlotId = slotId,
                    SearchScore = searchScore,
                    PrevSearchScore = prevSearchScore,
                };
            })
            .OrderByDescending(result => result.SearchScore)
            .ThenByDescending(result => result.PrevSearchScore)
            .ThenBy(result => result.SlotId)
            .Take(MaxCandidatePool)
            .Select(result => result.SlotId)
            .ToList();

        //Loads the levels and applies the standard slot filters, and ensures that your played levels are removed here
        List<SlotEntity> slots = await database.Slots
            .AsNoTracking()
            .Where(s => candidateIds.Contains(s.SlotId))
            .Where(queryBuilder.Build())
            .Where(s =>
                !database.VisitedLevels.Any(v =>
                    v.UserId == token.UserId &&
                    v.SlotId == s.SlotId))
            .ToListAsync();

        if (slots.Count == 0)
        {
            return new List<ScoredSlot>();
        }

        List<int> validSlotIds = slots
            .Select(s => s.SlotId)
            .ToList();

        //Hearts and likes are used ONLY to break a tie between levels
        Dictionary<int, int> heartCounts = await database.HeartedLevels
            .AsNoTracking()
            .Where(h => validSlotIds.Contains(h.SlotId))
            .GroupBy(h => h.SlotId)
            .ToDictionaryAsync(
                group => group.Key,
                group => group.Count());

        Dictionary<int, int> likeCounts = await database.RatedLevels
            .AsNoTracking()
            .Where(r =>
                validSlotIds.Contains(r.SlotId) &&
                r.Rating == 1)
            .GroupBy(r => r.SlotId)
            .ToDictionaryAsync(
                group => group.Key,
                group => group.Count());

        //This produces the final recommendation result objects.
        List<ScoredSlot> recommendations = slots
            .Select(slot =>
            {
                int prevSearchScore =
                    directScores.GetValueOrDefault(slot.SlotId);

                int neighborScore =
                    neighborScores.GetValueOrDefault(slot.SlotId);

                int creatorBonus =
                    seedUserIds.Contains(slot.CreatorId)
                        ? 1
                        : 0;

                int searchScore =
                    prevSearchScore +
                    neighborScore +
                    creatorBonus;

                return new ScoredSlot(
                    slot,
                    searchScore,
                    prevSearchScore,
                    heartCounts.GetValueOrDefault(slot.SlotId),
                    likeCounts.GetValueOrDefault(slot.SlotId));
            })
            .Where(result => result.SearchScore > 0)
            .OrderByDescending(result => result.SearchScore)
            .ThenByDescending(result => result.PrevSearchScore)
            .ThenByDescending(result => result.Hearts)
            .ThenByDescending(result => result.Likes)
            .ThenByDescending(result => result.Slot.SlotId)
            .ToList();

        return recommendations;
    }

    //Converts the scored recommendation into a regular slot.
    public static ILbpSerializable CreateSerializableSlot(ScoredSlot recommendation, GameTokenEntity token)
{
    SlotBase serialized = SlotBase.CreateFromEntity(recommendation.Slot, token);

    if (serialized is GameUserSlot userSlot)
    {
        userSlot.SearchScore =
            recommendation.SearchScore;

        userSlot.PrevSearchScore =
            recommendation.PrevSearchScore;
    }

    return serialized;
}

    public override async Task<GameCategory> Serialize(
        DatabaseContext database,
        GameTokenEntity token,
        SlotQueryBuilder queryBuilder,
        int numResults = 1)
    {
        List<ScoredSlot> recommendations =
            await this.GetScoredItems(
                database,
                token,
                queryBuilder);

        List<ILbpSerializable> serializedSlots =
            recommendations
                .Take(numResults)
                .Select(recommendation =>
                    CreateSerializableSlot(
                        recommendation,
                        token))
                .ToList();

        return GameCategory.CreateFromEntity(
            this,
            new GenericSerializableList(
                serializedSlots,
                recommendations.Count,
                numResults + 1));
    }
}