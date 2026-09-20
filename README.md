# Commerce backend

A .NET 10 modular-monolith backend for an interaction-heavy ecommerce storefront. PostgreSQL 18 is authoritative; HybridCache uses process memory plus optional Valkey/Redis acceleration and fails open to PostgreSQL.

## Run

```powershell
dotnet restore Commerce.slnx
dotnet build Commerce.slnx
dotnet test Commerce.slnx
docker compose -f infra/compose.yaml up --build
```

The API listens on `http://localhost:8080`. OpenAPI is available at `/openapi/v1.json`, liveness at `/health/live`, and readiness at `/health/ready`.

Development private endpoints accept `X-Customer-Id`. Production does not; configure `AUTH_AUTHORITY` and `AUTH_AUDIENCE` for JWT bearer validation. Never expose the development identity mechanism as production authentication.

## Primary routes

```text
GET  /api/storefront/home
GET  /api/storefront/products/{slug}
GET  /api/storefront/cart-summary
GET  /api/catalog/products
POST /api/catalog/products/batch
GET  /api/search/suggestions?q=...
GET  /api/cart
PUT  /api/cart/items
DELETE /api/cart/items/{variantId}
POST /api/checkout/confirm       Idempotency-Key required
GET  /api/orders
GET  /api/orders/{id}
GET  /api/admin/products/{id}   admin or catalog.write
PUT  /api/admin/products/{id}   If-Match required
GET  /api/admin/inventory/{id}  admin or catalog.write
PUT  /api/admin/inventory/{id}  If-Match required
```

See `docs/` for boundaries, caching, resilience, security, and performance decisions.
