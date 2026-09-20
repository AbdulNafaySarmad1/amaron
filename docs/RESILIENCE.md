# Resilience

| Dependency | Tier | Failure behavior |
|---|---|---|
| PostgreSQL | 0 required | readiness fails; requests return safe 503 problems |
| Valkey/Redis | 1 acceleration | runtime bypass and PostgreSQL query; does not gate API startup/readiness |
| product recommendations | 2 enhancement | product detail returns an empty recommendation list with `isDegraded=true` |
| telemetry exporter | 2 enhancement | drop telemetry without blocking requests |
| Motion/GSAP/WebGL | 2 enhancement | static server-rendered content and CSS visual remain usable |

## Deadlines and limits

| Workload | API timeout | Rate-limit policy |
|---|---:|---|
| autocomplete | 2 sec | 60/min sliding window |
| catalog | 5 sec | shared catalog bucket, 120/min |
| search | 8 sec | shared catalog bucket, 120/min |
| cart/order reads | 8 sec | cart bucket, 40/min |
| cart writes | 10 sec | cart bucket, 40/min |
| checkout | 20 sec | 5/min fixed window |
| admin | 5 sec | 20/min fixed window |

Queues are disabled. Limits are process-local operational protection, not a cluster-wide security control; replicas multiply capacity. A gateway is required if global abuse limits become necessary.

Frontend deadlines exceed API deadlines by approximately two seconds for checkout and otherwise bound network stalls at eight to ten seconds. Neither layer blindly retries mutations. Checkout correctness is independent of cache and optional dependencies; ambiguous checkout outcomes are recovered with the same idempotency key and payload.

## Failure domains

- PostgreSQL is the Tier 0 dependency for every commerce capability.
- The API modular monolith is one process failure domain.
- L1 cache, cooldown, output cache, and rate limits are replica-local.
- Valkey L2 is shared but optional at runtime.
- The Next server is a separate request-rendering failure domain.
- Browser-to-API private calls additionally depend on CORS, public API reachability, and production identity propagation.
