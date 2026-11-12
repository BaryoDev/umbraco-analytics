using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BaryoDev.Umbraco.Analytics.Data;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>Saves queued hits in batches and assigns the visit id.</summary>
public sealed class EventWriter(
    EventQueue queue, IScopeProvider scopes, IRuntimeState runtime,
    IOptions<AnalyticsOptions> options, ILogger<EventWriter> logger) : BackgroundService
{
    private const int BatchSize = 200;
    internal const int MaxRecent = 20_000;
    private readonly Dictionary<string, (string VisitId, DateTime Last)> _recent = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<Hit>(BatchSize);
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                if (runtime.Level != RuntimeLevel.Run)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                batch.Clear();
                while (batch.Count < BatchSize && queue.Reader.TryRead(out Hit? hit))
                {
                    batch.Add(hit);
                }

                try
                {
                    Save(batch);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Analytics could not save {Count} page views. They were dropped.", batch.Count);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // Save what is still queued, within the host shutdown time.
        var rest = new List<Hit>(BatchSize);
        while (!cancellationToken.IsCancellationRequested && runtime.Level == RuntimeLevel.Run)
        {
            rest.Clear();
            while (rest.Count < BatchSize && queue.Reader.TryRead(out Hit? hit))
            {
                rest.Add(hit);
            }
            if (rest.Count == 0)
            {
                break;
            }
            try
            {
                Save(rest);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Analytics could not save {Count} page views on shutdown. They were dropped.", rest.Count);
            }
        }
    }

    private void Save(List<Hit> batch)
    {
        TimeSpan idle = TimeSpan.FromMinutes(Math.Max(1, options.Value.VisitIdleMinutes));
        using var scope = scopes.CreateScope();
        foreach (Hit h in batch)
        {
            string visitId = VisitFor(scope, h, idle);
            scope.Database.Insert(new EventRow
            {
                OccurredUtc = h.OccurredUtc,
                Day = h.OccurredUtc.Date,
                VisitorId = h.VisitorId,
                VisitId = visitId,
                Path = Cut(h.Path, 500),
                ContentKey = h.ContentKey,
                ReferrerHost = Cut(h.ReferrerHost, 255),
                UtmSource = Cut(h.UtmSource, 255),
                UtmMedium = Cut(h.UtmMedium, 255),
                UtmCampaign = Cut(h.UtmCampaign, 255),
                Device = Cut(h.Device, 16),
                Browser = Cut(h.Browser, 32),
                Country = h.Country,
            });
        }
        scope.Complete();

        Prune(_recent, DateTime.UtcNow - idle, MaxRecent);
    }

    /// <summary>Drops visitors idle past the cutoff, then the least recent ones, so the map never stays above the cap.</summary>
    internal static void Prune(Dictionary<string, (string VisitId, DateTime Last)> recent, DateTime cutoff, int max)
    {
        if (recent.Count <= max)
        {
            return;
        }
        foreach (string k in recent.Where(p => p.Value.Last < cutoff).Select(p => p.Key).ToList())
        {
            recent.Remove(k);
        }
        if (recent.Count > max)
        {
            foreach (string k in recent.OrderBy(p => p.Value.Last).Take(recent.Count - max).Select(p => p.Key).ToList())
            {
                recent.Remove(k);
            }
        }
    }

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;

    private string VisitFor(IScope scope, Hit h, TimeSpan idle)
    {
        if (!_recent.TryGetValue(h.VisitorId, out var seen))
        {
            var last = scope.Database.SkipTake<EventRow>(0, 1, "WHERE VisitorId = @0 ORDER BY OccurredUtc DESC", h.VisitorId)
                .FirstOrDefault();
            seen = last is null ? (string.Empty, DateTime.MinValue) : (last.VisitId, last.OccurredUtc);
        }

        string id = seen.VisitId.Length > 0 && h.OccurredUtc - seen.Last <= idle
            ? seen.VisitId
            : Guid.NewGuid().ToString("N");
        _recent[h.VisitorId] = (id, h.OccurredUtc);
        return id;
    }
}
