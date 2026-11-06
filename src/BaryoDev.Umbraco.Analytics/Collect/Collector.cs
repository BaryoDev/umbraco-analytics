using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>Turns a request into a Hit, or null when the request must not be counted.</summary>
public sealed partial class Collector(IOptions<AnalyticsOptions> options, SecretProvider secrets, TimeProvider clock)
{
    private const string CookieItem = "BaryoDev.Analytics.Cookie";

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex CookieShape();

    public Hit? Build(HttpContext http, string rawPath, string? referrer, string? query, Guid? contentKey)
    {
        AnalyticsOptions o = options.Value;
        if (!o.Enabled)
        {
            return null;
        }

        IHeaderDictionary h = http.Request.Headers;
        if (o.RespectDoNotTrack && h["DNT"] == "1")
        {
            return null;
        }
        if (o.RespectGlobalPrivacyControl && h["Sec-GPC"] == "1")
        {
            return null;
        }

        string ua = h.UserAgent.ToString();
        if (UserAgentInfo.IsBot(ua, o.ExtraBotUserAgents))
        {
            return null;
        }

        string? path = NormalisePath(rawPath);
        if (path is null || IsExcluded(path, o))
        {
            return null;
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        string secret = secrets.Get();
        string visitor;
        if (o.SaltRotation == SaltRotation.Cookie)
        {
            visitor = VisitorHasher.FromCookie(o.SiteKey, EnsureCookie(http), secret);
        }
        else
        {
            string salt = VisitorHasher.Salt(o.SaltRotation, now, secret);
            visitor = VisitorHasher.FromRequest(o.SiteKey, ClientIp(http, o), ua, salt, secret);
        }

        var utm = ParseUtm(query);
        return new Hit(
            now, visitor, path, contentKey,
            ReferrerHost(referrer, http.Request.Host.Host),
            utm.Source, utm.Medium, utm.Campaign,
            UserAgentInfo.Device(ua), UserAgentInfo.Browser(ua),
            Country(h, o));
    }

    /// <summary>Reads the id cookie or makes one. The cookie is only ever set in Cookie mode.</summary>
    public string EnsureCookie(HttpContext http)
    {
        if (http.Items[CookieItem] is string cached)
        {
            return cached;
        }

        AnalyticsOptions o = options.Value;
        string? value = http.Request.Cookies[o.CookieName];
        if (value is null || !CookieShape().IsMatch(value))
        {
            value = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            if (!http.Response.HasStarted)
            {
                http.Response.Cookies.Append(o.CookieName, value, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = http.Request.IsHttps,
                    MaxAge = TimeSpan.FromDays(365),
                    IsEssential = true,
                });
            }
        }
        http.Items[CookieItem] = value;
        return value;
    }

    internal static string? NormalisePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        int cut = raw.IndexOfAny(['?', '#']);
        string p = cut >= 0 ? raw[..cut] : raw;
        if (!p.StartsWith('/'))
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

    private static bool IsExcluded(string path, AnalyticsOptions o)
    {
        if (Under(path, "/umbraco") || Under(path, "/baryo-analytics") || Under(path, "/App_Plugins"))
        {
            return true;
        }
        foreach (string x in o.ExcludedPaths)
        {
            if (!string.IsNullOrWhiteSpace(x) && Under(path, x.TrimEnd('/')))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Under(string path, string prefix)
        => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
           && (path.Length == prefix.Length || path[prefix.Length] == '/');

    private static string ClientIp(HttpContext http, AnalyticsOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.ClientIpHeader))
        {
            string first = http.Request.Headers[o.ClientIpHeader].ToString().Split(',')[0].Trim();
            if (IPAddress.TryParse(first, out IPAddress? parsed))
            {
                return parsed.ToString();
            }
        }
        return http.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }

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
