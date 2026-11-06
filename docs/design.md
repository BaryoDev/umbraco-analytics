# Design

Page view analytics that live in the site's own Umbraco database. The approach follows Umami (MIT): count visitors with a salted hash, not a cookie. The code is written from scratch.

## Visitor id

`visitorId = first 32 hex of SHA-256(siteKey | ip | userAgent | salt | secret)`

- `salt` rotates. `Monthly` (default) uses the UTC month, `Daily` uses the UTC date. The salt is itself `SHA-256("salt" | period | secret)`.
- When the salt changes the same person gets a new id. Nobody can be followed across periods.
- `secret` comes from `BaryoDev:Analytics:Secret`. If it is missing, a random one is generated on first run and kept in Umbraco's key-value store (`umbracoKeyValue`). It is never written to a file.
- `Cookie` mode skips hashing the IP. It sets a first-party random id cookie (`ba_vid`, HttpOnly, SameSite Lax) and stores `SHA-256(siteKey | cookie | secret)`. Off by default.
- The IP and User-Agent exist in memory for the length of one request. They are only inputs to the hash.

## Visits

A visit is one visitor with gaps under `VisitIdleMinutes` (default 30). The writer keeps the last seen time per visitor in memory and falls back to the newest event row on a cache miss. A new visit gets a random id.

## Tables

Created by a package migration plan (`BaryoDev.Umbraco.Analytics`), NPoco, no raw SQL, so it runs on SQLite and SQL Server.

`baryoAnalyticsEvent`: Id, OccurredUtc, Day (UTC date, for portable grouping), VisitorId, VisitId, Path, ContentKey (null when the request is not published content), ReferrerHost, UtmSource, UtmMedium, UtmCampaign, Device, Browser, Country.

`baryoAnalyticsDaily`: Id, Day, Dimension (`total`, `page`, `referrer`, `device`, `browser`, `country`), Value, Visitors, Visits, PageViews.

## Collection

- Script mode (default): a tiny script sends `navigator.sendBeacon` to `POST /baryo-analytics/collect` with path, referrer and query string. Falls back to `fetch` with `keepalive`.
- Middleware mode: the server records GET requests that return 200 HTML. Content key comes from the Umbraco route values.
- Only one mode is active. The other path does nothing, so a page is never counted twice.
- Skipped: DNT or Sec-GPC when the options say so, bot user agents, empty user agents, `/umbraco` and the configured excluded paths.
- Each client is limited to `RateLimitPerMinute` hits (fixed one-minute window, keyed by a salted hash of the IP). Extra hits are dropped and the endpoint still answers 202. The collect body is capped at 4 KB, and path, referrer and UTM values are cut to the column lengths.
- Events go into a bounded in-memory queue. A background writer saves them in batches. When the queue is full, new events are dropped instead of slowing the site.
- Country is read from a header (`CF-IPCountry` by default) only if it is present. There is no GeoIP lookup.

## Aggregation and retention

A daily background job (single or scheduling server only) handles every day older than `RetentionDays` (default 395). In one scope per day it deletes any earlier aggregate rows for that day, writes the new ones from the raw events, then deletes the raw events. Rolled-up rows keep counts only.

The dashboard reads raw events for the recent window and aggregate rows for days older than the raw cutoff. Visitors from aggregate days are summed per day, so a range that crosses the cutoff slightly over-counts people who came back on several days. Ranges of 7, 30 and 90 days stay inside the default retention and are exact.

## Dashboard

Management API `GET /umbraco/management/api/v1/baryo-analytics/summary?days=7|30|90`, Content section access only. The dashboard shows visitors, visits, page views, a daily SVG bar chart, top pages, top referrers, devices and browsers.

## Privacy claims backed by code

- No raw IP or User-Agent column exists. See `EventRow`.
- No cookie is set unless `SaltRotation` is `Cookie`. See `Collector`.
- Ids change when the salt rotates. See `VisitorHasher`.
- The referrer is reduced to its host, the path loses its query string, only `utm_source`, `utm_medium` and `utm_campaign` are kept from the query.
- The secret is not in the repo. See `SecretProvider`.

## Not covered

- Anyone can POST fake page views to the collect endpoint. A per-client rate limit drops floods, but a spread-out attacker can still add fake views.
- Behind a proxy, the site must configure forwarded headers or set `ClientIpHeader`, otherwise every visitor looks the same.
- Bot detection is a user agent substring list.

## Paid-tier candidates

- History past the retention window (keep aggregates forever, longer raw retention).
- CSV and JSON export, scheduled email reports.
- Goals and custom events, campaign reports.
- Multi-site views for one backoffice, per-section access.
- GeoIP from a bundled database.
