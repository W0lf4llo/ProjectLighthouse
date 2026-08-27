using LBPUnion.ProjectLighthouse.Types.Entities.Profile;
using LBPUnion.ProjectLighthouse.Types.Serialization;
using LBPUnion.ProjectLighthouse.Types.Users;
using Microsoft.EntityFrameworkCore;

namespace LBPUnion.ProjectLighthouse.Types.Entities.Interaction;

[PrimaryKey(nameof(UserId), nameof(SlotId))]
public class TeamPickQualification
{
    public int UserId { get; set; }

    public UserEntity User { get; set; } = null!;

    public int SlotId { get; set; }

    public GameVersion GameVersion { get; set; }
}