# Production shopper identity

Status: Superseded by ADR 013

## Context

At the time of this decision, the API could validate JWT bearer identity but the storefront did not acquire or propagate production credentials. `X-Customer-Id` was limited to explicitly enabled Development.

## Decision

This ADR recorded the requirement to choose a production identity topology rather than ship a shared browser-supplied identity. ADR 013 made that choice and implemented a same-origin OIDC BFF with Keycloak.

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

- Private storefront flows required Development configuration until ADR 013.
- Production launch was blocked on an identity decision.

## Consistency Guarantees

The superseding design maps validated `(issuer, sub)` to an application-owned identifier. Browser-supplied prices, totals, inventory, and customer IDs remain untrusted.

## Failure And Degradation Behavior

Without a valid production credential, private endpoints return 401; the storefront does not fall back to a shared customer identity. Public catalog browsing remains available.

## Reconsider When

- A production identity provider and deployment origin are selected.
- Server-rendered private pages are required.
- Anonymous carts must survive across devices or sessions.
- Multiple storefront origins or native clients consume the API.
