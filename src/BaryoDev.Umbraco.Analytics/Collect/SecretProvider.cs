using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Scoping;

namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>Config secret first. Otherwise one is generated once and kept in the Umbraco key-value store.</summary>
public sealed class SecretProvider(IOptions<AnalyticsOptions> options, IKeyValueService keyValues, IScopeProvider scopes)
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
                stored = CreateOnce();
            }
            return _secret = stored;
        }
    }

    // Insert only if absent. The key is the primary key, so when two servers race one insert fails
    // and both read the winner's value back.
    private string CreateOnce()
    {
        string candidate = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        try
        {
            using var scope = scopes.CreateScope();
            scope.Database.Insert("umbracoKeyValue", "key", false, new { key = Key, value = candidate, updated = DateTime.UtcNow });
            scope.Complete();
        }
        catch (DbException)
        {
            // Another server created it first.
        }
        return keyValues.GetValue(Key) ?? throw new InvalidOperationException("The analytics secret could not be stored.");
    }
}
