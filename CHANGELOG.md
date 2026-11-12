# Changelog

## Unreleased

- Daily page and referrer rows are deleted after another `RetentionDays`, so paths no longer live forever. Totals, device, browser and country rows are kept.
- The rollup writes daily rows in multi-row inserts of 200.
- The visitor map in the writer has a hard cap of 20,000 entries.
- A warning is logged for each minute where every hit came from one address and none had a forwarded header (a proxy that is not configured).
- The tracker.js header says that Cookie salt mode sets a first-party cookie.

## 0.1.0

First public release for Umbraco 17. Page view counts, cookie-free by default, stored in the site database, sent by a small beacon script, with a backoffice dashboard. Raw events older than the retention period (395 days by default) are rolled up into daily rows and deleted. Daily rows are kept.
