#nullable enable
using System.Xml.Serialization;

namespace LBPUnion.ProjectLighthouse.Types.Levels;

public class CategoryDefaults
{
    [XmlElement("gameFilter")]
    public string? GameFilter { get; set; }

    [XmlElement("dateFilterType")]
    public string? DateFilterType { get; set; }

    [XmlElement("includePlayed")]
    public bool? IncludePlayed { get; set; }

    [XmlElement("teamPicked")]
    public string? TeamPicked { get; set; }

    [XmlElement("blacklisted")]
    public string? Blacklisted { get; set; }

    public bool ShouldSerializeGameFilter() => !string.IsNullOrWhiteSpace(this.GameFilter);

    public bool ShouldSerializeDateFilterType() => !string.IsNullOrWhiteSpace(this.DateFilterType);

    public bool ShouldSerializeIncludePlayed() => this.IncludePlayed.HasValue;

    public bool ShouldSerializeTeamPicked() => !string.IsNullOrWhiteSpace(this.TeamPicked);

    public bool ShouldSerializeBlacklisted() => !string.IsNullOrWhiteSpace(this.Blacklisted);
}