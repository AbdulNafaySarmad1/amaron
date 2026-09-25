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

Availability shown to shoppers uses one definition, `SellableStock.Query`, which mirrors checkout allocation: when a warehouse stocks the variant, the sum of each warehouse's available-to-sell (on hand less reserved, safety stock and unavailable, never negative), capped by the aggregate on hand; otherwise the aggregate. Product cards, the `available` filter, product-page option availability and the bag's add check all use it, so the storefront never offers stock that checkout would refuse. Checkout itself remains authoritative and re-checks under lock. The query reads from `inventory` (keyed by variant) so each caller costs one probe per variant; the "in stock" filter uses a scalar subquery within a category or search, because PostgreSQL would flatten `EXISTS` into a semi-join that computes stock for every variant in the catalog.

## Search

`IProductSearch` (Application) is implemented by `PostgresProductSearch` (Infrastructure) as one composable SQL SELECT that EF joins into catalog queries, so filters, facets, category scope and paging are unchanged. A product matches when all query words appear in title, brand or description (`search_all`, English config), when title words start with what was typed (autocomplete, `he` finds headphones), when any word appears in the title or brand (`search_title`), when the title contains the text (3+ characters), or when the shopper's translated title matches (`product_translations.search_title`, simple config). Typo matching (word similarity >= 0.4 on the title, queries of 4+ characters) applies only when nothing matches exactly, which keeps "cable" from matching "Portable Speaker". Each way of matching is its own `UNION` branch so each uses its index (full-text GIN, trigram GIN); ranking runs only on the candidates, and the matcher is joined into each statement once. Typo matching uses the trigram index through `<%`, whose cut-off is the session setting `pg_trgm.word_similarity_threshold`, set on every connection from `PostgresProductSearch.TypoThreshold`. With a query and no explicit sort, results are ordered by relevance. The tsvector columns are generated by PostgreSQL and live outside the EF model.

## Detail filters

Within a category, `GET /api/catalog/products` returns `attributes`: facets built from the details products highlight (RAM, screen size, energy rating, book format). A label is offered when it covers at least 30% of the results and has 2 to 15 values, so author and page count never become filters; values sort by size with units (512 GB before 1 TB). Repeatable `attr=Label:Value` filters: values of one label are alternatives, different labels all apply, and a chosen label's counts ignore its own choice so its alternatives stay visible. Results are paged on their sort keys first and cards are projected for the page only.

## Product relationships

`product_relationships` links a source product to a target with a type (Accessory, Compatible, Complementary, Alternative, Upgrade, FrequentlyBoughtWith), a relevance score and a shopper-facing reason. The product endpoint returns them grouped in that order; `GET /api/catalog/products/addition?productIds=` returns at most one sellable setup item (accessory, compatible or complementary) not already in the given set, or 204. Relationships drive merchandising only and never affect catalog filtering.

## Catalog translations

`product_translations` (title, short description, description, SEO title and description) and `category_translations` (name) are keyed by entity and language. Every catalog read accepts `locale`; each field falls back to the canonical English record, and responses report the language actually served (`Locale`, `CategoryLocale`) so the storefront can mark text correctly. Cache keys include the language. Specifications, brands and relationship reasons are not translated yet.

## Bot protection and sitemaps

When `TURNSTILE_SECRET_KEY` is set, `POST /api/checkout/confirm` requires an `X-Turnstile-Token` that Cloudflare's siteverify accepts; otherwise it returns 403 `challenge_required` before any cart, stock or payment work. Verification runs server-side only (`CloudflareTurnstile`, 5-second timeout). If Cloudflare itself is unreachable or returns 5xx, checkout fails open and logs a warning: an outage at the challenge provider must not stop genuine orders, and the order path keeps its own rate limits and idempotency. Unset, the check is off.

`GET /api/catalog/sitemap/products?page=&pageSize=` pages active product slugs with their last update, 10,000 per page, for the storefront's sitemap index.

## Synthetic catalog and scale

`SYNTHETIC_CATALOG_PRODUCTS` (development only, default 0) loads a deterministic synthetic catalog once at startup: 25 departments and 109 subcategories with ru/ur/ar names, about 5,000 products per department. Each product family has its own attributes, and each kind of product pins the values and price range that make sense for it (earbuds are in-ear, a winter tyre is a winter tyre, an espresso machine is not $25). Books get authors, publishers and valid ISBN-13s. Rows are bulk-copied in one transaction (about 80 seconds for 125,000 products and 700,000 reviews); opening stock, balances and price records come from the same set-based step the seeder uses on every startup. To load: `SYNTHETIC_CATALOG_PRODUCTS=125000 docker compose -f infra/compose.yaml up -d api`.

Measured at 125,000 products (server time, warm):

| Request | Time |
|---|---|
| Department or subcategory page, any sort | 25–90 ms |
| Subcategory with detail filters | 40–90 ms |
| Category "in stock only" | 130–150 ms |
| Search, including typos (`lptop`) | 40–170 ms (two-letter queries matching thousands: ~220 ms) |
| Suggestions | 3–50 ms |
| Product page | 3–5 ms |
| Whole catalog, name or newest | ~300 ms |
| Whole catalog, rating or price sort | 0.5–1 s |
| Whole catalog, in stock only | ~1.5 s |

What made the difference, each confirmed with `EXPLAIN ANALYZE`: projecting card data only for the page being shown instead of every match before sorting; one index-served `UNION` branch per way of matching instead of `OR` across a join; the trigram index for typos instead of `word_similarity` on every title; scalar subqueries where PostgreSQL would otherwise hash all 125,000 variants; and `random_page_cost=1.1` (set in compose), since the default of 4 assumes spinning disks and steers the planner to whole-table hashes. Managed databases on SSD should use the same setting. The remaining whole-catalog sorts serve the homepage rails, which are cached; a stored rating summary or in-stock flag is the upgrade if unscoped listings become hot. No new indexes were needed: every plan uses existing ones (category/status/id, the GIN search columns, trigram title, variant/product, inventory and warehouse-stock keys). A covering reviews index was measured and rejected (680 to 500 ms on a path already behind a cache).

## PostgreSQL index plan

| Table | Indexes |
|---|---|
| categories | unique slug; parent/sort order |
| product_translations | product/locale primary key; GIN full-text title; trigram lower(title) |
| products | unique slug; category/status/id; trigram title and lower(title); GIN full-text (`search_title`, `search_all`) |
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

Indexes are query-driven and were checked against the 125,000-product catalog (see above). Listings use bounded offset pagination; count and page rows are separate read-committed statements and can shift under concurrent catalog writes. Partitioning is not warranted at this size: the whole database is about 500 MB and every scoped query stays on indexes. Further tuning should start from `EXPLAIN (ANALYZE, BUFFERS)` of the slow path.

## Language decision gates

Rust is not justified: there is no untrusted native parser, memory-safety boundary, or measured CPU workload that .NET libraries cannot handle. Go is not justified: HTTP/SSE fan-out is not a current requirement and ASP.NET Core supports the required asynchronous load. Neither service is created. Re-evaluate only with a measured, isolated workload and a stable disposable contract.

## Operations

Migrations are a deployment concern. `APPLY_MIGRATIONS=true` is intended only for the single-instance development container; production deployment jobs should run migrations before replicas start. Configure `AUTH_AUTHORITY`, `AUTH_AUDIENCE`, and `AUTH_ROLE_CLIENT_ID`; use `AUTH_METADATA_ADDRESS` only when the API needs a private discovery route while tokens retain the public issuer. Configure explicit frontend origins and trusted proxy IPs. `OTEL_EXPORTER_OTLP_ENDPOINT` exports ASP.NET Core traces, HTTP client traces, runtime metrics, and commerce-specific metrics including authentication failures and authorization denials. PostgreSQL alone controls API readiness; the protected `/health/identity` diagnostic separately checks discovery. See ADR 013 for identity and ADR 014 for operations authority and safety.
