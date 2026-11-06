using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>Config secret first. Otherwise one is generated once and kept in the Umbraco key-value store.</summary>
public sealed class SecretProvider(IOptions<AnalyticsOptions> options, IKeyValueService keyValues)
{
    public const string Key = "BaryoDev.Umbraco.Analytics.Secret";

    private readonly object _gate = new();
    private string? _secret;

    public string Get()
    {
        if (_secret is not null)
        {
            return _secret;
        }
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(options.Value.Secret))
            {
                return _secret = options.Value.Secret!;
            }
            string? stored = keyValues.GetValue(Key);
            if (string.IsNullOrWhiteSpace(stored))
            {
                stored = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                keyValues.SetValue(Key, stored);
            }
            return _secret = stored;
        }
    }
}
