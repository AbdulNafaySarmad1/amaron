# Performance

Storefront endpoints compose route-shaped projections to avoid frontend chatter. Product cards never include descriptions or complete specification trees. EF reads use `AsNoTracking`, server-side projections, deterministic bounded pagination, and cancellation. Search currently uses bounded case-normalized substring predicates; the title trigram index is available, but no full-text vector/index is claimed. Index changes require measured query plans.

JSON compression is enabled for worthwhile response sizes. Seeded asset URLs are contract examples; the current frontend intentionally uses generated CSS visuals and a procedural hero scene rather than loading those URLs.

No production latency or throughput number is claimed without a measured run. Current instrumentation covers HTTP/runtime signals and commerce-specific cache outcomes, search duration, checkout duration/failures, order count, and rate-limit rejection. Database command/lock duration is not yet instrumented and is a known observability boundary.

API timeout budgets are documented in `RESILIENCE.md`. Frontend request and enhancement budgets are documented in `FRONTEND.md`. Reconsider query/index design, ISR, or richer prefetch only from measured plans, server load, Core Web Vitals, and interaction latency.

## Load testing

The checked-in k6 profile runs four concurrent representative scenarios: homepage reads, autocomplete bursts, bounded search, and catalog reads mixed with cart writes. Start the normal stack, then run:

```powershell
docker compose -f infra/compose.yaml --profile load run --rm loadtest
```

The local smoke thresholds are an error rate below 1%, p95 below 1 second, and p99 below 2 seconds. They are regression tripwires, not production capacity claims. Capture CPU, memory, PostgreSQL activity, and cache metrics alongside k6 output before making scaling decisions.

Latest local patch-pass run (2026-09-20): 122 requests, 0% failed, p95 84.24 ms, p99 123.35 ms. This is a loopback development measurement at approximately six requests/second, not a production SLO or capacity result.
