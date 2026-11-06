using BaryoDev.Umbraco.Analytics.Data;
using BaryoDev.Umbraco.Analytics.Stats;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.Analytics.Retention;

/// <summary>Rolls raw events older than RetentionDays into daily rows, then deletes them.</summary>
public sealed class RetentionJob(
    IScopeProvider scopes, IRuntimeState runtime, IServerRoleAccessor roles,
    IOptions<AnalyticsOptions> options, TimeProvider clock, ILogger<RetentionJob> logger) : BackgroundService
{
    private const int DaysPerRun = 60;
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    // Column in the event table and the dimension name it is stored under.
    private static readonly (string Column, string Dimension)[] Dimensions =
    [
        ("Path", "page"), ("ReferrerHost", "referrer"), ("Device", "device"), ("Browser", "browser"), ("Country", "country"),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (runtime.Level == RuntimeLevel.Run && roles.CurrentServerRole is ServerRole.Single or ServerRole.SchedulingPublisher)
                {
                    try
                    {
                        Run();
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        logger.LogError(e, "The analytics retention job failed and will try again.");
                    }
                }
                await Task.Delay(Every, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    public int Run()
    {
        DateTime cutoff = clock.GetUtcNow().UtcDateTime.Date.AddDays(-Math.Max(1, options.Value.RetentionDays));
        List<DateTime> days;
        using (var scope = scopes.CreateScope(autoComplete: true))
        {
            days = scope.Database.Fetch<DateTime>(
                $"SELECT DISTINCT Day FROM {EventRow.TableName} WHERE Day < @0 ORDER BY Day", cutoff)
                .Take(DaysPerRun).ToList();
        }

        foreach (DateTime day in days)
        {
            RollUp(day);
        }
        if (days.Count > 0)
        {
            logger.LogInformation("Analytics rolled up {Count} days older than {Cutoff:yyyy-MM-dd}.", days.Count, cutoff);
        }
        return days.Count;
    }

    private const int DeleteChunk = 1000;

    private void RollUp(DateTime day)
    {
        // Step 1, one transaction: write the daily rows unless they exist. A crash later leaves them in place,
        // so the next run only has to finish deleting. A late event for a rolled day is deleted, not added.
        using (var scope = scopes.CreateScope())
        {
            var db = scope.Database;
            if (db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {DailyRow.TableName} WHERE Day = @0 AND Dimension = 'total'", day) == 0)
            {
                const string counts = "COUNT(*) AS PageViews, COUNT(DISTINCT VisitorId) AS Visitors, COUNT(DISTINCT VisitId) AS Visits";
                db.Execute($"DELETE FROM {DailyRow.TableName} WHERE Day = @0", day);
                foreach (var (column, dimension) in Dimensions)
                {
                    var rows = db.Fetch<CountRow>(
                        $"SELECT {column} AS Value, {counts} FROM {EventRow.TableName} WHERE Day = @0 GROUP BY {column}", day);
                    foreach (CountRow r in rows)
                    {
                        db.Insert(new DailyRow { Day = day, Dimension = dimension, Value = r.Value, Visitors = r.Visitors, Visits = r.Visits, PageViews = r.PageViews });
                    }
                }
                var total = db.Fetch<CountRow>($"SELECT {counts} FROM {EventRow.TableName} WHERE Day = @0", day).First();
                // The total row goes in last: it marks the day as rolled.
                db.Insert(new DailyRow { Day = day, Dimension = "total", Value = string.Empty, Visitors = total.Visitors, Visits = total.Visits, PageViews = total.PageViews });
            }
            scope.Complete();
        }

        // Step 2: delete the raw rows in small chunks so the writer is never blocked for long.
        while (true)
        {
            using var scope = scopes.CreateScope();
            var ids = scope.Database.SkipTake<long>(0, DeleteChunk,
                $"SELECT Id FROM {EventRow.TableName} WHERE Day = @0 ORDER BY Id", day);
            if (ids.Count == 0)
            {
                break;
            }
            scope.Database.Execute($"DELETE FROM {EventRow.TableName} WHERE Id IN (@ids)", new { ids });
            scope.Complete();
        }
    }
}
