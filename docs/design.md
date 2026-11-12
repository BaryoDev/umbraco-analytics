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

Visit ids are assigned per server process, so with several servers the same visitor can get more than one visit id. A visit is one visitor with gaps under `VisitIdleMinutes` (default 30). The writer keeps the last seen time per visitor in memory and falls back to the newest event row on a cache miss. A new visit gets a random id.

## Tables

Created by a package migration plan (`BaryoDev.Umbraco.Analytics`), NPoco. Creation uses no raw SQL. Stats and the retention job use plain SQL that works on SQLite and SQL Server. Package migrations must be allowed to run (the Umbraco default).

`baryoAnalyticsEvent`: Id, OccurredUtc, Day (UTC date, for portable grouping), VisitorId, VisitId, Path, ContentKey (null when the request is not published content), ReferrerHost, UtmSource, UtmMedium, UtmCampaign, Device, Browser, Country.

`baryoAnalyticsDaily`: Id, Day, Dimension (`total`, `page`, `referrer`, `device`, `browser`, `country`), Value, Visitors, Visits, PageViews.

## Collection

- Script mode (default): a tiny script sends `navigator.sendBeacon` to `POST /baryo-analytics/collect` with path, referrer and query string. Falls back to `fetch` with `keepalive`. The server percent-decodes the path, then accepts only same-site page paths: it must start with a single `/`, hold no control characters or backslashes, and not end in an asset extension.
- Middleware mode: the server records GET requests that return 200 HTML. Content key comes from the Umbraco route values.
- In `Cookie` mode with middleware, the cookie is set only on HTML 200 responses to requests that pass the DNT, GPC, bot and path checks. The collector never throws; a fault is logged once and the hit is dropped. On shutdown the queue is saved within the host shutdown time.
- Only one mode is active. The other path does nothing, so a page is never counted twice.
- Skipped: DNT or Sec-GPC when the options say so, bot user agents, empty user agents, `/umbraco` and the configured excluded paths.
- Each client is limited to `RateLimitPerMinute` hits (fixed one-minute window, keyed by a salted hash of the IP). The limiter holds at most 50,000 buckets per window. When full, hits from unseen clients are dropped until the next window. Extra hits are dropped and the endpoint still answers 202. The collect body is capped at 4 KB, and path, referrer and UTM values are cut to the column lengths.
- Events go into a bounded in-memory queue. A background writer saves them in batches. When the queue is full, new events are dropped instead of slowing the site.
- The client IP comes from the connection. `ClientIpHeader` is read only when the connection is from an entry in `TrustedProxies`; the chain is walked from the right and the first untrusted hop is used. The same value feeds the hash and the rate limiter.
- Country is read from a header (`CF-IPCountry` by default) only if it is present. There is no GeoIP lookup.

## Aggregation and retention

A background job (every 6 hours, single or scheduling server only) handles every day older than `RetentionDays` (default 395, minimum 1). For each day it first writes the daily rows in one transaction. The `total` row is written last and marks the day as rolled. Then it deletes the raw events in chunks of 1,000, so the writer is not blocked. If the job stops halfway, the next run sees the `total` row and only finishes deleting. A late event for an already rolled day is deleted without being counted.

Daily `page` and `referrer` rows older than twice `RetentionDays` are deleted in chunks of 1,000, because paths and referrer hosts can hold personal data. The `total`, device, browser and country rows stay. The daily rows are written with multi-row inserts of 200.

The dashboard reads raw events for days with no `total` row and daily rows for the rest, so changing `RetentionDays` leaves no gap and no double count. Visitors and visits are summed per day on rolled days, and a visit that crosses UTC midnight counts once per day. A range that includes rolled days over-counts people who came back on several days. Ranges of 7, 30 and 90 days stay inside the default retention and are exact. Top lists are ranked and limited in SQL. The path is stored as received, so grouping follows the database collation (case-insensitive on SQL Server, case-sensitive on SQLite).

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
- Behind a proxy, the site must configure forwarded headers or set `ClientIpHeader` and `TrustedProxies`, otherwise every visitor looks the same.
- Bot detection is a user agent substring list.

## Paid-tier candidates

- History past the retention window (keep aggregates forever, longer raw retention).
- CSV and JSON export, scheduled email reports.
- Goals and custom events, campaign reports.
- Multi-site views for one backoffice, per-section access.
- GeoIP from a bundled database.
