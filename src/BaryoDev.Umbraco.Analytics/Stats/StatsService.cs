using BaryoDev.Umbraco.Analytics.Data;
using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.Analytics.Stats;

/// <summary>
/// Reads raw events and daily rows. A day that has a daily total row has been rolled up, so its raw rows
/// (which may still exist while old rows are being deleted) are ignored and the daily rows are used.
/// </summary>
public sealed class StatsService(IScopeProvider scopes, TimeProvider clock)
{
    public const int TopCount = 10;
    // Each source returns a few more than TopCount so the merge of raw and rolled rows stays accurate.
    private const int PerSource = 50;
    private const string Counts = "COUNT(*) AS PageViews, COUNT(DISTINCT VisitorId) AS Visitors, COUNT(DISTINCT VisitId) AS Visits";
    private static readonly string NotRolled =
        $"Day NOT IN (SELECT Day FROM {DailyRow.TableName} WHERE Dimension = 'total')";

    public Summary Get(int days)
    {
        DateTime today = clock.GetUtcNow().UtcDateTime.Date;
        DateTime from = today.AddDays(-(days - 1));

        using var scope = scopes.CreateScope(autoComplete: true);
        var db = scope.Database;

        var rawDaily = db.Fetch<CountRow>(
            $"SELECT Day, {Counts} FROM {EventRow.TableName} WHERE Day >= @0 AND {NotRolled} GROUP BY Day", from);
        var rolledDaily = db.Fetch<CountRow>(
            $"SELECT Day, Visitors, Visits, PageViews FROM {DailyRow.TableName} WHERE Dimension = 'total' AND Day >= @0", from);

        var byDay = rawDaily.Concat(rolledDaily).GroupBy(r => r.Day.Date)
            .ToDictionary(g => g.Key, g => new Totals(g.Sum(r => r.Visitors), g.Sum(r => r.Visits), g.Sum(r => r.PageViews)));
        var daily = Enumerable.Range(0, days).Select(i =>
        {
            DateTime d = from.AddDays(i);
            Totals t = byDay.GetValueOrDefault(d, new Totals(0, 0, 0));
            return new DayPoint(d.ToString("yyyy-MM-dd"), t.Visitors, t.Visits, t.PageViews);
        }).ToList();

        // Distinct counts across the raw days are exact. Rolled days are added per day.
        var rawTotal = db.Fetch<CountRow>(
            $"SELECT {Counts} FROM {EventRow.TableName} WHERE Day >= @0 AND {NotRolled}", from).FirstOrDefault();
        var totals = new Totals(
            (rawTotal?.Visitors ?? 0) + rolledDaily.Sum(r => r.Visitors),
            (rawTotal?.Visits ?? 0) + rolledDaily.Sum(r => r.Visits),
            (rawTotal?.PageViews ?? 0) + rolledDaily.Sum(r => r.PageViews));

        return new Summary(
            days, totals, daily,
            Top(db, "Path", "page", from),
            Top(db, "ReferrerHost", "referrer", from),
            Top(db, "Device", "device", from),
            Top(db, "Browser", "browser", from),
            Top(db, "Country", "country", from, skipEmpty: true));
    }

    // column and dimension are fixed strings from this file, never user input. Ranking and the limit run in SQL.
    private static List<Ranked> Top(NPoco.IDatabase db, string column, string dimension, DateTime from, bool skipEmpty = false)
    {
        string where = skipEmpty ? $"AND {column} <> ''" : string.Empty;
        var raw = db.SkipTake<CountRow>(0, PerSource,
            $"SELECT {column} AS Value, {Counts} FROM {EventRow.TableName} WHERE Day >= @0 AND {NotRolled} {where} " +
            $"GROUP BY {column} ORDER BY COUNT(*) DESC, {column}", from);
        var rolled = db.SkipTake<CountRow>(0, PerSource,
            $"SELECT Value, SUM(Visitors) AS Visitors, SUM(Visits) AS Visits, SUM(PageViews) AS PageViews FROM {DailyRow.TableName} " +
            $"WHERE Dimension = @0 AND Day >= @1 {(skipEmpty ? "AND Value <> ''" : "")} GROUP BY Value ORDER BY SUM(PageViews) DESC, Value",
            dimension, from);

        return raw.Concat(rolled).GroupBy(r => r.Value)
            .Select(g => new Ranked(g.Key, g.Sum(r => r.Visitors), g.Sum(r => r.Visits), g.Sum(r => r.PageViews)))
            .OrderByDescending(r => r.PageViews).ThenBy(r => r.Value, StringComparer.Ordinal)
            .Take(TopCount).ToList();
    }
}
