# Read-model cache consistency and failure policy

Status: Accepted

## Context

Catalog reads are frequent and tolerate bounded staleness, while carts, checkout, orders, price, and inventory correctness do not. Valkey improves shared-cache hit rates but must not become a correctness dependency.

## Decision

PostgreSQL remains authoritative. HybridCache provides process-local L1 plus optional Valkey/Redis L2 for categories, home, product details, and suggestions. L1 is capped at five seconds; L2 expiration is endpoint-specific. Search, batch cards, carts, checkout, and orders are not application-cached.

Distributed operations have a 300 ms default provider timeout. Read failures trigger a ten-second process-local bypass and direct PostgreSQL reads; the database factory retains the wider endpoint deadline. Invalidation is best-effort and never changes a committed mutation response. Inventory changes evict the broad `products` and `homepage` tags because those are the actual cache entries carrying availability hints.

## Alternatives Considered

- Valkey as a required read store.
- Write-through cache updates in the database transaction.
- An outbox with guaranteed cache invalidation.
- No application cache.
- Per-variant availability cache entries.

## Why This Decision

The catalog is small, PostgreSQL projections are bounded, and checkout always revalidates authority. Best-effort invalidation plus bounded TTLs is simpler than transactional cache coordination and preserves availability during Valkey outages.

## Benefits

- Cache failure does not corrupt commerce state.
- Valkey can fail at runtime without failing readiness.
- Cache invalidation cannot convert a committed order into an error response.
- No wildcard scans or global flushes are required.

## Costs And Trade-offs

- Public availability hints can be stale until eviction or TTL expiry.
- Broad product/home invalidation sacrifices some hit rate for clear semantics.
- L1 and bypass state are replica-local.
- Compose starts Valkey but no longer blocks API startup on Valkey health.

## Consistency Guarantees

- No private shopper state is cached.
- Cached price and availability are presentation hints, not checkout inputs.
- Product details expire within five minutes, home within two minutes, categories within ten minutes, and suggestions within one minute; L1 is at most five seconds.
- Best-effort invalidation reduces normal staleness but is not a transactional guarantee.

## Failure And Degradation Behavior

Distributed-cache connection or timeout failures fall through to PostgreSQL and increment failure metrics. Invalidation failures are logged with exceptions and ignored after the database commit. PostgreSQL failure still fails readiness and commerce requests.

## Reconsider When

- Catalog load threatens PostgreSQL headroom during Valkey outages.
- Multiple catalog writers make TTL-based convergence insufficient.
- Inventory hints require a measured freshness SLA below current TTLs.
- An outbox already exists for other post-commit work.
- Replica count makes process-local stampede protection insufficient.
