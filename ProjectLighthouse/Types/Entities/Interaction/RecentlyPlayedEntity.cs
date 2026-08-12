#nullable enable

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using LBPUnion.ProjectLighthouse.Types.Entities.Profile;
using Microsoft.EntityFrameworkCore;

namespace LBPUnion.ProjectLighthouse.Types.Entities.Interaction;

[Index(nameof(UserId), IsUnique = true)]
public class RecentlyPlayedEntity
{
    [Key]
    public int RecentlyPlayedId { get; set; }
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;
    public List<int> SlotIds { get; set; } = new();
    public List<long> LastPlayedAt { get; set; } = new();

}