using LBPUnion.ProjectLighthouse.Types.Entities.Profile;
using LBPUnion.ProjectLighthouse.Types.Users;
using Microsoft.EntityFrameworkCore;

namespace LBPUnion.ProjectLighthouse.Types.Entities.Interaction;

[PrimaryKey(
    nameof(UserId),
    nameof(SlotId),
    nameof(ChildSlotId),
    nameof(ScoreType),
    nameof(GameVersion),
    nameof(IsStory))]
public class ScoreboardPinQualificationEntity
{
    public int UserId { get; set; }

    public UserEntity User { get; set; } = null!;

    public int SlotId { get; set; }

    public int ChildSlotId { get; set; }

    public int ScoreType { get; set; }

    public GameVersion GameVersion { get; set; }

    public bool IsStory { get; set; }
}
