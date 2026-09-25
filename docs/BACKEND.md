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

Catalog owns products, categories, variants, and assets. Operations owns governed price history, demand observations and forecasts, warehouse stock and ledger movements, replenishment proposals, promotions, alerts, and operational audit. Supply chain owns supplier organizations and source terms, RFQs and quotations, purchase orders, ASNs, fiscal invoices, physical receipts, lot balances, document matching, warehouse tasks, and blind cycle counts. Search is a catalog read capability backed by PostgreSQL. Storefront composes route-specific read models without owning transactional data. Cart owns shopper intent and never owns price truth. Checkout orchestrates a PostgreSQL transaction across cart, materialized price, aggregate and warehouse inventory, idempotency, and order creation. Orders own immutable purchase snapshots. Authentication validates OIDC access tokens and maps `(issuer, sub)` to an internal `ApplicationUser`; development mode can use explicit header identity.

These are logical capabilities inside a layered monolith, not independently enforced modules. Application services share the `ICommerceDbContext` EF abstraction and domain entities. `Commerce.Application` depends on Domain and Contracts; Infrastructure implements persistence/cache abstractions; API is the composition root. Split ports or services only when measured ownership or scaling needs justify the added coordination.

Administrative product and inventory writes use strong resource versions through `ETag` and `If-Match`. A committed mutation performs best-effort HybridCache and public ASP.NET output-cache invalidation. Stale writers receive `409 concurrency_conflict`; missing preconditions receive `428 precondition_required`.

## Identity and authorization

The API validates JWTs locally from configured OIDC metadata/JWKS; Keycloak is not called per request. Issuer, audience, signature, lifetime, token type, and `sub` are validated. Unknown signing keys trigger metadata refresh, supporting rotation while the provider is reachable and old keys remain published through outstanding-token expiry.

Keycloak client roles are normalized into application permissions. Admin routes require `Administration.Access` plus a resource policy. Operations policies separately cover operations, pricing, demand, inventory, replenishment, promotions, suppliers, procurement, receiving, invoice matching, and audit. Supplier portal routes require `Supplier.Portal`, then application queries restrict purchase orders, ASNs, and invoices to the single active `supplier_users` membership. Only policies attached to endpoints are active controls; defining a policy does not create an endpoint.

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

receipt -> idempotency advisory lock -> purchase-order row lock -> inventory row locks
        -> validate physical = accepted + damaged + quarantined
        -> disposition balances + immutable ledger -> accepted-only checkout aggregate
        -> purchase-order receipt state -> commit
```

Cancellation tokens flow from `HttpContext.RequestAborted` through services, EF, and cache. Storefront GET requests are safe and write no analytics or reservation state.

## Sellable stock

Availability shown to shoppers uses one definition, `SellableStock.Query`, which mirrors checkout allocation: when a warehouse stocks the variant, the sum of each warehouse's available-to-sell (on hand less reserved, safety stock and unavailable, never negative), capped by the aggregate on hand; otherwise the aggregate. Product cards, the `available` filter, product-page option availability and the bag's add check all use it, so the storefront never offers stock that checkout would refuse. Checkout itself remains authoritative and re-checks under lock.

## Search

`IProductSearch` (Application) is implemented by `PostgresProductSearch` (Infrastructure) as one composable SQL SELECT that EF joins into catalog queries, so filters, facets, category scope and paging are unchanged. A product matches when all query words appear in title, brand or description (`search_all`, English config), when any word appears in the title or brand (`search_title`), when the title contains the text, or when the shopper's translated title matches (`product_translations.search_title`, simple config). Typo matching (`word_similarity` >= 0.4 on the title, queries of 4+ characters) applies only when nothing matches exactly, which keeps "cable" from matching "Portable Speaker". With a query and no explicit sort, results are ordered by relevance through a join, never a per-row subquery. The tsvector columns are generated by PostgreSQL and live outside the EF model.

## Product relationships

`product_relationships` links a source product to a target with a type (Accessory, Compatible, Complementary, Alternative, Upgrade, FrequentlyBoughtWith), a relevance score and a shopper-facing reason. The product endpoint returns them grouped in that order; `GET /api/catalog/products/addition?productIds=` returns at most one sellable setup item (accessory, compatible or complementary) not already in the given set, or 204. Relationships drive merchandising only and never affect catalog filtering.

## Catalog translations

`product_translations` (title, short description, description, SEO title and description) and `category_translations` (name) are keyed by entity and language. Every catalog read accepts `locale`; each field falls back to the canonical English record, and responses report the language actually served (`Locale`, `CategoryLocale`) so the storefront can mark text correctly. Cache keys include the language. Specifications, brands and relationship reasons are not translated yet.

## Bot protection and sitemaps

When `TURNSTILE_SECRET_KEY` is set, `POST /api/checkout/confirm` requires an `X-Turnstile-Token` that Cloudflare's siteverify accepts; otherwise it returns 403 `challenge_required` before any cart, stock or payment work. Verification runs server-side only (`CloudflareTurnstile`, 5-second timeout). If Cloudflare itself is unreachable or returns 5xx, checkout fails open and logs a warning: an outage at the challenge provider must not stop genuine orders, and the order path keeps its own rate limits and idempotency. Unset, the check is off.

`GET /api/catalog/sitemap/products?page=&pageSize=` pages active product slugs with their last update, 10,000 per page, for the storefront's sitemap index.

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
