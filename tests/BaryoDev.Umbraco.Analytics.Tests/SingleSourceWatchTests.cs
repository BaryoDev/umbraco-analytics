using BaryoDev.Umbraco.Analytics.Collect;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace BaryoDev.Umbraco.Analytics.Tests;

public class SingleSourceWatchTests
{
    private sealed class ListLogger : ILogger
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }

    private static (SingleSourceWatch Watch, ListLogger Log, FakeTimeProvider Time) Create()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
        var log = new ListLogger();
        return (new SingleSourceWatch(time, log), log, time);
    }

    [Fact]
    public void One_address_with_no_forwarded_header_warns_once_for_the_minute()
    {
        var (watch, log, time) = Create();
        for (int i = 0; i < SingleSourceWatch.MinHits; i++)
        {
            watch.Record("10.0.0.1", forwardedHeaderPresent: false);
        }
        Assert.Empty(log.Levels);

        time.Advance(TimeSpan.FromMinutes(1));
        watch.Record("10.0.0.1", false);

        Assert.Equal([LogLevel.Warning], log.Levels);
    }

    [Fact]
    public void Several_addresses_a_forwarded_header_or_a_quiet_minute_do_not_warn()
    {
        var (watch, log, time) = Create();
        for (int i = 0; i < 30; i++)
        {
            watch.Record(i % 2 == 0 ? "10.0.0.1" : "10.0.0.2", false);
        }
        time.Advance(TimeSpan.FromMinutes(1));
        for (int i = 0; i < 30; i++)
        {
            watch.Record("10.0.0.1", forwardedHeaderPresent: true);
        }
        time.Advance(TimeSpan.FromMinutes(1));
        for (int i = 0; i < SingleSourceWatch.MinHits - 1; i++)
        {
            watch.Record("10.0.0.1", false);
        }
        time.Advance(TimeSpan.FromMinutes(1));
        watch.Record("10.0.0.1", false);

        Assert.Empty(log.Levels);
    }
}
