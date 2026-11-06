# BaryoDev.Umbraco.Analytics

Page view analytics for Umbraco 17 that stays in your own site. Events go into your Umbraco database (SQLite or SQL Server) through tables the package creates. No second database, no external service, no cookie by default.

The visitor counting idea comes from Umami (MIT). The code here is written from scratch. See [docs/design.md](docs/design.md).

## Install

```
dotnet add package BaryoDev.Umbraco.Analytics
```

Start the site once. The migration creates `baryoAnalyticsEvent` and `baryoAnalyticsDaily`. An Analytics dashboard appears in the Content section.

## Collect page views

Script mode is the default. Add the tag to your layout:

```html
<script defer src="/baryo-analytics/tracker.js"></script>
```

The script posts to `/baryo-analytics/collect` next to itself. Set `data-endpoint="..."` on the tag to use another URL.

Middleware mode records HTML page requests on the server, no script needed. Set `"Mode": "Middleware"`.

Only one mode runs at a time, so a page is not counted twice.

## Config

All keys live under `BaryoDev:Analytics` in appsettings.

| Key | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Turn collection off |
| `Mode` | `Script` | `Script` or `Middleware` |
| `SiteKey` | `default` | Mixed into the visitor hash |
| `Secret` | generated | Server secret. If empty, one is generated on first run and kept in the Umbraco key-value store. Set it from an environment variable or a secret store, never commit it |
| `SaltRotation` | `Monthly` | `Monthly`, `Daily` or `Cookie` |
| `VisitIdleMinutes` | `30` | Idle time that ends a visit |
| `RetentionDays` | `395` | Raw events older than this are rolled into daily totals and deleted |
| `RespectDoNotTrack` | `true` | Skip requests with `DNT: 1` |
| `RespectGlobalPrivacyControl` | `true` | Skip requests with `Sec-GPC: 1` |
| `CountryHeader` | `CF-IPCountry` | Country is stored only if this header is present |
| `ClientIpHeader` | empty | Header with the client IP chain. Read only when the connection comes from a `TrustedProxies` entry |
| `TrustedProxies` | empty | IPs or CIDRs of your proxies. The header chain is walked from the right and the first hop that is not a trusted proxy is the client |
| `ExcludedPaths` | empty | Extra path prefixes to skip |
| `ExtraBotUserAgents` | empty | Extra User-Agent fragments to skip. The built-in list matches fragments such as `bot`, so a rare real browser can be skipped |
| `CookieName` | `ba_vid` | Cookie name in `Cookie` mode |
| `RateLimitPerMinute` | `120` | Hits per client per minute. Extra hits are dropped. `0` turns it off. Tracks at most 50,000 clients per minute, then drops hits from new ones |
| `QueueCapacity` | `10000` | Events held in memory before new ones are dropped |

`/umbraco`, `/App_Plugins` and `/baryo-analytics` paths are always skipped, and so are bots.

Behind a reverse proxy, configure ASP.NET Core forwarded headers or set `ClientIpHeader` together with `TrustedProxies`. A header from any other source is ignored, so it cannot be forged. Otherwise all visitors share one address and count as one. With no usable address at all (no remote IP), the rate limit is skipped and a warning is logged once. If every visitor shares one proxy address, the limit of `RateLimitPerMinute` applies to the whole site, so fix the forwarded headers first.

## Privacy model

- The visitor id is `SHA-256(siteKey, IP, User-Agent, salt, secret)`, cut to 32 hex characters. The salt changes every month (or day), so ids do not carry over.
- The raw IP and User-Agent are not stored. The event table has no column for them.
- No cookie is set unless `SaltRotation` is `Cookie`. That mode uses a random first-party id cookie and stores only its hash.
- The stored path is the page path as typed, without the query string or fragment. If your URLs carry tokens or emails in the path, they are stored for the retention period, so use `ExcludedPaths` for those routes. The referrer is kept as a host and the UTM values as sent.
- The path loses its query string. The referrer is kept as a host. Only the three `utm_` values are kept from the query.
- Device class and browser family come from the User-Agent and are stored as short labels.
- A generated secret lives in the same database as the events. Set `Secret` from an environment variable or a secret store if you want it kept apart.
- Country comes from `CountryHeader` as sent. Only use it behind a proxy that sets or overwrites that header.
- Visit stitching is per server process. With several servers, a visitor who moves between them can be counted as more than one visit.
- This describes what the code does. Whether it meets the consent rules where you operate is your call.

## Dashboard

Visitors, visits and page views for 7, 30 or 90 days, a daily bar chart, top pages, top referrers, devices and browsers. Access follows the Content section.

## Build

```
dotnet build -c Release
```

## Licence

MIT.
