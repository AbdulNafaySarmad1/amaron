# Frontend architecture

## Boundaries

The Next.js App Router uses Server Components for public route data acquisition and client components for cart state, checkout forms, autocomplete, and interaction effects. Public server reads use container-internal `API_URL`. Private browser calls use same-origin `/api/bff/*` route handlers; no API URL, customer ID, or bearer token is exposed as public browser configuration. The root client provider does not convert its server-rendered children into client components.

Public commerce routes are request-rendered to keep image builds independent of API availability. API and HybridCache policies provide the active public caching layer. Search is request-dependent. Private shopper calls are browser-only and `no-store` at both BFF and API.

## Identity BFF

`/api/auth/login` starts OIDC authorization code flow with S256 PKCE, state, nonce, a ten-minute Redis transaction, and a sanitized local `returnTo`. `/api/auth/callback` validates that transaction and creates an eight-hour-by-default Redis session. Production cookies use `__Host-` names and are Secure, HttpOnly, SameSite=Lax, and path `/`; their values are random session IDs, not tokens. `/api/auth/session` exposes only authentication state, display name, and the session CSRF token. Logout deletes the session before returning the provider end-session URL.

The BFF refreshes access tokens server-side within a 30-second expiry window and forwards only `Accept`, JSON content type, `Authorization`, and, when supplied, `Idempotency-Key`. Its route/method allowlist covers cart item reads/writes, checkout confirmation, and order reads; it is not a general API proxy. Mutations and logout require exact canonical Origin and CSRF-token checks. `APP_URL` is required in production and defines that canonical origin; OIDC issuer and optional internal metadata URL remain server-only.

The dedicated `apps/admin` application uses the same server-side OIDC and Redis session pattern at `http://localhost:3001`, with its own client, cookie namespace, CSRF token, and explicit `/api/admin/operations/**` method/path allowlist. It never exposes API bearer tokens to browser JavaScript. The admin interface provides grouped operational metrics, sortable/exportable tables, pricing bulk preview before apply, forecast and replenishment actions, approval queues, alerts, and audit views. The API still enforces every granular permission and business invariant.

## State and consistency

Zustand stores the last server-confirmed cart. Cart writes are pessimistic and serialized in the browser because the API accepts absolute quantities. A request generation prevents a stale GET from overwriting a newer mutation. `idle`, `loading`, `ready`, and `error` are distinct; only a successful empty response renders the empty-cart state.

Checkout persists an idempotency key in session storage, bound to cart version and a SHA-256 request fingerprint; address data is not stored there. Network and 5xx outcomes are treated as ambiguous; retrying unchanged details reuses the key. Confirmed success clears the key. The API remains authoritative for price and inventory.

## Latency budgets

| Operation | Frontend deadline | API deadline |
|---|---:|---:|
| public server read | 8 sec | catalog 5 sec; search 8 sec |
| autocomplete | 10 sec caller default; API rejects after 2 sec | 2 sec |
| cart/order browser call | 10 sec | read 8 sec; write 10 sec |
| checkout | 22 sec | 20 sec |

Frontend code does not automatically retry requests. GET retry policy can be added only after measuring transient failure rates and respecting `Retry-After`. Mutations must not be blindly retried; checkout recovery uses idempotency.

## Progressive enhancement

Primary HTML is visible before hydration. Motion is used for local state changes. GSAP owns one scroll sequence and fails back to static content. The hero WebGL scene is decorative, dynamically imported, gated by viewport, reduced-motion, WebGL support, and Save-Data, paused offscreen, and backed by a CSS visual.

Dense product grids disable automatic product-route prefetching to protect browser and backend budgets. Reconsider using intent-based prefetch only with measurements.

## Failure domains

- API or PostgreSQL: public route error boundary; private controls show local retry/error states.
- Keycloak/OIDC: new login fails; existing sessions continue only while Redis is available and their access token remains usable. Refresh failure deletes the session and produces 401.
- Redis session store: authentication and BFF session reads fail. Because the root layout reads the session, the current implementation can also fail public page rendering; this is not the API cache's fail-open behavior.
- Category navigation: degrades to the permanent All goods route.
- Valkey as API cache: transparent API fallback to PostgreSQL.
- JavaScript/hydration: server-rendered public content remains visible; private mutations require JavaScript.
- WebGL/GSAP: static content remains usable.
- OIDC configuration: production requires HTTPS, a fixed `APP_URL`, managed client credentials, durable Redis, and provider redirect/logout URIs matching the public origin.

Frontend unit tests cover CSRF and Origin helpers, safe return paths, the refresh window, and BFF route allowlisting. They do not run a browser against Keycloak or Redis and do not cover callback, refresh, logout, revocation, or outage flows end to end.
