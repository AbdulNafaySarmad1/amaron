# Amaron commerce

A Next.js 16 storefront and dedicated operations console backed by a .NET 10 modular monolith. PostgreSQL 18 is authoritative. Keycloak supplies OIDC identity and coarse roles; ASP.NET policies and application queries enforce commerce authorization. HybridCache fails open to PostgreSQL, while Redis is required for browser login transactions and sessions.

## Run

Prerequisites: Docker Desktop for the full stack and integration tests; .NET 10 SDK for host backend development; Node.js 24 for host storefront development.

```powershell
dotnet restore Commerce.slnx
dotnet build Commerce.slnx
dotnet test Commerce.slnx
docker compose -f infra/compose.yaml up --build
```

Compose starts the storefront at `http://localhost:3000`, operations console at `http://localhost:3001`, API at `http://localhost:8080`, and local Keycloak at `http://localhost:8081`. OpenAPI is available in Development at `/openapi/v1.json`; liveness is `/health/live`, PostgreSQL readiness is `/health/ready`, and the protected identity diagnostic is `/health/identity`.

The imported local realm includes `customer.dev`, `support.dev`, and `catalog.manager.dev`. Their passwords and client/admin secrets come from `KEYCLOAK_*` environment variables; Compose fallbacks are explicitly local-development credentials, not production secrets. `.env.example` values such as `change-me` are configuration markers and must be replaced outside local development.

For host-only frontend development instead of the Compose storefront:

```powershell
Copy-Item apps/storefront/.env.example apps/storefront/.env.local
npm --prefix apps/storefront ci
npm --prefix apps/storefront run dev
```

The storefront listens on `http://localhost:3000`. Its checks are `npm --prefix apps/storefront run lint`, `npm --prefix apps/storefront run typecheck`, `npm --prefix apps/storefront run test`, and `npm --prefix apps/storefront run build`. The .NET solution does not run these frontend checks.

The operations console listens on `http://localhost:3001`. Run the same checks with `apps/admin` as the npm prefix. It uses a separate `admin-web` OIDC client and exposes only an allowlisted BFF proxy to policy-protected operations endpoints.

The storefront uses a same-origin OIDC authorization-code-plus-PKCE BFF. Access and refresh tokens remain in Redis; the browser receives an opaque HttpOnly session cookie and sends a session CSRF token for mutations. The API validates bearer JWTs locally and maps `(iss, sub)` to an internal `ApplicationUser`. Development headers work only when both Development and `ALLOW_DEVELOPMENT_IDENTITY=true`; never expose them as production authentication.

Production still requires a hardened external OIDC/Keycloak deployment, TLS and a correct public `APP_URL`, managed secrets, durable and highly available Redis and databases, explicit `FRONTEND_ORIGINS` and `TRUSTED_PROXY_IPS`, backups and monitoring, and end-to-end identity/outage tests. The checked-in `start-dev` realm, local users, loopback topology, and fallback credentials are not production-ready. See ADR 013.

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
GET  /api/admin/products/{id}   Administration.Access + Catalog.Read
PUT  /api/admin/products/{id}   Administration.Access + Catalog.Manage; If-Match required
GET  /api/admin/inventory/{id}  Administration.Access + Inventory.Read
PUT  /api/admin/inventory/{id}  Administration.Access + Inventory.Manage; If-Match required
GET  /api/admin/operations/dashboard
GET  /api/admin/operations/variants
POST /api/admin/operations/pricing/schedules
POST /api/admin/operations/pricing/{id}/approve
GET  /api/admin/operations/demand
GET  /api/admin/operations/inventory
GET  /api/admin/operations/replenishment
GET  /api/admin/operations/promotions
GET  /api/admin/operations/approvals
GET  /api/admin/operations/audit
```

See `docs/` for boundaries, caching, resilience, security, and performance decisions.
