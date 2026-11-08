# Changelog

## 0.1.0

First public release for Umbraco 17. Page view counts, cookie-free by default, stored in the site database, sent by a small beacon script, with a backoffice dashboard. Raw events older than the retention period (395 days by default) are rolled up into daily rows and deleted. Daily rows are kept.
