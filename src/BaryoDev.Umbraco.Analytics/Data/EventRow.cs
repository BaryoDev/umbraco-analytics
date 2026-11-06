using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace BaryoDev.Umbraco.Analytics.Data;

/// <summary>One page view. There is deliberately no column for an IP address or User-Agent.</summary>
[TableName(TableName)]
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[ExplicitColumns]
public sealed class EventRow
{
    public const string TableName = "baryoAnalyticsEvent";

    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public long Id { get; set; }

    [Column("OccurredUtc")]
    public DateTime OccurredUtc { get; set; }

    [Column("Day")]
    [Index(IndexTypes.NonClustered, Name = "IX_baryoAnalyticsEvent_Day")]
    public DateTime Day { get; set; }

    [Column("VisitorId")]
    [Length(32)]
    [Index(IndexTypes.NonClustered, Name = "IX_baryoAnalyticsEvent_Visitor", ForColumns = "VisitorId,OccurredUtc")]
    public string VisitorId { get; set; } = string.Empty;

    [Column("VisitId")]
    [Length(32)]
    public string VisitId { get; set; } = string.Empty;

    [Column("Path")]
    [Length(500)]
    public string Path { get; set; } = "/";

    [Column("ContentKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ContentKey { get; set; }

    [Column("ReferrerHost")]
    [Length(255)]
    public string ReferrerHost { get; set; } = string.Empty;

    [Column("UtmSource")]
    [Length(255)]
    public string UtmSource { get; set; } = string.Empty;

    [Column("UtmMedium")]
    [Length(255)]
    public string UtmMedium { get; set; } = string.Empty;

    [Column("UtmCampaign")]
    [Length(255)]
    public string UtmCampaign { get; set; } = string.Empty;

    [Column("Device")]
    [Length(16)]
    public string Device { get; set; } = string.Empty;

    [Column("Browser")]
    [Length(32)]
    public string Browser { get; set; } = string.Empty;

    [Column("Country")]
    [Length(2)]
    public string Country { get; set; } = string.Empty;
}
