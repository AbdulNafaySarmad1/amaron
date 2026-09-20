# Frontend architecture

## Boundaries

The Next.js App Router uses Server Components for route data acquisition and client components for cart state, checkout forms, autocomplete, and interaction effects. Public server reads use container-internal `API_URL`; browser reads use `NEXT_PUBLIC_API_URL`. The root client provider does not convert its server-rendered children into client components.

Public commerce routes are request-rendered to keep image builds independent of API availability. API and HybridCache policies provide the active public caching layer. Search is request-dependent. Private shopper calls are browser-only and `no-store` at the API.

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
- Category navigation: degrades to the permanent All goods route.
- Valkey: transparent API fallback to PostgreSQL.
- JavaScript/hydration: server-rendered public content remains visible; private mutations require JavaScript.
- WebGL/GSAP: static content remains usable.
- Production identity: public browsing works, but private flows require the decision in ADR 0004.
