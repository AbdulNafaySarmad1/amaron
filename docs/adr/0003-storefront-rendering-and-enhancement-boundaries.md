# Storefront rendering and enhancement boundaries

Status: Accepted

## Context

The storefront needs crawlable, fast public content and rich interaction without making shopping depend on hydration, animation, WebGL, or a frontend build-time API connection.

## Decision

Next.js Server Components fetch public catalog data through the server-only `API_URL`. Browser components call `NEXT_PUBLIC_API_URL` for private cart, checkout, orders, and autocomplete. Public commerce routes are request-rendered so the frontend image can build independently of the API; backend/HybridCache and HTTP cache headers provide public read acceleration.

Client boundaries are used where browser state or animation is required. Primary content is emitted visible in server HTML. Product-grid links disable automatic prefetch because dense result pages otherwise create speculative load; top-level navigation keeps normal prefetch behavior.

Motion handles local UI transitions. GSAP is limited to one editorial scroll sequence. Three.js is limited to the hero, dynamically loaded near the viewport, disabled for reduced motion and Save-Data, paused offscreen, and layered over a static fallback.

## Alternatives Considered

- Static generation or ISR for public routes.
- A fully client-rendered storefront.
- Server-rendering private shopper state.
- Loading all animation and WebGL code eagerly.
- Prefetching every product detail route in grids.

## Why This Decision

Independent container builds and graceful deployment are more valuable here than full-route caching. Route-shaped backend projections keep request rendering bounded. Progressive enhancement retains the intended visual language without making core commerce depend on GPU or animation support.

## Benefits

- Frontend builds do not require a live API.
- Public HTML remains meaningful before or without hydration.
- WebGL and GSAP failures leave static content usable.
- Dense search pages avoid uncontrolled speculative backend traffic.

## Costs And Trade-offs

- Request rendering creates more Next.js server work than ISR.
- Some static content lives inside client component trees and hydrates.
- Product detail navigation may begin slightly later because grid prefetching is disabled.
- Decorative WebGL still consumes GPU while visible on capable clients.

## Consistency Guarantees

- Public pages may be stale according to backend cache and HTTP TTLs.
- Private shopper state is loaded from the API and is never inferred from server-rendered public data.
- Core browsing, cart, and checkout controls do not require WebGL or GSAP.

## Failure And Degradation Behavior

Category navigation failure leaves the All goods route available. Page-level public fetch failure reaches the Next error boundary. Browser requests have explicit deadlines and are not automatically retried. Rich enhancement import or capability failure retains static visuals and content.

## Reconsider When

- Next server CPU or API request volume shows request rendering is material.
- Deployment can guarantee API availability during frontend builds.
- Product-grid prefetch improves measured conversion without harming backend headroom.
- Hydration cost breaches the frontend budget.
- Rich media causes sustained frame, memory, or battery regressions.
