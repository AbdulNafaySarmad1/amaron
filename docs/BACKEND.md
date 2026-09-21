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

Catalog owns products, categories, variants, and assets. Operations owns governed price history, demand observations and forecasts, warehouse stock and ledger movements, replenishment proposals, promotions, alerts, and operational audit. Search is a catalog read capability backed by PostgreSQL. Storefront composes route-specific read models without owning transactional data. Cart owns shopper intent and never owns price truth. Checkout orchestrates a PostgreSQL transaction across cart, materialized price, aggregate and warehouse inventory, idempotency, and order creation. Orders own immutable purchase snapshots. Authentication validates OIDC access tokens and maps `(issuer, sub)` to an internal `ApplicationUser`; development mode can use explicit header identity.

These are logical capabilities inside a layered monolith, not independently enforced modules. Application services share the `ICommerceDbContext` EF abstraction and domain entities. `Commerce.Application` depends on Domain and Contracts; Infrastructure implements persistence/cache abstractions; API is the composition root. Split ports or services only when measured ownership or scaling needs justify the added coordination.

Administrative product and inventory writes use strong resource versions through `ETag` and `If-Match`. A committed mutation performs best-effort HybridCache and public ASP.NET output-cache invalidation. Stale writers receive `409 concurrency_conflict`; missing preconditions receive `428 precondition_required`.

## Identity and authorization

The API validates JWTs locally from configured OIDC metadata/JWKS; Keycloak is not called per request. Issuer, audience, signature, lifetime, token type, and `sub` are validated. Unknown signing keys trigger metadata refresh, supporting rotation while the provider is reachable and old keys remain published through outstanding-token expiry.

Keycloak client roles are normalized into application permissions. Admin routes require `Administration.Access` plus a resource policy. Operations policies separately cover operations, pricing read/manage/approve, demand read/manage, inventory read/manage, replenishment read/manage, promotions read/manage, and operational audit. Only policies attached to endpoints are active controls; defining a policy does not create an endpoint.

The identity provider controls authentication and coarse assignment. ASP.NET policies control capability entry, and application queries control object/business authorization. Order reads default to the current internal user; `Orders.ReadAny` passes an explicit flag that broadens the query. This keeps ownership checks at the data boundary and returns not found for unauthorized object lookup. Keycloak Authorization Services is not used. No service identity exists today; add one only for a concrete non-user caller.

`application_users` has a unique `(IdentityIssuer, ExternalSubject)` index. First private use creates a UUIDv7 internal user, handling concurrent creation by re-reading the unique row. Carts, checkout idempotency, and orders use that internal ID rather than a provider subject.

## Request and data flow

```text
Next.js -> ASP.NET endpoint -> application service -> HybridCache (read models)
                                             \----> EF projection -> PostgreSQL

checkout -> idempotency lookup -> DB transaction -> lock cart -> reload cart items
         -> lock inventory rows in stable order
         -> materialized authoritative price -> decrement aggregate and warehouse stock
         -> append inventory ledger -> order snapshot
         -> clear cart -> persist idempotency result -> commit -> invalidate tags

price activator -> advisory transaction lock -> apply/expire approved schedules
                -> precedence resolution -> materialize variant price -> invalidate tags
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
| price_records | variant/effective interval; status/effective start |
| warehouse_stock | warehouse/variant primary key; variant/warehouse lookup |
| inventory_ledger | variant/warehouse/created timestamp |
| operations_audit | resource/type/time; created timestamp |

Indexes are query-driven. Search uses bounded offset pagination and case-normalized substring matching for the assignment dataset; count and page rows are separate read-committed statements and can shift under concurrent catalog writes. Production tuning must use `EXPLAIN (ANALYZE, BUFFERS)` before adding full-text search, functional indexes, compiled queries, partitioning, or replicas.

## Language decision gates

Rust is not justified: there is no untrusted native parser, memory-safety boundary, or measured CPU workload that .NET libraries cannot handle. Go is not justified: HTTP/SSE fan-out is not a current requirement and ASP.NET Core supports the required asynchronous load. Neither service is created. Re-evaluate only with a measured, isolated workload and a stable disposable contract.

## Operations

Migrations are a deployment concern. `APPLY_MIGRATIONS=true` is intended only for the single-instance development container; production deployment jobs should run migrations before replicas start. Configure `AUTH_AUTHORITY`, `AUTH_AUDIENCE`, and `AUTH_ROLE_CLIENT_ID`; use `AUTH_METADATA_ADDRESS` only when the API needs a private discovery route while tokens retain the public issuer. Configure explicit frontend origins and trusted proxy IPs. `OTEL_EXPORTER_OTLP_ENDPOINT` exports ASP.NET Core traces, HTTP client traces, runtime metrics, and commerce-specific metrics including authentication failures and authorization denials. PostgreSQL alone controls API readiness; the protected `/health/identity` diagnostic separately checks discovery. See ADR 013 for identity and ADR 014 for operations authority and safety.
