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
        DateTime cutoff = clock.GetUtcNow().UtcDateTime.Date.AddDays(-options.Value.RetentionDays);
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

    private void RollUp(DateTime day)
    {
        const string counts = "COUNT(*) AS PageViews, COUNT(DISTINCT VisitorId) AS Visitors, COUNT(DISTINCT VisitId) AS Visits";
        using var scope = scopes.CreateScope();
        var db = scope.Database;

        // Redo the day from scratch so a run that stopped halfway cannot double count.
        db.Execute($"DELETE FROM {DailyRow.TableName} WHERE Day = @0", day);

        var total = db.Fetch<CountRow>($"SELECT {counts} FROM {EventRow.TableName} WHERE Day = @0", day).First();
        db.Insert(new DailyRow { Day = day, Dimension = "total", Value = string.Empty, Visitors = total.Visitors, Visits = total.Visits, PageViews = total.PageViews });

        foreach (var (column, dimension) in Dimensions)
        {
            var rows = db.Fetch<CountRow>(
                $"SELECT {column} AS Value, {counts} FROM {EventRow.TableName} WHERE Day = @0 GROUP BY {column}", day);
            foreach (CountRow r in rows)
            {
                db.Insert(new DailyRow { Day = day, Dimension = dimension, Value = r.Value, Visitors = r.Visitors, Visits = r.Visits, PageViews = r.PageViews });
            }
        }

        db.Execute($"DELETE FROM {EventRow.TableName} WHERE Day = @0", day);
        scope.Complete();
    }
}
