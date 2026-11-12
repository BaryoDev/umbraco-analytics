using System.Reflection;
using BaryoDev.Umbraco.Analytics.Collect;
using BaryoDev.Umbraco.Analytics.Data;
using BaryoDev.Umbraco.Analytics.Retention;
using BaryoDev.Umbraco.Analytics.Stats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.Install;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace BaryoDev.Umbraco.Analytics.IntegrationTests;

[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public class AnalyticsDataTests : UmbracoIntegrationTest
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private AnalyticsOptions _options = new();
    private FakeTimeProvider _time = new(Now);

    public class RuntimeProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_Level")
            {
                return RuntimeLevel.Run;
            }
            Type type = targetMethod!.ReturnType;
            return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }

    [SetUp]
    public async Task RunPackageMigration()
    {
        _options = new AnalyticsOptions { RetentionDays = 3 };
        _time = new FakeTimeProvider(Now);

        var runner = Services.GetRequiredService<PackageMigrationRunner>();
        await runner.RunPackageMigrationsIfPendingAsync("BaryoDev.Umbraco.Analytics");

        Assert.That(TableExists(EventRow.TableName), Is.True);
        Assert.That(TableExists(DailyRow.TableName), Is.True);
    }

    private bool TableExists(string name)
    {
        using var scope = ScopeProvider.CreateScope(autoComplete: true);
        return scope.Database.SqlContext.SqlSyntax.DoesTableExist(scope.Database, name);
    }

    private IOptions<AnalyticsOptions> Opts => Options.Create(_options);

    private static Hit HitAt(string visitor, DateTime utc, string path = "/", string device = "Desktop")
        => new(utc, visitor, path, null, "", "", "", "", device, "Chrome", "");

    private static DateTime At(int month, int day, int hour = 12, int minute = 0)
        => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    // Runs the real writer: queued hits are saved by StartAsync and StopAsync.
    private async Task Write(params Hit[] hits)
    {
        var queue = new EventQueue(Opts);
        foreach (Hit h in hits)
        {
            Assert.That(queue.TryEnqueue(h), Is.True);
        }
        var writer = new EventWriter(
            queue, ScopeProvider, DispatchProxy.Create<IRuntimeState, RuntimeProxy>(), Opts, NullLogger<EventWriter>.Instance);
        await writer.StartAsync(CancellationToken.None);
        await writer.StopAsync(CancellationToken.None);
    }

    private List<EventRow> Events()
    {
        using var scope = ScopeProvider.CreateScope(autoComplete: true);
        return scope.Database.Fetch<EventRow>($"SELECT * FROM {EventRow.TableName} ORDER BY Id");
    }

    private List<DailyRow> Daily()
    {
        using var scope = ScopeProvider.CreateScope(autoComplete: true);
        return scope.Database.Fetch<DailyRow>($"SELECT * FROM {DailyRow.TableName} ORDER BY Id");
    }

    private void Seed(IEnumerable<EventRow> rows)
    {
        using var scope = ScopeProvider.CreateScope();
        foreach (EventRow row in rows)
        {
            scope.Database.Insert(row);
        }
        scope.Complete();
    }

    private static EventRow Row(DateTime utc, string visitor, string visit, string path = "/")
        => new()
        {
            OccurredUtc = utc,
            Day = utc.Date,
            VisitorId = visitor,
            VisitId = visit,
            Path = path,
            Device = "Desktop",
            Browser = "Chrome",
        };

    private RetentionJob Job() => new(
        ScopeProvider, DispatchProxy.Create<IRuntimeState, RuntimeProxy>(), GetRequiredService<IServerRoleAccessor>(),
        Opts, _time, NullLogger<RetentionJob>.Instance);

    [TestCase(7)]
    [TestCase(30)]
    [TestCase(90)]
    public void Api_accepts_the_three_ranges(int days)
    {
        var controller = new BaryoDev.Umbraco.Analytics.Api.AnalyticsController(Stats());
        Assert.That(controller.GetSummary(days), Is.TypeOf<Microsoft.AspNetCore.Mvc.OkObjectResult>());
    }

    private StatsService Stats() => new(ScopeProvider, _time);

    [Test]
    public void Migration_runs_twice_without_error()
    {
        Assert.That(TableExists(EventRow.TableName), Is.True);
        Assert.That(TableExists(DailyRow.TableName), Is.True);

        var executor = Services.GetRequiredService<IMigrationPlanExecutor>();
        Assert.DoesNotThrowAsync(async () => await executor.ExecutePlanAsync(new AnalyticsMigrationPlan(), string.Empty));

        Assert.That(TableExists(EventRow.TableName), Is.True);
        Assert.That(TableExists(DailyRow.TableName), Is.True);
    }

    [Test]
    public async Task Writer_stitches_hits_within_30_minutes_into_one_visit()
    {
        await Write(
            HitAt("v1", At(11, 7, 9, 0)),
            HitAt("v1", At(11, 7, 9, 20)),
            HitAt("v1", At(11, 7, 9, 50)),
            HitAt("v1", At(11, 7, 10, 21)),
            HitAt("v2", At(11, 7, 9, 5)));

        var rows = Events();
        Assert.That(rows, Has.Count.EqualTo(5));
        var v1 = rows.Where(r => r.VisitorId == "v1").ToList();
        Assert.That(v1[0].VisitId, Is.Not.Empty);
        Assert.That(v1[1].VisitId, Is.EqualTo(v1[0].VisitId));
        Assert.That(v1[2].VisitId, Is.EqualTo(v1[0].VisitId), "exactly 30 minutes after the last hit is still the same visit");
        Assert.That(v1[3].VisitId, Is.Not.EqualTo(v1[0].VisitId), "31 minutes after the last hit starts a new visit");
        Assert.That(rows.Single(r => r.VisitorId == "v2").VisitId, Is.Not.EqualTo(v1[0].VisitId));
        Assert.That(v1[0].Day, Is.EqualTo(At(11, 7).Date));
    }

    [Test]
    public async Task Writer_continues_a_visit_from_the_database_after_a_restart()
    {
        await Write(HitAt("v1", At(11, 7, 9, 0)));
        await Write(HitAt("v1", At(11, 7, 9, 25)));

        var rows = Events();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[1].VisitId, Is.EqualTo(rows[0].VisitId));
    }

    [Test]
    public void Retention_rolls_a_day_then_deletes_its_raw_rows()
    {
        // 2,500 rows on the old day is three delete chunks. Cutoff is 2026-11-04.
        DateTime old = At(11, 1, 10);
        Seed(Enumerable.Range(0, 2500).Select(i => Row(old.AddSeconds(i), "v" + (i % 50), "s" + (i % 100), "/p" + (i % 5))));
        Seed([Row(At(11, 5), "v1", "s1"), Row(At(11, 6), "v2", "s2")]);

        int rolled = Job().Run();

        Assert.That(rolled, Is.EqualTo(1));
        var left = Events();
        Assert.That(left, Has.Count.EqualTo(2), "only the rows inside the retention window remain");
        Assert.That(left.All(r => r.Day >= At(11, 5).Date), Is.True);

        var daily = Daily();
        var total = daily.Single(d => d.Dimension == "total");
        Assert.That((total.Day, total.PageViews, total.Visitors, total.Visits), Is.EqualTo((old.Date, 2500, 50, 100)));
        var pages = daily.Where(d => d.Dimension == "page").OrderBy(d => d.Value).ToList();
        Assert.That(pages.Select(p => p.Value), Is.EqualTo(new[] { "/p0", "/p1", "/p2", "/p3", "/p4" }));
        Assert.That(pages.All(p => p.PageViews == 500), Is.True);

        Assert.That(Job().Run(), Is.EqualTo(0));
        Assert.That(Daily(), Has.Count.EqualTo(daily.Count));
    }

    [Test]
    public void Rollup_writes_more_values_than_one_insert_chunk()
    {
        DateTime old = At(11, 1, 10);
        Seed(Enumerable.Range(0, 450).Select(i => Row(old.AddSeconds(i), "v" + i, "s" + i, "/unique" + i)));

        Job().Run();

        Assert.That(Daily().Count(d => d.Dimension == "page"), Is.EqualTo(450));
        Assert.That(Daily().Single(d => d.Dimension == "total").PageViews, Is.EqualTo(450));
    }

    [Test]
    public void Page_and_referrer_daily_rows_are_deleted_after_another_retention_period()
    {
        // RetentionDays is 3, now is 2026-11-07, so the cutoff is 11-04 and the purge horizon is 11-01.
        DailyRow D(DateTime day, string dimension) => new() { Day = day.Date, Dimension = dimension, Value = "/secret/token", Visitors = 1, Visits = 1, PageViews = 1 };
        using (var scope = ScopeProvider.CreateScope())
        {
            foreach (var row in new[]
            {
                D(At(10, 30), "page"), D(At(10, 30), "referrer"), D(At(10, 30), "device"), D(At(10, 30), "total"),
                D(At(11, 1), "page"), D(At(11, 2), "referrer"),
            })
            {
                scope.Database.Insert(row);
            }
            scope.Complete();
        }

        Job().Run();

        var left = Daily().Select(d => (d.Day.Day, d.Dimension)).ToList();
        Assert.That(left, Is.EquivalentTo(new[] { (30, "device"), (30, "total"), (1, "page"), (2, "referrer") }));
    }

    [Test]
    public void A_late_event_for_a_rolled_day_is_deleted_and_not_counted()
    {
        Seed([Row(At(11, 1), "v1", "s1"), Row(At(11, 1, 13), "v2", "s2")]);
        Job().Run();
        Seed([Row(At(11, 1, 23), "v3", "s3")]);

        Assert.That(Job().Run(), Is.EqualTo(1));

        Assert.That(Events(), Is.Empty);
        Assert.That(Daily().Single(d => d.Dimension == "total").PageViews, Is.EqualTo(2));
    }

    [Test]
    public void Stats_read_rolled_and_raw_days_without_double_counting()
    {
        Seed(
        [
            Row(At(11, 1, 9), "a", "s1", "/x"),
            Row(At(11, 1, 10), "a", "s1", "/y"),
            Row(At(11, 1, 11), "b", "s2", "/x"),
            Row(At(11, 5, 9), "a", "s3", "/x"),
            Row(At(11, 5, 10), "c", "s4", "/z"),
        ]);
        Job().Run();
        // The rolled day is now daily rows only. A late raw row for it must not be added again.
        Seed([Row(At(11, 1, 23), "d", "s5", "/x")]);

        Summary summary = Stats().Get(10);

        Assert.That(summary.Totals, Is.EqualTo(new Totals(4, 4, 5)));
        Assert.That(summary.Daily, Has.Count.EqualTo(10));
        Assert.That(summary.Daily.Single(d => d.Day == "2026-11-01").PageViews, Is.EqualTo(3));
        Assert.That(summary.Daily.Single(d => d.Day == "2026-11-05").PageViews, Is.EqualTo(2));
        Assert.That(summary.Daily.Sum(d => d.PageViews), Is.EqualTo(summary.Totals.PageViews));
        Assert.That(summary.TopPages.Select(p => (p.Value, p.PageViews)),
            Is.EqualTo(new[] { ("/x", 3), ("/y", 1), ("/z", 1) }));
    }

    [Test]
    public void Top_pages_query_runs_on_sqlite()
    {
        var rows = new List<EventRow>();
        for (int page = 0; page < 12; page++)
        {
            for (int n = 0; n <= page; n++)
            {
                rows.Add(Row(At(11, 6, 8, n), "v" + page, "s" + page, "/page" + page.ToString("00")));
            }
        }
        Seed(rows);

        Summary summary = Stats().Get(7);

        Assert.That(summary.TopPages, Has.Count.EqualTo(StatsService.TopCount));
        Assert.That(summary.TopPages[0], Is.EqualTo(new Ranked("/page11", 1, 1, 12)));
        Assert.That(summary.TopPages.Select(p => p.PageViews), Is.Ordered.Descending);
        Assert.That(summary.Devices.Single().Value, Is.EqualTo("Desktop"));
        Assert.That(summary.Countries, Is.Empty);
    }

    [Test]
    public void Secret_is_created_once()
    {
        var keyValues = GetRequiredService<IKeyValueService>();
        var first = new SecretProvider(Options.Create(new AnalyticsOptions()), keyValues, ScopeProvider);
        var second = new SecretProvider(Options.Create(new AnalyticsOptions()), keyValues, ScopeProvider);

        string a = first.Get();
        string b = second.Get();

        Assert.That(a, Has.Length.EqualTo(64));
        Assert.That(b, Is.EqualTo(a));
        Assert.That(keyValues.GetValue(SecretProvider.Key), Is.EqualTo(a));
    }

    [Test]
    public void Configured_secret_wins_and_is_not_stored()
    {
        var keyValues = GetRequiredService<IKeyValueService>();
        var provider = new SecretProvider(Options.Create(new AnalyticsOptions { Secret = "from-config" }), keyValues, ScopeProvider);

        Assert.That(provider.Get(), Is.EqualTo("from-config"));
        Assert.That(keyValues.GetValue(SecretProvider.Key), Is.Null);
    }
}
