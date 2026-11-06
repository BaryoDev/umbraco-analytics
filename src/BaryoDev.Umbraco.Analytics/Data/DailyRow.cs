using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace BaryoDev.Umbraco.Analytics.Data;

/// <summary>Daily totals kept after the raw events are deleted.</summary>
[TableName(TableName)]
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[ExplicitColumns]
public sealed class DailyRow
{
    public const string TableName = "baryoAnalyticsDaily";

    [Column("Id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public long Id { get; set; }

    [Column("Day")]
    [Index(IndexTypes.NonClustered, Name = "IX_baryoAnalyticsDaily_Day", ForColumns = "Day,Dimension")]
    public DateTime Day { get; set; }

    [Column("Dimension")]
    [Length(16)]
    public string Dimension { get; set; } = string.Empty;

    [Column("Value")]
    [Length(500)]
    public string Value { get; set; } = string.Empty;

    [Column("Visitors")]
    public int Visitors { get; set; }

    [Column("Visits")]
    public int Visits { get; set; }

    [Column("PageViews")]
    public int PageViews { get; set; }
}
