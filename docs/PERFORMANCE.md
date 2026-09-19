# Performance

Storefront endpoints compose route-shaped projections to avoid frontend chatter. Product cards never include descriptions or complete specification trees. EF reads use `AsNoTracking`, server-side projections, deterministic bounded pagination, and cancellation. Search uses PostgreSQL full-text and trigram indexes. JSON compression is enabled for worthwhile response sizes while images and models are served by object storage/CDN URLs.

No latency or throughput number is claimed without a measured run. Track request, database, cache, search, checkout, and order durations plus cache outcomes and rate-limit rejections through .NET `ActivitySource` and `Meter`, which are OpenTelemetry-compatible.
