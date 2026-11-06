using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>Fixed one-minute window per client, at most 50,000 clients per window. The key is a salted hash, so no raw IP is held in memory.</summary>
public sealed class RateLimiter(IOptions<AnalyticsOptions> options, SecretProvider secrets, TimeProvider clock)
{
    private readonly object _gate = new();
    private Dictionary<string, int> _counts = [];
    private long _window;
    private const int MaxBuckets = 50_000;

    public bool Allow(string ip)
    {
        int limit = options.Value.RateLimitPerMinute;
        if (limit <= 0)
        {
            return true;
        }

        string key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes("rl|" + Partition(ip) + "|" + secrets.Get())), 0, 8);
        long window = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
        lock (_gate)
        {
            if (window != _window)
            {
                _window = window;
                _counts = [];
            }
            if (!_counts.TryGetValue(key, out int seen) && _counts.Count >= MaxBuckets)
            {
                return false;
            }
            int n = seen + 1;
            _counts[key] = n;
            return n <= limit;
        }
    }

    // One IPv6 /64 is one client, so a single host cannot fill the table with fresh addresses.
    internal static string Partition(string ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var addr)) return ip;
        if (addr.IsIPv4MappedToIPv6) return addr.MapToIPv4().ToString();
        if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return addr.ToString();
        byte[] b = addr.GetAddressBytes();
        Array.Clear(b, 8, 8);
        return new System.Net.IPAddress(b).ToString() + "/64";
    }
}
