using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>Middleware mode: counts GET requests that answer 200 with HTML.</summary>
public sealed class CollectMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext http, IOptions<AnalyticsOptions> options, Collector collector, EventQueue queue, IRuntimeState runtime)
    {
        AnalyticsOptions o = options.Value;
        bool active = o.Enabled && o.Mode == CollectionMode.Middleware
                      && runtime.Level == RuntimeLevel.Run && HttpMethods.IsGet(http.Request.Method);
        if (!active)
        {
            await next(http);
            return;
        }

        string path = http.Request.Path.Value ?? "/";
        collector.PrepareCookie(http, path);

        await next(http);

        if (http.Response.StatusCode != StatusCodes.Status200OK
            || http.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) != true
            || !Collector.IsDocumentFetch(http))
        {
            return;
        }

        Hit? hit = collector.Build(http, path, http.Request.Headers.Referer.ToString(), http.Request.QueryString.Value);
        if (hit is not null)
        {
            Guid? key = http.Features.Get<UmbracoRouteValues>()?.PublishedRequest.PublishedContent?.Key;
            queue.TryEnqueue(hit with { ContentKey = key });
        }
    }
}

/// <summary>Puts the middleware at the front of the pipeline so it sees the finished response.</summary>
public sealed class CollectStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<CollectMiddleware>();
        next(app);
    };
}
