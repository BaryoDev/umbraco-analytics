using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>Turns a request into a Hit, or null when the request must not be counted.</summary>
public sealed partial class Collector(
    IOptions<AnalyticsOptions> options, SecretProvider secrets, RateLimiter limiter, TimeProvider clock, ILogger<Collector> logger)
{
    private const string CookieItem = "BaryoDev.Analytics.Cookie";
    private int _failureLogged;
    private int _proxyWarned;

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex CookieShape();

    /// <summary>Never throws. A failure is logged once and the hit is dropped.</summary>
    public Hit? Build(HttpContext http, string rawPath, string? referrer, string? query)
    {
        try
        {
            return BuildCore(http, rawPath, referrer, query);
        }
        catch (Exception e)
        {
            LogFailure(e);
            return null;
        }
    }

    /// <summary>Middleware mode, before the response starts. Sets the id cookie only for a request that will be counted.</summary>
    public void PrepareCookie(HttpContext http, string rawPath)
    {
        try
        {
            AnalyticsOptions o = options.Value;
            if (o.SaltRotation != SaltRotation.Cookie || !IsDocumentFetch(http) || !Passes(http, rawPath, out _, out _))
            {
                return;
            }

            string? existing = http.Request.Cookies[o.CookieName];
            if (existing is not null && CookieShape().IsMatch(existing))
            {
                http.Items[CookieItem] = existing;
                return;
            }

            string value = NewCookieValue();
            http.Items[CookieItem] = value;
            http.Response.OnStarting(() =>
            {
                // Only HTML pages get the cookie. Secure uses the scheme after forwarded headers have run.
                if (http.Response.StatusCode == StatusCodes.Status200OK
                    && http.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
                {
                    AppendCookie(http, value);
                }
                return Task.CompletedTask;
            });
        }
        catch (Exception e)
        {
            LogFailure(e);
        }
    }

    public static bool IsDocumentFetch(HttpContext http)
    {
        string dest = http.Request.Headers["Sec-Fetch-Dest"].ToString();
        return dest.Length == 0 || dest.Equals("document", StringComparison.OrdinalIgnoreCase);
    }

    private void LogFailure(Exception e)
    {
        if (Interlocked.Exchange(ref _failureLogged, 1) == 0)
        {
            logger.LogWarning(e, "Analytics could not record a page view. This is logged once. Page views are skipped while it keeps failing.");
        }
    }

    // The cheap skip checks. No state is changed here.
    private bool Passes(HttpContext http, string rawPath, out string path, out string ua)
    {
        path = string.Empty;
        AnalyticsOptions o = options.Value;
        IHeaderDictionary h = http.Request.Headers;
        ua = h.UserAgent.ToString();
        if (!o.Enabled
            || (o.RespectDoNotTrack && h["DNT"] == "1")
            || (o.RespectGlobalPrivacyControl && h["Sec-GPC"] == "1")
            || UserAgentInfo.IsBot(ua, o.ExtraBotUserAgents))
        {
            return false;
        }

        string? clean = NormalisePath(rawPath);
        if (clean is null || IsExcluded(clean, o))
        {
            return false;
        }
        path = clean;
        return true;
    }

    private Hit? BuildCore(HttpContext http, string rawPath, string? referrer, string? query)
    {
        AnalyticsOptions o = options.Value;
        if (!Passes(http, rawPath, out string path, out string ua))
        {
            return null;
        }

        string ip = ClientIp(http, o);
        // With no usable address there is nothing fair to key the limit on, so it is skipped.
        if (ip.Length > 0 && !limiter.Allow(ip))
        {
            return null;
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        string secret = secrets.Get();
        string visitor;
        if (o.SaltRotation == SaltRotation.Cookie)
        {
            visitor = VisitorHasher.FromCookie(o.SiteKey, CookieId(http), secret);
        }
        else
        {
            string salt = VisitorHasher.Salt(o.SaltRotation, now, secret);
            visitor = VisitorHasher.FromRequest(o.SiteKey, RateLimiter.Partition(ip), ua, salt, secret);
        }

        var utm = ParseUtm(query);
        return new Hit(
            now, visitor, Cut(path, 500), null,
            ReferrerHost(referrer, http.Request.Host.Host),
            utm.Source, utm.Medium, utm.Campaign,
            UserAgentInfo.Device(ua), UserAgentInfo.Browser(ua),
            Country(http.Request.Headers, o));
    }

    // Middleware mode already chose the cookie. Script mode answers the beacon, so it sets the cookie here.
    private string CookieId(HttpContext http)
    {
        if (http.Items[CookieItem] is string cached)
        {
            return cached;
        }

        AnalyticsOptions o = options.Value;
        string? value = http.Request.Cookies[o.CookieName];
        if (value is null || !CookieShape().IsMatch(value))
        {
            value = NewCookieValue();
            if (o.Mode == CollectionMode.Script && !http.Response.HasStarted)
            {
                AppendCookie(http, value);
            }
        }
        http.Items[CookieItem] = value;
        return value;
    }

    private static string NewCookieValue() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private void AppendCookie(HttpContext http, string value)
        => http.Response.Cookies.Append(options.Value.CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = http.Request.IsHttps,
            MaxAge = TimeSpan.FromDays(365),
            IsEssential = true,
        });

    internal static string? NormalisePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        int cut = raw.IndexOfAny(['?', '#']);
        string p = cut >= 0 ? raw[..cut] : raw;
        // Same-site page paths only: no "//host", no backslashes or control characters, no asset files.
        if (!p.StartsWith('/') || p.StartsWith("//") || p.Contains('\\') || p.Any(char.IsControl) || IsAsset(p))
        {
            return null;
        }
        if (p.Length > 1)
        {
            p = p.TrimEnd('/');
            if (p.Length == 0)
            {
                p = "/";
            }
        }
        return p.Length > 500 ? p[..500] : p;
    }

    private static readonly string[] AssetExtensions =
        [".js", ".css", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico", ".woff", ".woff2", ".ttf", ".json", ".xml", ".txt"];

    private static bool IsAsset(string path)
        => AssetExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static bool IsExcluded(string path, AnalyticsOptions o)
    {
        if (Under(path, "/umbraco") || Under(path, "/baryo-analytics") || Under(path, "/App_Plugins"))
        {
            return true;
        }
        foreach (string x in o.ExcludedPaths)
        {
            string prefix = "/" + x.Trim().Trim('/');
            if (prefix.Length > 1 && Under(path, prefix))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Under(string path, string prefix)
        => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
           && (path.Length == prefix.Length || path[prefix.Length] == '/');

    private string[]? _trustedKey;
    private IPNetwork[] _trusted = [];

    /// <summary>
    /// The header is read only when the connection comes from a trusted proxy. Then the chain is walked
    /// from the right and the first hop that is not a trusted proxy wins, so a forged left entry is ignored.
    /// </summary>
    private string ClientIp(HttpContext http, AnalyticsOptions o)
    {
        IPAddress? remote = Unmap(http.Connection.RemoteIpAddress);
        if (remote is null || (string.IsNullOrWhiteSpace(o.ClientIpHeader) && http.Request.Headers.ContainsKey("X-Forwarded-For")))
        {
            if (Interlocked.Exchange(ref _proxyWarned, 1) == 0)
            {
                logger.LogWarning(
                    "Analytics sees no usable client address (no remote IP, or an X-Forwarded-For header that is not handled). " +
                    "Configure ASP.NET Core forwarded headers, or ClientIpHeader with TrustedProxies. The rate limit is skipped without an address.");
            }
        }
        if (string.IsNullOrWhiteSpace(o.ClientIpHeader) || remote is null)
        {
            return remote?.ToString() ?? string.Empty;
        }

        IPNetwork[] trusted = Trusted(o.TrustedProxies);
        if (!IsTrusted(remote, trusted))
        {
            return remote.ToString();
        }

        string[] hops = http.Request.Headers[o.ClientIpHeader].ToString().Split(',', StringSplitOptions.TrimEntries);
        for (int i = hops.Length - 1; i >= 0; i--)
        {
            string text = hops[i];
            // IPv4 hops may carry a port. IPv6 hops in brackets already parse.
            if (text.Count(c => c == ':') == 1 && text.Contains('.'))
            {
                text = text[..text.IndexOf(':')];
            }
            if (!IPAddress.TryParse(text, out IPAddress? hop))
            {
                break;
            }
            hop = Unmap(hop)!;
            if (!IsTrusted(hop, trusted))
            {
                return hop.ToString();
            }
        }
        return remote.ToString();
    }

    private IPNetwork[] Trusted(List<string> entries)
    {
        string[] snapshot = [.. entries];
        if (_trustedKey is null || !_trustedKey.SequenceEqual(snapshot))
        {
            var list = new List<IPNetwork>();
            foreach (string e in snapshot)
            {
                string text = e.Trim();
                if (IPNetwork.TryParse(text, out IPNetwork net))
                {
                    list.Add(net);
                }
                else if (IPAddress.TryParse(text, out IPAddress? single))
                {
                    single = Unmap(single)!;
                    list.Add(new IPNetwork(single, single.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
                }
            }
            _trusted = [.. list];
            _trustedKey = snapshot;
        }
        return _trusted;
    }

    private static bool IsTrusted(IPAddress ip, IPNetwork[] trusted) => trusted.Any(n => n.Contains(ip));

    private static IPAddress? Unmap(IPAddress? ip) => ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip;

    private static string Country(IHeaderDictionary h, AnalyticsOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.CountryHeader))
        {
            return string.Empty;
        }
        string v = h[o.CountryHeader].ToString().Trim().ToUpperInvariant();
        bool ok = v.Length == 2 && v.All(c => c is >= 'A' and <= 'Z') && v is not ("XX" or "T1");
        return ok ? v : string.Empty;
    }

    private static string ReferrerHost(string? referrer, string ownHost)
    {
        if (!Uri.TryCreate(referrer, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
        {
            return string.Empty;
        }
        string host = Strip(uri.Host);
        return host == Strip(ownHost) ? string.Empty : Cut(host, 255);

        static string Strip(string h) => h.ToLowerInvariant().StartsWith("www.") ? h[4..].ToLowerInvariant() : h.ToLowerInvariant();
    }

    private static (string Source, string Medium, string Campaign) ParseUtm(string? query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return ("", "", "");
        }
        var q = QueryHelpers.ParseQuery(query.StartsWith('?') ? query : "?" + query);
        string Get(string k) => q.TryGetValue(k, out var v) ? Cut(v.ToString().Trim(), 255) : string.Empty;
        return (Get("utm_source"), Get("utm_medium"), Get("utm_campaign"));
    }

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;
}
