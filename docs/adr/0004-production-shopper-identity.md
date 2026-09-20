# Production shopper identity

Status: Proposed

## Context

The API validates JWT bearer identity in production and accepts `X-Customer-Id` only in explicitly enabled Development. The current storefront is a development demonstration and does not acquire or propagate production credentials.

## Decision

Before production use, choose and implement one identity topology: direct browser-to-API bearer tokens or a same-origin Next.js backend-for-frontend session. Until then, production private cart, checkout, and order flows are not claimed as deployable. The storefront sends the development identity header only when `NEXT_PUBLIC_DEMO_CUSTOMER_ID` is explicitly configured.

## Alternatives Considered

- Direct OAuth/OIDC bearer tokens from the browser.
- Same-origin encrypted session cookies with Next route handlers proxying private API calls.
- Anonymous durable cart tokens.
- Continuing to expose a public customer-ID header.

## Why This Decision

Token storage, refresh, CSRF, SSR identity propagation, and deployment origin are product/security decisions not established by this repository. Making the gap explicit is safer than embedding a placeholder authentication mechanism.

## Benefits

- Development identity cannot silently masquerade as production authentication.
- The eventual implementation can match the deployment and identity provider.
- API ownership checks remain the final authorization boundary.

## Costs And Trade-offs

- Private storefront flows require Development configuration today.
- Production launch remains blocked on an identity decision.

## Consistency Guarantees

Customer identity comes from validated JWT `sub` in production and is bounded to the persisted 200-character key. Browser-supplied prices, totals, inventory, and customer IDs remain untrusted.

## Failure And Degradation Behavior

Without a production credential, private endpoints return 401; the storefront must not fall back to a shared customer identity. Public catalog browsing remains available.

## Reconsider When

- A production identity provider and deployment origin are selected.
- Server-rendered private pages are required.
- Anonymous carts must survive across devices or sessions.
- Multiple storefront origins or native clients consume the API.
