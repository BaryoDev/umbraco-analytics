using Microsoft.Extensions.Logging;

namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>
/// Warns when a whole minute of hits came from one address and none carried a forwarded header. That is what a
/// reverse proxy or CDN with no forwarded headers configured looks like. The minute is judged as a window, but a warning is logged at most once an hour. No address is logged.
/// </summary>
internal sealed class SingleSourceWatch(TimeProvider clock, ILogger logger)
{
    internal const int MinHits = 20;

    private readonly object _gate = new();
    private long _window = -1;
    private int _hits;
    private string? _first;
    private bool _many;
    private bool _forwarded;
    private long _lastWarned = long.MinValue;

    public void Record(string ip, bool forwardedHeaderPresent)
    {
        long window = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
        lock (_gate)
        {
            if (window != _window)
            {
                if (_hits >= MinHits && !_many && !_forwarded && window - _lastWarned >= 60)
                {
                    _lastWarned = window;
                    logger.LogWarning(
                        "All {Count} analytics hits in the last minute came from one address and none had a forwarded header. " +
                        "If the site is behind a proxy or CDN, configure ASP.NET Core forwarded headers, or ClientIpHeader with TrustedProxies. " +
                        "Until then all visitors share one rate-limit bucket and, in hash modes, one visitor per User-Agent.", _hits);
                }
                _window = window;
                _hits = 0;
                _first = null;
                _many = false;
                _forwarded = false;
            }
            _hits++;
            if (_first is null)
            {
                _first = ip;
            }
            else if (_first != ip)
            {
                _many = true;
            }
            _forwarded |= forwardedHeaderPresent;
        }
    }
}
