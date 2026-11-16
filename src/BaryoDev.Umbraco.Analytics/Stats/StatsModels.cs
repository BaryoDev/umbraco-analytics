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

/// <summary>One UTC hour. Hour is an ISO 8601 UTC instant such as 2026-11-07T13:00:00Z.</summary>
public sealed record HourPoint(string Hour, int Visitors, int Visits, int PageViews);

/// <summary>The rolling last 24 hours: 24 hourly buckets ending with the hour that holds now.</summary>
public sealed record LastDay(
    string From,
    string To,
    Totals Totals,
    IReadOnlyList<HourPoint> Hourly,
    IReadOnlyList<Ranked> TopPages,
    IReadOnlyList<Ranked> TopReferrers);

/// <summary>Row shape for the grouped SQL queries. Names match the select aliases.</summary>
public sealed class CountRow
{
    public string Value { get; set; } = string.Empty;
    public DateTime Day { get; set; }
    public int Visitors { get; set; }
    public int Visits { get; set; }
    public int PageViews { get; set; }
}
