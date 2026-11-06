using System.Text.Json;
using BaryoDev.Umbraco.Analytics.Collect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace BaryoDev.Umbraco.Analytics.Api;

/// <summary>Public beacon endpoint for script mode. Always answers 204 so the page never sees an error.</summary>
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
            return NoContent();
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
                return NoContent();
            }

            using JsonDocument doc = JsonDocument.Parse(buffer.AsMemory(0, read));
            JsonElement root = doc.RootElement;
            string? path = Text(root, "p");
            Hit? hit = collector.Build(HttpContext, path ?? string.Empty, Text(root, "r"), Text(root, "q"),
                Collector.NormalisePath(path) is { } clean ? resolver.Resolve(clean) : null);
            if (hit is not null)
            {
                queue.TryEnqueue(hit);
            }
        }
        catch (JsonException)
        {
            // A malformed beacon is ignored.
        }
        return NoContent();
    }

    private static string? Text(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
