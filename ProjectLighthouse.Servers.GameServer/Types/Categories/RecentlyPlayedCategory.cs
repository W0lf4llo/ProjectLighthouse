#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

using LBPUnion.ProjectLighthouse.Database;
using LBPUnion.ProjectLighthouse.Filter;
using LBPUnion.ProjectLighthouse.Types.Entities.Interaction;
using LBPUnion.ProjectLighthouse.Types.Entities.Level;
using LBPUnion.ProjectLighthouse.Types.Entities.Token;

using Microsoft.EntityFrameworkCore;

namespace LBPUnion.ProjectLighthouse.Servers.GameServer.Types.Categories;

public class RecentlyPlayedCategory : SlotCategory
{
    public override string Name { get; set; } = "Recently Played";
    public override string Description { get; set; } = "Your recently played content";
    public override string IconHash { get; set; } = "g820616";
    public override string Endpoint { get; set; } = "recently_played";
    public override string Tag => "my_recently_played";
    public override string[] Sorts { get; } =
    {
      "relevance",
    };

    public override IQueryable<SlotEntity> GetItems(
        DatabaseContext database,
        GameTokenEntity token,
        SlotQueryBuilder queryBuilder)
    {
        RecentlyPlayedEntity? recentlyPlayed = database.RecentlyPlayed
            .AsNoTracking()
            .FirstOrDefault(r => r.UserId == token.UserId);

        if (recentlyPlayed == null || recentlyPlayed.SlotIds.Count == 0)
            return database.Slots.Where(_ => false);

        List<int> slotIds = recentlyPlayed.SlotIds
            .Take(30)
            .ToList();

        ParameterExpression slotParameter =
            Expression.Parameter(typeof(SlotEntity), "slot");

        MemberExpression slotIdProperty =
            Expression.Property(
                slotParameter,
                nameof(SlotEntity.SlotId));

        Expression orderExpression =
            Expression.Constant(slotIds.Count);

        for (int i = slotIds.Count - 1; i >= 0; i--)
        {
            orderExpression = Expression.Condition(
                Expression.Equal(
                    slotIdProperty,
                    Expression.Constant(slotIds[i])),
                Expression.Constant(i),
                orderExpression);
        }

        Expression<Func<SlotEntity, int>> ordering =
            Expression.Lambda<Func<SlotEntity, int>>(
                orderExpression,
                slotParameter);

        return database.Slots
            .Where(s => slotIds.Contains(s.SlotId))
            .Where(queryBuilder.Build())
            .OrderBy(ordering);
    }
}
