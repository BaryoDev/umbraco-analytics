namespace BaryoDev.Umbraco.Analytics;

public enum SaltRotation
{
    Monthly,
    Daily,
    /// <summary>Sets a first-party random id cookie instead of hashing the IP and User-Agent.</summary>
    Cookie,
}

public enum CollectionMode
{
    /// <summary>The tracker script posts to the collect endpoint.</summary>
    Script,
    /// <summary>The server records HTML page requests itself.</summary>
    Middleware,
}

public sealed class AnalyticsOptions
{
    public const string Section = "BaryoDev:Analytics";

    public bool Enabled { get; set; } = true;
    public string SiteKey { get; set; } = "default";
    /// <summary>Server secret. Leave empty to generate one on first run, kept in the Umbraco key-value store.</summary>
    public string? Secret { get; set; }
    public SaltRotation SaltRotation { get; set; } = SaltRotation.Monthly;
    public CollectionMode Mode { get; set; } = CollectionMode.Script;
    public int VisitIdleMinutes { get; set; } = 30;
    public int RetentionDays { get; set; } = 395;
    public bool RespectDoNotTrack { get; set; } = true;
    public bool RespectGlobalPrivacyControl { get; set; } = true;
    public string CookieName { get; set; } = "ba_vid";
    /// <summary>Header holding the country code. Read only if present.</summary>
    public string CountryHeader { get; set; } = "CF-IPCountry";
    /// <summary>Header holding the client IP chain. Read only when the connection comes from a TrustedProxies entry.</summary>
    public string? ClientIpHeader { get; set; }
    /// <summary>IPs or CIDRs of proxies allowed to set ClientIpHeader. Empty means the header is never trusted.</summary>
    public List<string> TrustedProxies { get; set; } = [];
    public List<string> ExcludedPaths { get; set; } = [];
    public List<string> ExtraBotUserAgents { get; set; } = [];
    /// <summary>Hits per client per minute before extra hits are dropped. 0 turns the limit off.</summary>
    public int RateLimitPerMinute { get; set; } = 120;
    public int QueueCapacity { get; set; } = 10000;
}
