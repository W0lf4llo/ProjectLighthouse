using LBPUnion.ProjectLighthouse.Types.Serialization;
using LBPUnion.ProjectLighthouse.Types.Users;
using Microsoft.EntityFrameworkCore;

namespace LBPUnion.ProjectLighthouse.Types.Entities.Profile;

[PrimaryKey(nameof(UserId), nameof(GameVersion), nameof(ProgressType))]
public class UserPinBaselineEntity
{
    public int UserId { get; set; }

    public UserEntity User { get; set; } = null!;

    public GameVersion GameVersion { get; set; }

    public uint ProgressType { get; set; }

    public double BaselineValue { get; set; }
}