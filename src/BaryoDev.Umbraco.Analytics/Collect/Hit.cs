namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>What the collector learned from one request, before it is queued.</summary>
public sealed record Hit(
    DateTime OccurredUtc,
    string VisitorId,
    string Path,
    Guid? ContentKey,
    string ReferrerHost,
    string UtmSource,
    string UtmMedium,
    string UtmCampaign,
    string Device,
    string Browser,
    string Country);
