using BaryoDev.Umbraco.Analytics.Data;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.Analytics.Stats;

/// <summary>Reads raw events for the recent window and daily rows for older days.</summary>
public sealed class StatsService(IScopeProvider scopes, IOptions<AnalyticsOptions> options, TimeProvider clock)
{
    public const int TopCount = 10;
    private const string Counts = "COUNT(*) AS PageViews, COUNT(DISTINCT VisitorId) AS Visitors, COUNT(DISTINCT VisitId) AS Visits";

    public Summary Get(int days)
    {
        DateTime today = clock.GetUtcNow().UtcDateTime.Date;
        DateTime from = today.AddDays(-(days - 1));
        DateTime rawFloor = today.AddDays(-options.Value.RetentionDays);

        using var scope = scopes.CreateScope(autoComplete: true);
        var db = scope.Database;

        DateTime rawFrom = from > rawFloor ? from : rawFloor;
        var rawDaily = db.Fetch<CountRow>(
            $"SELECT Day, {Counts} FROM {EventRow.TableName} WHERE Day >= @0 GROUP BY Day", rawFrom);
        var rolledDaily = from < rawFloor
            ? db.Fetch<CountRow>(
                $"SELECT Day, Visitors, Visits, PageViews FROM {DailyRow.TableName} WHERE Dimension = 'total' AND Day >= @0 AND Day < @1",
                from, rawFloor)
            : [];

        var byDay = rawDaily.Concat(rolledDaily).GroupBy(r => r.Day.Date)
            .ToDictionary(g => g.Key, g => new Totals(g.Sum(r => r.Visitors), g.Sum(r => r.Visits), g.Sum(r => r.PageViews)));
        var daily = Enumerable.Range(0, days).Select(i =>
        {
            DateTime d = from.AddDays(i);
            Totals t = byDay.GetValueOrDefault(d, new Totals(0, 0, 0));
            return new DayPoint(d.ToString("yyyy-MM-dd"), t.Visitors, t.Visits, t.PageViews);
        }).ToList();

        // Distinct counts across the raw window are exact. Rolled days are added per day.
        var rawTotal = db.Fetch<CountRow>($"SELECT {Counts} FROM {EventRow.TableName} WHERE Day >= @0", rawFrom).FirstOrDefault();
        var totals = new Totals(
            (rawTotal?.Visitors ?? 0) + rolledDaily.Sum(r => r.Visitors),
            (rawTotal?.Visits ?? 0) + rolledDaily.Sum(r => r.Visits),
            (rawTotal?.PageViews ?? 0) + rolledDaily.Sum(r => r.PageViews));

        return new Summary(
            days, totals, daily,
            Top(db, "Path", "page", from, rawFrom, rawFloor),
            Top(db, "ReferrerHost", "referrer", from, rawFrom, rawFloor),
            Top(db, "Device", "device", from, rawFrom, rawFloor),
            Top(db, "Browser", "browser", from, rawFrom, rawFloor),
            Top(db, "Country", "country", from, rawFrom, rawFloor, skipEmpty: true));
    }

    // column and dimension are fixed strings from this file, never user input.
    private static List<Ranked> Top(
        NPoco.IDatabase db, string column, string dimension, DateTime from, DateTime rawFrom, DateTime rawFloor,
        bool skipEmpty = false)
    {
        string where = skipEmpty ? $"AND {column} <> ''" : string.Empty;
        var raw = db.Fetch<CountRow>(
            $"SELECT {column} AS Value, {Counts} FROM {EventRow.TableName} WHERE Day >= @0 {where} GROUP BY {column}", rawFrom);
        var rolled = from < rawFloor
            ? db.Fetch<CountRow>(
                $"SELECT Value, SUM(Visitors) AS Visitors, SUM(Visits) AS Visits, SUM(PageViews) AS PageViews FROM {DailyRow.TableName} " +
                $"WHERE Dimension = @0 AND Day >= @1 AND Day < @2 {(skipEmpty ? "AND Value <> ''" : "")} GROUP BY Value",
                dimension, from, rawFloor)
            : [];

        return raw.Concat(rolled).GroupBy(r => r.Value)
            .Select(g => new Ranked(g.Key, g.Sum(r => r.Visitors), g.Sum(r => r.Visits), g.Sum(r => r.PageViews)))
            .OrderByDescending(r => r.PageViews).ThenBy(r => r.Value, StringComparer.Ordinal)
            .Take(TopCount).ToList();
    }
}
