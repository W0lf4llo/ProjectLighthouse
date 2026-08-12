#nullable enable
using LBPUnion.ProjectLighthouse.Database;
using LBPUnion.ProjectLighthouse.Extensions;
using LBPUnion.ProjectLighthouse.Filter;
using LBPUnion.ProjectLighthouse.Filter.Sorts;
using LBPUnion.ProjectLighthouse.Types.Entities.Level;
using LBPUnion.ProjectLighthouse.Types.Entities.Token;

namespace LBPUnion.ProjectLighthouse.Servers.GameServer.Types.Categories;

public class RecommendedCategory : SlotCategory
{
    public override string Name { get; set; } = "Recommended For You";
    public override string Description { get; set; } = "Stuff we think you'll like";
    public override string IconHash { get; set; } = "g820625";
    public override string Endpoint { get; set; } = "recommended";
    public override string Tag => "recommended";
    public override IQueryable<SlotEntity> GetItems(DatabaseContext database, GameTokenEntity token, SlotQueryBuilder queryBuilder)
    {
        IQueryable<int> heartedCreatorIds = database.HeartedProfiles
                .Where(h => h.UserId == token.UserId)
                .Select(h => h.HeartedUserId);

        IQueryable<SlotEntity> query = database.Slots
                .Where(s => heartedCreatorIds.Contains(s.CreatorId))
                .Where(s =>
                    !database.VisitedLevels.Any(v =>
                        v.UserId == token.UserId &&
                        v.SlotId == s.SlotId))
                .Where(queryBuilder.Build());

        return query.ApplyOrdering(
            new SlotSortBuilder<SlotEntity>()
                .AddSort(new UniquePlaysTotalSort())
                .AddSort(new LastUpdatedSort()));
    }
}