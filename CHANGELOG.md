# Changelog

## 0.3.0

### Changes

- The dashboard now uses the Umbraco UI elements and the backoffice theme (light, dark and high contrast) instead of the baryo.dev colors. Only the baryo.dev logo and the name stay in the header.
- The stylesheet is now `dashboard.css`. The dashboard script and stylesheet load with the package version query so an upgrade does not serve a cached copy.

## 0.2.1

### Breaking

- Daily page and referrer rows are kept again by default, as in 0.1.0. The 0.2.0 purge runs only when the new option `DeleteOldPageAndReferrerRows` is `true`.
- To keep the 0.2.0 behaviour, set `BaryoDev:Analytics:DeleteOldPageAndReferrerRows` to `true`.
- Rows already deleted by 0.2.0 are not restored.

## 0.2.0

### Breaking

- The collect endpoint drops a beacon unless the browser sends `Sec-Fetch-Site: same-origin`, or its Origin (or Referer) host is the request host or listed in the new `AllowedOrigins` option. The endpoint still answers 202 and logs one warning per process on the first drop. Behind a proxy that rewrites the Host header, keep the original Host, enable `XForwardedHost` in forwarded headers, or list the page host in `AllowedOrigins`. A site served on more than one domain, or a tracker served from another domain, must list the page hosts in `AllowedOrigins` (`host`, `host:port` or a full origin).
- 0.1.0 kept daily rows forever. Daily page and referrer rows are now deleted once they are older than twice `RetentionDays` (790 days with the default 395), so paths no longer live forever. 0.2.1 makes this opt-in. Totals, device, browser and country rows are still kept. Raise `RetentionDays` to keep page and referrer rows longer.

### Changes

- New option `RequirePublishedContent` (default `false`) drops script-mode hits whose path is not published content.
- A warning is logged once when the connection address is private or loopback and `ClientIpHeader` is not set. The README says what that means for the rate limit.
- New "Last 24 hours" range in the dashboard, with hourly buckets, top pages and top referrers. It reads the raw events through a new endpoint, `GET baryo-analytics/last-24-hours`. Existing endpoints and ranges are unchanged. No schema change.
- The rollup writes daily rows in multi-row inserts of 200.
- The visitor map in the writer has a hard cap of 20,000 entries.
- A warning is logged, at most once an hour, when a whole minute of hits came from one address and none had a forwarded header (a proxy that is not configured).
- The tracker.js header says that Cookie salt mode sets a first-party cookie.

## 0.1.0

First public release for Umbraco 17. Page view counts, cookie-free by default, stored in the site database, sent by a small beacon script, with a backoffice dashboard. Raw events older than the retention period (395 days by default) are rolled up into daily rows and deleted. Daily rows are kept.
