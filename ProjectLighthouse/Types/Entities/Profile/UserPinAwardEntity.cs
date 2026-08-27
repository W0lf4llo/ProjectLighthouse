using LBPUnion.ProjectLighthouse.Types.Users;
using Microsoft.EntityFrameworkCore;

namespace LBPUnion.ProjectLighthouse.Types.Entities.Profile;

/// <summary>
/// Stores an awarded pin for one user in one game.
///
/// Awards are keyed by PinId.
/// </summary>
[PrimaryKey(nameof(UserId), nameof(GameVersion), nameof(PinId))]
public class UserPinAwardEntity
{
    public int UserId { get; set; }

    public UserEntity User { get; set; } = null!;

    public GameVersion GameVersion { get; set; }

    public uint PinId { get; set; }

    public long AwardCount { get; set; }
}