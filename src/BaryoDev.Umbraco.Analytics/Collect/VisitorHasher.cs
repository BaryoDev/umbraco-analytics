using System.Security.Cryptography;
using System.Text;

namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>Builds the visitor id. The IP and User-Agent are only inputs to the hash and are never kept.</summary>
public static class VisitorHasher
{
    public static string Salt(SaltRotation rotation, DateTime utcNow, string secret)
    {
        string period = rotation == SaltRotation.Daily ? utcNow.ToString("yyyy-MM-dd") : utcNow.ToString("yyyy-MM");
        return Hex("salt|" + period + "|" + secret);
    }

    public static string FromRequest(string siteKey, string ip, string userAgent, string salt, string secret)
        => Hex(string.Join('|', siteKey, ip, userAgent, salt, secret));

    public static string FromCookie(string siteKey, string cookie, string secret)
        => Hex(string.Join('|', siteKey, cookie, secret));

    private static string Hex(string input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)), 0, 16).ToLowerInvariant();
}
