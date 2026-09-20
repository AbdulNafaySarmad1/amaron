# Amaron commerce

A Next.js 16 storefront backed by a .NET 10 modular monolith. PostgreSQL 18 is authoritative; HybridCache uses process memory plus optional Valkey/Redis acceleration and fails open to PostgreSQL.

## Run

Prerequisites: Docker Desktop for the full stack and integration tests; .NET 10 SDK for host backend development; Node.js 24 for host storefront development.

```powershell
dotnet restore Commerce.slnx
dotnet build Commerce.slnx
dotnet test Commerce.slnx
docker compose -f infra/compose.yaml up --build
```

Compose starts the storefront at `http://localhost:3000` and the API at `http://localhost:8080`. OpenAPI is available at `/openapi/v1.json`, liveness at `/health/live`, and readiness at `/health/ready`.

For host-only frontend development instead of the Compose storefront:

```powershell
Copy-Item apps/storefront/.env.example apps/storefront/.env.local
npm --prefix apps/storefront ci
npm --prefix apps/storefront run dev
```

The storefront listens on `http://localhost:3000`. Its production checks are `npm --prefix apps/storefront run lint`, `npm --prefix apps/storefront run typecheck`, and `npm --prefix apps/storefront run build`. The .NET solution does not run these frontend checks.

Development private endpoints accept `X-Customer-Id`. Production does not; configure `AUTH_AUTHORITY` and `AUTH_AUDIENCE` for JWT bearer validation. Never expose the development identity mechanism as production authentication.

The checked-in storefront has no production login/token flow. Private shopper features are Development-only until the identity topology in `docs/adr/0004-production-shopper-identity.md` is resolved.

## Primary routes

```text
GET  /api/storefront/home
GET  /api/storefront/products/{slug}
GET  /api/storefront/cart-summary
GET  /api/catalog/categories
GET  /api/catalog/products
GET  /api/catalog/products/{slug}
POST /api/catalog/products/batch
GET  /api/search/suggestions?q=...
GET  /api/cart
PUT  /api/cart/items
DELETE /api/cart/items/{variantId}
DELETE /api/cart
POST /api/checkout/confirm       Idempotency-Key required
GET  /api/orders
GET  /api/orders/{id}
GET  /api/admin/products/{id}   admin or catalog.write
PUT  /api/admin/products/{id}   If-Match required
GET  /api/admin/inventory/{id}  admin or catalog.write
PUT  /api/admin/inventory/{id}  If-Match required
```

See `docs/` for boundaries, caching, resilience, security, and performance decisions.
