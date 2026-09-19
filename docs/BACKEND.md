# Backend architecture

## Directory tree

```text
apps/api/
  src/
    Commerce.Api/             HTTP pipeline, endpoints, policies, health
    Commerce.Application/     use cases, ports, orchestration
    Commerce.Domain/          commerce entities and invariants
    Commerce.Infrastructure/  EF Core, PostgreSQL, caching, seed
    Commerce.Contracts/       stable request/response contracts
  tests/
    Commerce.UnitTests/
    Commerce.IntegrationTests/
infra/compose.yaml
docs/{BACKEND,CACHING,RESILIENCE,SECURITY,PERFORMANCE}.md
```

## Module boundaries

Catalog owns products, categories, variants, assets, prices, and inventory. Search is a catalog read capability backed by PostgreSQL. Storefront composes route-specific read models without owning transactional data. Cart owns shopper intent and never owns price truth. Checkout orchestrates a PostgreSQL transaction across cart, inventory, idempotency, and order creation. Orders own immutable purchase snapshots. Authentication supplies an authenticated customer identity; development mode uses an explicit header identity. Pricing and inventory are authoritative application ports used by checkout. Reviews supply read aggregates. Recommendations are optional and fall back to featured or category products.

Modules share IDs and contracts, never EF entities. `Commerce.Application` depends on Domain and Contracts; Infrastructure implements application ports; API is the composition root.

## Request and data flow

```text
Next.js -> ASP.NET endpoint -> application service -> HybridCache (read models)
                                             \----> EF projection -> PostgreSQL

checkout -> idempotency lookup -> DB transaction -> lock inventory rows
         -> authoritative products/prices -> decrement stock -> order snapshot
         -> clear cart -> persist idempotency result -> commit -> invalidate tags
```

Cancellation tokens flow from `HttpContext.RequestAborted` through services, EF, and cache. Storefront GET requests are safe and write no analytics or reservation state.

## PostgreSQL index plan

| Table | Indexes |
|---|---|
| categories | unique slug; parent/sort order |
| products | unique slug; category/status/id; GIN search vector; trigram title |
| variants | unique SKU; product/status |
| inventory | variant primary key; nonnegative check |
| reviews | product/status; product/rating aggregate path |
| carts | unique active customer; updated timestamp |
| cart_items | unique cart/variant; variant FK |
| orders | customer/created/id; unique order number |
| idempotency_records | unique customer/key; expiry |

Indexes are query-driven. Search uses bounded offset pagination for the assignment dataset. Production tuning must use `EXPLAIN (ANALYZE, BUFFERS)` before adding compiled queries, partitioning, or replicas.

## Language decision gates

Rust is not justified: there is no untrusted native parser, memory-safety boundary, or measured CPU workload that .NET libraries cannot handle. Go is not justified: HTTP/SSE fan-out is not a current requirement and ASP.NET Core supports the required asynchronous load. Neither service is created. Re-evaluate only with a measured, isolated workload and a stable disposable contract.
