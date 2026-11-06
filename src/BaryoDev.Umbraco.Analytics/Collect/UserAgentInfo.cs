namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>Small built-in User-Agent matcher. It only sorts, it does not try to be exact.</summary>
public static class UserAgentInfo
{
    private static readonly string[] Bots =
    [
        "bot", "spider", "crawl", "slurp", "headless", "lighthouse", "pingdom", "uptime", "monitor",
        "curl/", "wget/", "python-requests", "go-http-client", "okhttp", "java/", "libwww", "facebookexternalhit",
        "preview", "scrapy", "httpclient", "axios/", "node-fetch",
    ];

    public static bool IsBot(string? ua, IEnumerable<string> extra)
    {
        if (string.IsNullOrWhiteSpace(ua))
        {
            return true;
        }
        foreach (string b in Bots)
        {
            if (ua.Contains(b, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        foreach (string b in extra)
        {
            if (!string.IsNullOrWhiteSpace(b) && ua.Contains(b, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static string Device(string ua)
    {
        if (Has(ua, "iPad") || Has(ua, "Tablet") || (Has(ua, "Android") && !Has(ua, "Mobile")))
        {
            return "Tablet";
        }
        if (Has(ua, "Mobi") || Has(ua, "iPhone") || Has(ua, "Android"))
        {
            return "Mobile";
        }
        return "Desktop";
    }

    public static string Browser(string ua)
    {
        if (Has(ua, "Edg/") || Has(ua, "EdgA/") || Has(ua, "EdgiOS/")) return "Edge";
        if (Has(ua, "OPR/") || Has(ua, "Opera")) return "Opera";
        if (Has(ua, "SamsungBrowser")) return "Samsung Internet";
        if (Has(ua, "Firefox/") || Has(ua, "FxiOS/")) return "Firefox";
        if (Has(ua, "Chrome/") || Has(ua, "CriOS/")) return "Chrome";
        if (Has(ua, "Safari/")) return "Safari";
        return "Other";
    }

    private static bool Has(string ua, string part) => ua.Contains(part, StringComparison.Ordinal);
}
