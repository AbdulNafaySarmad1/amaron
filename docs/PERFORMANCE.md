# Performance

Storefront endpoints compose route-shaped projections to avoid frontend chatter. Product cards never include descriptions or complete specification trees. EF reads use `AsNoTracking`, server-side projections, deterministic bounded pagination, and cancellation. Search uses PostgreSQL full-text and trigram indexes. JSON compression is enabled for worthwhile response sizes while images and models are served by object storage/CDN URLs.

No latency or throughput number is claimed without a measured run. Track request, database, cache, search, checkout, and order durations plus cache outcomes and rate-limit rejections through .NET `ActivitySource` and `Meter`, which are OpenTelemetry-compatible.

## Load testing

The checked-in k6 profile runs four concurrent representative scenarios: homepage reads, autocomplete bursts, bounded search, and catalog reads mixed with cart writes. Start the normal stack, then run:

```powershell
docker compose -f infra/compose.yaml --profile load run --rm loadtest
```

The local smoke thresholds are an error rate below 1%, p95 below 1 second, and p99 below 2 seconds. They are regression tripwires, not production capacity claims. Capture CPU, memory, PostgreSQL activity, and cache metrics alongside k6 output before making scaling decisions.
