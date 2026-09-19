# Resilience

| Dependency | Tier | Failure behavior |
|---|---|---|
| PostgreSQL | 0 required | readiness fails; requests return safe 503 problems |
| Valkey/Redis | 1 acceleration | bypass cache and query PostgreSQL |
| recommendations | 2 enhancement | return featured/category fallback |
| telemetry exporter | 2 enhancement | drop telemetry without blocking requests |
| realtime transport | 2 enhancement | normal HTTP refresh remains functional |

Autocomplete, search, catalog, and checkout have separate rate-limit and timeout policies. Queues are bounded or disabled. Cancellation is not logged as an application error. Non-idempotent mutations are not blindly retried. Checkout correctness is independent of cache and optional dependencies.
