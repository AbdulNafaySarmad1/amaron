# Caching

| Data | L1/L2 TTL | Invalidation | Fallback |
|---|---:|---|---|
| category navigation | 1/10 min | `categories`, `homepage` | PostgreSQL projection |
| home read model | 30 sec/2 min | `homepage`, `products`, `deals` | compose required sections; omit optional sections |
| product detail | 30 sec/5 min | `product:{id}`, `product:{slug}` | PostgreSQL projection |
| product cards/batch | 30 sec/2 min | product tags | PostgreSQL projection |
| autocomplete | 15 sec/1 min | `search-suggestions` | bounded PostgreSQL query |
| search pages | selective 15 sec/1 min | catalog version/TTL | PostgreSQL search |
| availability hint | at most 10 sec | inventory tag | PostgreSQL; never checkout truth |
| carts/orders/checkout | not cached | n/a | PostgreSQL only |

`HybridCache` provides process-local L1, optional RESP-compatible L2, and in-process stampede protection. `CACHE_PROVIDER` selects `valkey`, `redis`, or `memory`; business code depends only on `IReadModelCache`. Distributed-cache failures are logged, briefly bypassed by a circuit-style cooldown, and fall through to the database. No wildcard scans or global flushes are used.
