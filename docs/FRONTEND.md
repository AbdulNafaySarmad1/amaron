# Frontend architecture

## Boundaries

The Next.js App Router uses Server Components for public route data acquisition and client components for cart state, checkout forms, autocomplete, and interaction effects. Public server reads use container-internal `API_URL`. Private browser calls use same-origin `/api/bff/*` route handlers; no API URL, customer ID, or bearer token is exposed as public browser configuration. The root client provider does not convert its server-rendered children into client components.

Public commerce routes are request-rendered to keep image builds independent of API availability. API and HybridCache policies provide the active public caching layer. Search is request-dependent. Private shopper calls are browser-only and `no-store` at both BFF and API.

## Language, region and currency

Every page lives under `/{locale}` (`en`, `ru`, `ur`, `ar`) in `app/[lang]`; API routes stay at `/api`. `src/proxy.ts` redirects a URL without a locale using, in order: the account preference (the OIDC `locale` claim, mirrored into `amaron-account-locale` at sign-in), the `amaron-locale` cookie, `Accept-Language`, then English. A locale in the URL is always honoured. Language is never derived from location.

Region (`amaron-region`, ship-to country and currency) is separate. Without a saved region, Cloudflare's `CF-IPCountry` suggests one on first visit; it never binds the shopper. Only currencies the pricing service can charge are selectable (USD today), so no price is shown that checkout cannot honour.

UI strings live in `src/i18n/dictionaries/*.json`; a missing translation falls back to English, and a unit test fails if a translation drifts from the English keys or placeholders. Client components read strings with `useT()` and link with the locale-aware `Link` and `useLocalizedRouter()` from `locale-provider`. `ur` and `ar` render `dir="rtl"`; layout CSS uses logical properties only. Script faces (Noto Serif for Cyrillic headings, Noto Naskh Arabic, Noto Nastaliq Urdu) are not preloaded and download only for pages that use them; see the font-stack note in `globals.css` before reordering families.

Content without a translation is marked as English so it is laid out left-to-right and read correctly by screen readers: whole pages via `<Untranslated>` (remove it once a page's copy is in the dictionaries), and catalog names via `lang={CATALOG_LANG} dir="auto"`. Arabic-script CSS rules use `x:lang(ur)`, never `:lang(ur) x`, so they do not reach nested English content.

## Category spaces and product presentation

Each category has its own page at `/{locale}/c/{slug}`; results always come from the category's subtree (enforced by the API, not the page). Old `/search?category=` links redirect there. On a category page the header search, search mode and suggestions are scoped to that category by default; a scoped search with no results says so and offers "Search all products" rather than widening silently. Brand counts come from the API as a facet computed before the brand filter is applied.

Products carry a `kind` (presentation only, e.g. `book` renders a cover) and ordered attributes. Attributes flagged as highlights (at most three) summarise the product on cards; the full list is the product's specifications. Nothing in React branches on a specific product type beyond choosing that presentation.

Counted strings ("3 items", Russian "2 товара", Arabic dual forms) are plural-form objects in the dictionaries and are rendered with `plural()`, which uses `Intl.PluralRules`; never build them with `format()`.

## Product page, checkout and confirmation

The product page's first viewport holds only what a purchase decision needs: name, one-line proposition, rating, price and saving, options, availability, Add to bag / Save, and the store's delivery, returns and support promises. Details, specifications and delivery follow below; further products are labelled "More in {category}" because they are same-category, not relationship-based. Checkout swaps the header for a minimal one (wordmark, "Secure checkout", back to store) and hides the bottom nav: no search, categories or recommendations compete with the order. The confirmation leads with reassurance and next steps and shows no products for sale.

## Guests and account-only actions

Browsing, search and Saved work without an account. Account-only actions (the bag today, reviews later) call `useRequireSignIn()` from `sign-in-gate.tsx`; for guests it opens a prompt explaining why, with sign-in returning to the same page and query. Sign-out returns to the homepage in the language the shopper was using.

## Identity BFF

`/api/auth/login` starts OIDC authorization code flow with S256 PKCE, state, nonce, a ten-minute Redis transaction, and a sanitized local `returnTo`. `/api/auth/callback` validates that transaction and creates an eight-hour-by-default Redis session. Production cookies use `__Host-` names and are Secure, HttpOnly, SameSite=Lax, and path `/`; their values are random session IDs, not tokens. `/api/auth/session` exposes only authentication state, display name, and the session CSRF token. Logout deletes the session before returning the provider end-session URL.

The BFF refreshes access tokens server-side within a 30-second expiry window and forwards only `Accept`, JSON content type, `Authorization`, and, when supplied, `Idempotency-Key`. Its route/method allowlist covers cart item reads/writes, checkout confirmation, and order reads; it is not a general API proxy. Mutations and logout require exact canonical Origin and CSRF-token checks. `APP_URL` is required in production and defines that canonical origin; OIDC issuer and optional internal metadata URL remain server-only.

The dedicated `apps/admin` application uses the same server-side OIDC and Redis session pattern at `http://localhost:3001`, with its own client, cookie namespace, CSRF token, and separate explicit `/api/admin/operations/**` and `/api/admin/supply-chain/**` method/path allowlists. It never exposes API bearer tokens to browser JavaScript. The admin interface provides grouped operational metrics, sortable/exportable tables, pricing bulk preview before apply, forecast and replenishment actions, supplier/source/PO views, inbound and invoice views, disposition balances, a physical receiving workbench, approval queues, alerts, and audit views. Receipt entry calculates physical and missing quantities before sending an idempotent command, but the API remains authoritative for identifiers, state transitions, quantities, permissions, and all business invariants.

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
