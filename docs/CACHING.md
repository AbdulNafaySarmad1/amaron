# Caching

| Data | L1/L2 TTL | Invalidation | Fallback |
|---|---:|---|---|
| category navigation | 5 sec/10 min | `categories`, `homepage` | PostgreSQL projection |
| home read model | 5 sec/2 min | `homepage`, `products` | PostgreSQL projection |
| product detail | 5 sec/5 min | `products`, `product:{slug}` | PostgreSQL projection |
| autocomplete | 5 sec/1 min | `search-suggestions` | bounded PostgreSQL query |
| search, batch, related cards | not application-cached | n/a | PostgreSQL projection |
| carts/orders/checkout | not cached | n/a | PostgreSQL only |

`HybridCache` provides process-local L1, optional RESP-compatible L2, and in-process stampede protection. `CACHE_PROVIDER` selects `valkey`, `redis`, or `memory`; `CACHE_ENABLED=false` disables the distributed provider but retains memory caching. Business code depends only on `IReadModelCache`.

Distributed-cache operations have a 300 ms default provider timeout (`CACHE_OPERATION_TIMEOUT_MS`). Connection and timeout failures are logged, bypassed for ten seconds in that API process, and fall through to PostgreSQL. The PostgreSQL factory runs under the endpoint cancellation budget rather than the cache timeout, avoiding duplicate database work on cache misses. Invalidation is best-effort and broad where availability is embedded in product/home read models. A failed invalidation can expose stale presentation data until TTL expiry, but checkout never trusts cached price or inventory. No wildcard scans or global flushes are used.

ASP.NET output cache, browser/CDN cache headers, and any future Next full-route cache are separate failure and invalidation domains. The current storefront request-renders public routes, so API/HybridCache is the primary cache layer. See ADR 0002.
