using System.Net;
using System.Reflection;
using System.Text;
using BaryoDev.Umbraco.Analytics.Api;
using BaryoDev.Umbraco.Analytics.Collect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Authorization;

namespace BaryoDev.Umbraco.Analytics.Tests;

public class CollectorTests
{
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

    private sealed class Rig
    {
        public AnalyticsOptions Options { get; } = new() { Secret = "test-secret", CountryHeader = "" };
        public FakeTimeProvider Time { get; }
        public Collector Collector { get; }
        public RateLimiter Limiter { get; }

        public Rig(Action<AnalyticsOptions>? configure = null, DateTimeOffset? start = null)
        {
            Time = new FakeTimeProvider(start ?? new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
            configure?.Invoke(Options);
            var opts = Microsoft.Extensions.Options.Options.Create(Options);
            // A configured secret means the key-value store and scopes are never touched.
            var secrets = new SecretProvider(opts, null!, null!);
            Limiter = new RateLimiter(opts, secrets, Time);
            Collector = new Collector(opts, secrets, Limiter, Time, NullLogger<Collector>.Instance);
        }

        public Hit? Hit(string remote, Action<HttpContext>? tweak = null, string path = "/page")
        {
            DefaultHttpContext http = Request(remote, tweak);
            return Collector.Build(http, path, null, null);
        }

        public string Visitor(string remote, Action<HttpContext>? tweak = null)
            => Hit(remote, tweak)?.VisitorId ?? throw new InvalidOperationException("request was skipped");
    }

    private static DefaultHttpContext Request(string remote, Action<HttpContext>? tweak = null)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        http.Request.Headers.UserAgent = Chrome;
        tweak?.Invoke(http);
        return http;
    }

    [Fact]
    public void Same_visitor_same_month_hashes_equal_and_next_month_differs()
    {
        var rig = new Rig();
        string first = rig.Visitor("203.0.113.7");

        rig.Time.SetUtcNow(new DateTimeOffset(2026, 11, 28, 23, 0, 0, TimeSpan.Zero));
        Assert.Equal(first, rig.Visitor("203.0.113.7"));

        rig.Time.SetUtcNow(new DateTimeOffset(2026, 11, 30, 23, 59, 59, TimeSpan.Zero));
        string lastSecond = rig.Visitor("203.0.113.7");
        Assert.Equal(first, lastSecond);

        rig.Time.SetUtcNow(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        string nextMonth = rig.Visitor("203.0.113.7");
        Assert.NotEqual(first, nextMonth);
        Assert.NotEqual(lastSecond, nextMonth);
    }

    [Fact]
    public void Daily_rotation_changes_the_hash_at_utc_midnight()
    {
        var rig = new Rig(o => o.SaltRotation = SaltRotation.Daily, new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
        string morning = rig.Visitor("203.0.113.7");

        rig.Time.SetUtcNow(new DateTimeOffset(2026, 11, 3, 23, 59, 59, TimeSpan.Zero));
        string night = rig.Visitor("203.0.113.7");

        rig.Time.SetUtcNow(new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero));
        string nextDay = rig.Visitor("203.0.113.7");

        Assert.Equal(morning, night);
        Assert.NotEqual(night, nextDay);
    }

    [Fact]
    public void Ipv6_visitors_hash_per_64()
    {
        var rig = new Rig();

        string a = rig.Visitor("2001:db8:1:2::1");
        string sameNetwork = rig.Visitor("2001:db8:1:2:aaaa:bbbb:cccc:dddd");
        string otherNetwork = rig.Visitor("2001:db8:1:3::1");

        Assert.Equal(a, sameNetwork);
        Assert.NotEqual(a, otherNetwork);
        Assert.Equal("2001:db8:1:2::/64", RateLimiter.Partition("2001:db8:1:2:aaaa:bbbb:cccc:dddd"));
    }

    [Theory]
    [InlineData("DNT")]
    [InlineData("Sec-GPC")]
    public void Dnt_and_gpc_requests_are_skipped(string header)
    {
        var rig = new Rig();

        Assert.Null(rig.Hit("203.0.113.7", h => h.Request.Headers[header] = "1"));
        Assert.NotNull(rig.Hit("203.0.113.7"));
    }

    [Fact]
    public void Dnt_is_counted_when_respecting_it_is_turned_off()
    {
        var rig = new Rig(o => o.RespectDoNotTrack = false);

        Assert.NotNull(rig.Hit("203.0.113.7", h => h.Request.Headers["DNT"] = "1"));
    }

    [Theory]
    [InlineData("Googlebot/2.1 (+http://www.google.com/bot.html)")]
    [InlineData("curl/8.4.0")]
    [InlineData("")]
    public void Bot_user_agent_is_skipped(string ua)
    {
        var rig = new Rig();

        Assert.Null(rig.Hit("203.0.113.7", h => h.Request.Headers.UserAgent = ua));
    }

    private sealed class RecordingResponse : HttpResponseFeature
    {
        public List<(Func<object, Task> Callback, object State)> Started { get; } = [];

        public override void OnStarting(Func<object, Task> callback, object state) => Started.Add((callback, state));
    }

    private static (DefaultHttpContext Http, RecordingResponse Response) CookieRequest(Action<HttpContext>? tweak = null)
    {
        DefaultHttpContext http = Request("203.0.113.7", tweak);
        var response = new RecordingResponse();
        http.Features.Set<IHttpResponseFeature>(response);
        return (http, response);
    }

    [Fact]
    public async Task Cookie_is_not_set_for_a_dnt_request_in_cookie_mode()
    {
        var rig = new Rig(o => o.SaltRotation = SaltRotation.Cookie);

        var (skipped, skippedResponse) = CookieRequest(h => h.Request.Headers["DNT"] = "1");
        rig.Collector.PrepareCookie(skipped, "/page");
        Assert.Empty(skippedResponse.Started);
        Assert.Empty(skipped.Items);

        // Control: a normal HTML page does get the cookie.
        var (normal, normalResponse) = CookieRequest();
        rig.Collector.PrepareCookie(normal, "/page");
        Assert.Single(normalResponse.Started);
        normal.Response.ContentType = "text/html; charset=utf-8";
        var (callback, state) = normalResponse.Started[0];
        await callback(state);
        Assert.Contains("ba_vid=", normal.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public void Forwarded_header_is_ignored_without_trusted_proxies()
    {
        var rig = new Rig(o => o.ClientIpHeader = "X-Forwarded-For");

        string forged = rig.Visitor("9.9.9.9", h => h.Request.Headers["X-Forwarded-For"] = "1.1.1.1");
        string direct = rig.Visitor("9.9.9.9");
        string asClaimed = rig.Visitor("1.1.1.1");

        Assert.Equal(direct, forged);
        Assert.NotEqual(asClaimed, forged);
    }

    [Fact]
    public void Forwarded_header_is_ignored_by_default()
    {
        var rig = new Rig();

        string forged = rig.Visitor("9.9.9.9", h => h.Request.Headers["X-Forwarded-For"] = "1.1.1.1");

        Assert.Equal(rig.Visitor("9.9.9.9"), forged);
        Assert.NotEqual(rig.Visitor("1.1.1.1"), forged);
    }

    [Theory]
    [InlineData("1.1.1.1, 10.0.0.5")]
    [InlineData("6.6.6.6, 1.1.1.1, 10.0.0.5")]
    public void Rightmost_untrusted_hop_is_used_with_trusted_proxies(string chain)
    {
        var rig = new Rig(o =>
        {
            o.ClientIpHeader = "X-Forwarded-For";
            o.TrustedProxies = ["10.0.0.0/8"];
        });

        string viaProxy = rig.Visitor("10.0.0.9", h => h.Request.Headers["X-Forwarded-For"] = chain);

        Assert.Equal(rig.Visitor("1.1.1.1"), viaProxy);
    }

    [Fact]
    public void Header_from_an_untrusted_connection_is_ignored_even_with_trusted_proxies()
    {
        var rig = new Rig(o =>
        {
            o.ClientIpHeader = "X-Forwarded-For";
            o.TrustedProxies = ["10.0.0.0/8"];
        });

        string forged = rig.Visitor("9.9.9.9", h => h.Request.Headers["X-Forwarded-For"] = "1.1.1.1");

        Assert.Equal(rig.Visitor("9.9.9.9"), forged);
    }

    [Fact]
    public void Ipv4_hop_with_port_is_parsed()
    {
        var rig = new Rig(o =>
        {
            o.ClientIpHeader = "X-Forwarded-For";
            o.TrustedProxies = ["10.0.0.5"];
        });

        string viaProxy = rig.Visitor("10.0.0.5", h => h.Request.Headers["X-Forwarded-For"] = "1.1.1.1:54321");

        Assert.Equal(rig.Visitor("1.1.1.1"), viaProxy);
    }

    public class RuntimeProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_Level")
            {
                return RuntimeLevel.Run;
            }
            Type type = targetMethod!.ReturnType;
            return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }

    [Fact]
    public async Task Script_path_is_percent_decoded()
    {
        var rig = new Rig(o => o.Mode = CollectionMode.Script);
        var queue = new EventQueue(Microsoft.Extensions.Options.Options.Create(rig.Options));
        var controller = new CollectController(
            Microsoft.Extensions.Options.Options.Create(rig.Options), rig.Collector, queue,
            new ContentKeyResolver(null!), DispatchProxy.Create<IRuntimeState, RuntimeProxy>());

        byte[] body = Encoding.UTF8.GetBytes("""{"p":"/caf%C3%A9/a%20b"}""");
        DefaultHttpContext http = Request("203.0.113.7");
        http.Request.Body = new MemoryStream(body);
        http.Request.ContentLength = body.Length;
        controller.ControllerContext = new ControllerContext(new ActionContext(
            http, new RouteData(), new ControllerActionDescriptor { ControllerName = "Collect", ActionName = "Collect" }));

        IActionResult result = await controller.Collect(CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.True(queue.Reader.TryRead(out Hit? hit));
        Assert.Equal("/café/a b", hit!.Path);
    }

    private static (CollectController Controller, EventQueue Queue) ScriptController(DefaultHttpContext http)
    {
        var rig = new Rig(o => o.Mode = CollectionMode.Script);
        var queue = new EventQueue(Microsoft.Extensions.Options.Options.Create(rig.Options));
        var controller = new CollectController(
            Microsoft.Extensions.Options.Options.Create(rig.Options), rig.Collector, queue,
            new ContentKeyResolver(null!), DispatchProxy.Create<IRuntimeState, RuntimeProxy>());
        controller.ControllerContext = new ControllerContext(new ActionContext(
            http, new RouteData(), new ControllerActionDescriptor { ControllerName = "Collect", ActionName = "Collect" }));
        return (controller, queue);
    }

    private static async Task AssertBeaconDropped(Stream body, long? contentLength)
    {
        DefaultHttpContext http = Request("203.0.113.7");
        http.Request.Body = body;
        http.Request.ContentLength = contentLength;
        (CollectController controller, EventQueue queue) = ScriptController(http);

        IActionResult result = await controller.Collect(CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Beacon_over_4096_bytes_is_dropped()
    {
        byte[] body = Encoding.UTF8.GetBytes("""{"p":"/""" + new string('a', 4100) + "\"}");
        await AssertBeaconDropped(new MemoryStream(body), body.Length);
    }

    [Fact]
    public async Task Beacon_with_a_large_content_length_is_dropped_before_reading()
    {
        byte[] body = Encoding.UTF8.GetBytes("""{"p":"/ok"}""");
        await AssertBeaconDropped(new MemoryStream(body), 5000);
    }

    [Fact]
    public async Task Chunked_beacon_over_the_cap_is_dropped()
    {
        await AssertBeaconDropped(new MemoryStream(new byte[100_000]), null);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"p":123}""")]
    [InlineData("""{"p":"\ud800"}""")]
    [InlineData("not json")]
    public async Task Malformed_beacon_is_accepted_and_records_nothing(string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        await AssertBeaconDropped(new MemoryStream(body), body.Length);
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("/a\\b")]
    [InlineData("/a\nb")]
    [InlineData("/img.png")]
    [InlineData("/app.JS")]
    [InlineData("page")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NormalisePath_rejects(string? raw) => Assert.Null(Collector.NormalisePath(raw));

    [Theory]
    [InlineData("/x/", "/x")]
    [InlineData("/", "/")]
    [InlineData("///", null)]
    [InlineData("/x?q=1", "/x")]
    [InlineData("/x#f", "/x")]
    [InlineData("/a/b/c", "/a/b/c")]
    public void NormalisePath_cleans(string raw, string? expected) => Assert.Equal(expected, Collector.NormalisePath(raw));

    [Fact]
    public void NormalisePath_cuts_at_500_characters()
        => Assert.Equal(500, Collector.NormalisePath("/" + new string('a', 600))!.Length);

    [Theory]
    [InlineData("US", "US")]
    [InlineData("us", "US")]
    [InlineData(" ph ", "PH")]
    [InlineData("XX", "")]
    [InlineData("T1", "")]
    [InlineData("USA", "")]
    [InlineData("", "")]
    [InlineData("1A", "")]
    public void Country_header_is_validated(string header, string expected)
    {
        var rig = new Rig(o => o.CountryHeader = "CF-IPCountry");
        Hit? hit = rig.Hit("203.0.113.7", http => http.Request.Headers["CF-IPCountry"] = header);
        Assert.Equal(expected, hit!.Country);
    }

    private static async Task<(bool Called, EventQueue Queue)> RunMiddleware(
        Action<HttpContext> request, int status = 200, string contentType = "text/html; charset=utf-8")
    {
        var rig = new Rig(o => o.Mode = CollectionMode.Middleware);
        var queue = new EventQueue(Microsoft.Extensions.Options.Options.Create(rig.Options));
        bool called = false;
        var middleware = new CollectMiddleware(ctx =>
        {
            called = true;
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = contentType;
            return Task.CompletedTask;
        });
        DefaultHttpContext http = Request("203.0.113.7");
        http.Request.Method = "GET";
        http.Request.Path = "/page";
        request(http);
        await middleware.InvokeAsync(
            http, Microsoft.Extensions.Options.Options.Create(rig.Options), rig.Collector, queue,
            DispatchProxy.Create<IRuntimeState, RuntimeProxy>());
        return (called, queue);
    }

    [Fact]
    public async Task Middleware_records_a_200_html_get()
    {
        (bool called, EventQueue queue) = await RunMiddleware(_ => { });
        Assert.True(called);
        Assert.True(queue.Reader.TryRead(out Hit? hit));
        Assert.Equal("/page", hit!.Path);
    }

    [Fact]
    public async Task Middleware_skips_a_404()
    {
        (bool called, EventQueue queue) = await RunMiddleware(_ => { }, status: 404);
        Assert.True(called);
        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Middleware_skips_json()
    {
        (_, EventQueue queue) = await RunMiddleware(_ => { }, contentType: "application/json");
        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Middleware_skips_a_post()
    {
        (bool called, EventQueue queue) = await RunMiddleware(h => h.Request.Method = "POST");
        Assert.True(called);
        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Middleware_skips_an_image_fetch()
    {
        (_, EventQueue queue) = await RunMiddleware(h => h.Request.Headers["Sec-Fetch-Dest"] = "image");
        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public void Analytics_controller_requires_content_section_access()
    {
        var attributes = typeof(AnalyticsController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false);
        var own = Assert.IsType<AuthorizeAttribute>(Assert.Single(attributes));
        Assert.Equal(AuthorizationPolicies.SectionAccessContent, own.Policy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(365)]
    [InlineData(-1)]
    public void Analytics_controller_rejects_other_ranges(int days)
    {
        // The range check runs before the service is used.
        var controller = new AnalyticsController(new BaryoDev.Umbraco.Analytics.Stats.StatsService(null!, TimeProvider.System));
        Assert.IsType<BadRequestObjectResult>(controller.GetSummary(days));
    }
}
