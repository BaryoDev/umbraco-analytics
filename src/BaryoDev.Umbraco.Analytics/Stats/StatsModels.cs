namespace BaryoDev.Umbraco.Analytics.Stats;

public sealed record Totals(int Visitors, int Visits, int PageViews);

public sealed record DayPoint(string Day, int Visitors, int Visits, int PageViews);

public sealed record Ranked(string Value, int Visitors, int Visits, int PageViews);

public sealed record Summary(
    int Days,
    Totals Totals,
    IReadOnlyList<DayPoint> Daily,
    IReadOnlyList<Ranked> TopPages,
    IReadOnlyList<Ranked> TopReferrers,
    IReadOnlyList<Ranked> Devices,
    IReadOnlyList<Ranked> Browsers,
    IReadOnlyList<Ranked> Countries);

/// <summary>Row shape for the grouped SQL queries. Names match the select aliases.</summary>
public sealed class CountRow
{
    public string Value { get; set; } = string.Empty;
    public DateTime Day { get; set; }
    public int Visitors { get; set; }
    public int Visits { get; set; }
    public int PageViews { get; set; }
}
