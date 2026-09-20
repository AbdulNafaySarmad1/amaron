# Backend architecture

## Directory tree

```text
apps/api/
  src/
    Commerce.Api/             HTTP pipeline, endpoints, policies, health
    Commerce.Application/     use cases and orchestration
    Commerce.Domain/          commerce entities and invariants
    Commerce.Infrastructure/  EF Core, PostgreSQL, caching, seed
    Commerce.Contracts/       stable request/response contracts
  tests/
    Commerce.UnitTests/
    Commerce.IntegrationTests/
infra/compose.yaml
docs/{BACKEND,FRONTEND,CACHING,RESILIENCE,SECURITY,PERFORMANCE}.md
docs/adr/                     durable architectural decisions
```

## Module boundaries

Catalog owns products, categories, variants, assets, prices, and inventory. Search is a catalog read capability backed by PostgreSQL. Storefront composes route-specific read models without owning transactional data. Cart owns shopper intent and never owns price truth. Checkout orchestrates a PostgreSQL transaction across cart, inventory, idempotency, and order creation. Orders own immutable purchase snapshots. Authentication supplies an authenticated customer identity; development mode uses an explicit header identity. Reviews supply read aggregates. Recommendation failure returns an empty optional section and a degradation flag.

These are logical capabilities inside a layered monolith, not independently enforced modules. Application services share the `ICommerceDbContext` EF abstraction and domain entities. `Commerce.Application` depends on Domain and Contracts; Infrastructure implements persistence/cache abstractions; API is the composition root. Split ports or services only when measured ownership or scaling needs justify the added coordination.

Administrative product and inventory writes use strong resource versions through `ETag` and `If-Match`. A committed mutation performs best-effort HybridCache and public ASP.NET output-cache invalidation. Stale writers receive `409 concurrency_conflict`; missing preconditions receive `428 precondition_required`.

## Request and data flow

```text
Next.js -> ASP.NET endpoint -> application service -> HybridCache (read models)
                                             \----> EF projection -> PostgreSQL

checkout -> idempotency lookup -> DB transaction -> lock cart -> reload cart items
         -> lock inventory rows in stable order
         -> authoritative products/prices -> decrement stock -> order snapshot
         -> clear cart -> persist idempotency result -> commit -> invalidate tags
```

Cancellation tokens flow from `HttpContext.RequestAborted` through services, EF, and cache. Storefront GET requests are safe and write no analytics or reservation state.

## PostgreSQL index plan

| Table | Indexes |
|---|---|
| categories | unique slug; parent/sort order |
| products | unique slug; category/status/id; trigram title |
| variants | unique SKU; product/status |
| inventory | variant primary key; nonnegative check |
| reviews | product/status; product/rating aggregate path |
| carts | unique active customer; updated timestamp |
| cart_items | unique cart/variant; variant FK |
| orders | customer/created/id; unique order number |
| idempotency_records | unique customer/key; created timestamp |

Indexes are query-driven. Search uses bounded offset pagination and case-normalized substring matching for the assignment dataset; count and page rows are separate read-committed statements and can shift under concurrent catalog writes. Production tuning must use `EXPLAIN (ANALYZE, BUFFERS)` before adding full-text search, functional indexes, compiled queries, partitioning, or replicas.

## Language decision gates

Rust is not justified: there is no untrusted native parser, memory-safety boundary, or measured CPU workload that .NET libraries cannot handle. Go is not justified: HTTP/SSE fan-out is not a current requirement and ASP.NET Core supports the required asynchronous load. Neither service is created. Re-evaluate only with a measured, isolated workload and a stable disposable contract.

## Operations

Migrations are a deployment concern. `APPLY_MIGRATIONS=true` is intended only for the single-instance development container; production deployment jobs should run migrations before replicas start. Configure `AUTH_AUTHORITY` and `AUTH_AUDIENCE` for JWT validation. Configure `OTEL_EXPORTER_OTLP_ENDPOINT` to export ASP.NET Core traces, HTTP client traces, runtime metrics, and commerce-specific cache/search/checkout/order metrics. PostgreSQL controls readiness; Valkey and telemetry remain fail-open accelerators. See the ADRs for checkout lock order, idempotency, and cache consistency.
