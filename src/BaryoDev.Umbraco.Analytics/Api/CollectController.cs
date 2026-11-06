using System.Text.Json;
using BaryoDev.Umbraco.Analytics.Collect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.Analytics.Api;

/// <summary>Public beacon endpoint for script mode. Always answers 202 so the page never sees an error.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class CollectController(
    IOptions<AnalyticsOptions> options, Collector collector, EventQueue queue,
    ContentKeyResolver resolver, IRuntimeState runtime) : ControllerBase
{
    private const int MaxBody = 4096;

    [HttpPost("baryo-analytics/collect")]
    public async Task<IActionResult> Collect(CancellationToken ct)
    {
        if (options.Value.Mode != CollectionMode.Script || runtime.Level != RuntimeLevel.Run
            || Request.ContentLength is > MaxBody)
        {
            return Accepted();
        }

        var limit = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false })
        {
            limit.MaxRequestBodySize = MaxBody;
        }

        try
        {
            byte[] buffer = new byte[MaxBody + 1];
            int read = 0;
            int n;
            while (read < buffer.Length && (n = await Request.Body.ReadAsync(buffer.AsMemory(read), ct)) > 0)
            {
                read += n;
            }
            if (read > MaxBody)
            {
                return Accepted();
            }

            using JsonDocument doc = JsonDocument.Parse(buffer.AsMemory(0, read));
            JsonElement root = doc.RootElement;
            string? path = Text(root, "p");
            // The browser sends the encoded pathname. Middleware mode sees it decoded, so decode it here too.
            Hit? hit = collector.Build(HttpContext, Decode(path), Text(root, "r"), Text(root, "q"));
            if (hit is not null)
            {
                queue.TryEnqueue(hit with { ContentKey = resolver.Resolve(hit.Path) });
            }
        }
        catch (Exception)
        {
            // A malformed or oversized beacon, or a fault in recording it, never reaches the page. Collector logs real faults once.
        }
        return Accepted();
    }

    private static string Decode(string? path)
    {
        try
        {
            return Uri.UnescapeDataString(path ?? string.Empty);
        }
        catch (UriFormatException)
        {
            return string.Empty;
        }
    }

    private static string? Text(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
