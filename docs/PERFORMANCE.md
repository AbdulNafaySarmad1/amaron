# Performance

Storefront endpoints compose route-shaped projections to avoid frontend chatter. Product cards never include descriptions or complete specification trees. EF reads use `AsNoTracking`, server-side projections, deterministic bounded pagination, and cancellation. Search uses generated full-text columns with GIN indexes, prefix autocomplete, trigram-indexed substring and typo matching, one index-served branch per way of matching (see `BACKEND.md`, Search). Listings page on their sort keys and project card data for the page only. Index changes require measured query plans; the 125,000-product measurements and the decisions they drove are in `BACKEND.md`, Synthetic catalog and scale.

JSON compression is enabled for worthwhile response sizes. Seeded asset URLs are contract examples; the frontend intentionally uses generated CSS product visuals rather than loading those URLs, and has no 3D or scroll-driven animation.

No production latency or throughput number is claimed without a measured run. Current instrumentation covers HTTP/runtime signals and commerce-specific cache outcomes, search duration, checkout duration/failures, order count, and rate-limit rejection. Database command/lock duration is not yet instrumented and is a known observability boundary.

API timeout budgets are documented in `RESILIENCE.md`. Frontend request and enhancement budgets are documented in `FRONTEND.md`. Reconsider query/index design, ISR, or richer prefetch only from measured plans, server load, Core Web Vitals, and interaction latency.

## Load testing

The checked-in k6 profile runs four concurrent representative scenarios: homepage reads, autocomplete bursts, bounded search, and catalog reads mixed with cart writes. Cart writes use a product that is sellable when the run starts, since the API refuses stock checkout could not allocate. All requests come from one client, so the autocomplete limit (60 per minute per client) is shared between runs: leave a minute between consecutive runs. Start the normal stack, then run:

```powershell
docker compose -f infra/compose.yaml --profile load run --rm loadtest
```

The local smoke thresholds are an error rate below 1%, p95 below 1 second, and p99 below 2 seconds. They are regression tripwires, not production capacity claims. Capture CPU, memory, PostgreSQL activity, and cache metrics alongside k6 output before making scaling decisions.

Latest local patch-pass run (2026-09-20): 122 requests, 0% failed, p95 84.24 ms, p99 123.35 ms. This is a loopback development measurement at approximately six requests/second, not a production SLO or capacity result.
