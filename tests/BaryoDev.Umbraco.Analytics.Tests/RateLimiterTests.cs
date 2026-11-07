using BaryoDev.Umbraco.Analytics.Collect;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BaryoDev.Umbraco.Analytics.Tests;

public class RateLimiterTests
{
    private static (RateLimiter Limiter, FakeTimeProvider Time) Create(int perMinute = 120)
    {
        var options = Options.Create(new AnalyticsOptions { Secret = "test-secret", RateLimitPerMinute = perMinute });
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
        return (new RateLimiter(options, new SecretProvider(options, null!, null!), time), time);
    }

    [Fact]
    public void Rate_limit_allows_120_then_drops()
    {
        var (limiter, time) = Create();

        for (int i = 0; i < 120; i++)
        {
            Assert.True(limiter.Allow("203.0.113.7"), "hit " + (i + 1));
        }
        Assert.False(limiter.Allow("203.0.113.7"));
        Assert.True(limiter.Allow("203.0.113.8"));

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(limiter.Allow("203.0.113.7"));
    }

    [Fact]
    public void Zero_turns_the_limit_off()
    {
        var (limiter, _) = Create(perMinute: 0);

        for (int i = 0; i < 500; i++)
        {
            Assert.True(limiter.Allow("203.0.113.7"));
        }
    }

    [Fact]
    public void Bucket_table_stops_at_50000()
    {
        var (limiter, time) = Create();
        static string Ip(int n) => $"10.{(n >> 16) & 255}.{(n >> 8) & 255}.{n & 255}";

        for (int i = 0; i < 50_000; i++)
        {
            Assert.True(limiter.Allow(Ip(i)), "client " + i);
        }

        // A new client is dropped, a known one still counts.
        Assert.False(limiter.Allow(Ip(50_000)));
        Assert.True(limiter.Allow(Ip(0)));

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(limiter.Allow(Ip(50_000)));
    }
}
