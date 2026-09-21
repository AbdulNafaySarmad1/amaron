# External identity provider boundary

Status: Accepted

## Context

The storefront needs browser sign-in without exposing OAuth tokens to browser JavaScript, and the API needs stable identities and coarse permissions without making commerce authorization provider-specific. Keycloak is the selected reference and local-development identity provider, but the application should remain portable to a conforming OIDC provider. Identity-provider roles cannot safely express object ownership, order visibility, inventory invariants, or other business rules.

## Decision

Use a same-origin Next.js BFF with OIDC authorization code flow, S256 PKCE, state, and nonce. The BFF stores access, refresh, and ID tokens in Redis under a random opaque session identifier; the browser receives only a `Secure` production `__Host-` HttpOnly SameSite=Lax cookie. State-changing BFF requests require both an exact Origin match to `APP_URL` and a session-bound CSRF token. The proxy exposes only an explicit private-commerce route and method allowlist.

The ASP.NET API validates signed access JWTs locally from OIDC discovery/JWKS, including issuer, audience, lifetime, signature, token type, and `sub`. Keycloak client roles are normalized into application permission claims and consumed through named ASP.NET policies. Keycloak owns authentication, account lifecycle, groups, and coarse role assignment. ASP.NET and the application layer remain authoritative for endpoint permissions, object ownership, and business invariants. In particular, orders require own-order access by default; `orders-view-any` permits the service query to cross that ownership boundary, while a non-owner otherwise receives not found.

Application users are created on first private API use and keyed uniquely by `(issuer, sub)`; commerce records use the resulting internal user ID. Provider display name and email are profile data, not identifiers. Keycloak Authorization Services is intentionally disabled: named policies and application queries keep resource and business authorization near the protected data. No machine-to-machine service identity is currently required or configured; all imported clients have service accounts disabled. Add a dedicated least-privilege client-credentials identity only when a real service caller exists.

Depend only on standard OIDC discovery, authorization, token, end-session, issuer, audience, and JWKS behavior plus configurable role-claim mapping. Keycloak is the selected provider, not an authorization dependency embedded in domain code, so another conforming OIDC provider can replace it with configuration and claim-mapping changes.

## Alternatives

- ASP.NET Core Identity: rejected because this application needs external identity separation, federation readiness, centralized groups, and standards-based service identities rather than an application-owned credential store.
- Managed identity providers such as Auth0, Okta, Entra, or Cognito: viable OIDC alternatives, but not selected for the current self-hosting and portability requirements.
- A custom JWT or OAuth implementation: rejected because authentication protocols and cryptography must use maintained standards implementations rather than application code.
- Browser-held bearer tokens: rejected for the storefront because it exposes tokens to browser JavaScript and spreads refresh and token handling across clients.
- Provider-managed fine-grained authorization or Keycloak Authorization Services: rejected because order ownership and commerce rules require current application data and transactional enforcement.
- Provider subject alone as the commerce key: rejected because `sub` is scoped by issuer and may collide across providers or realms.
- Anonymous durable customer headers or production use of development headers: rejected because browser-supplied identity is not authentication.
- A provider-specific adapter throughout the domain: rejected to retain OIDC portability.

## Benefits

- OAuth tokens remain server-side, while the browser uses an opaque, revocable session handle.
- Provider compromise boundaries and application authorization responsibilities are explicit.
- Named policies make coarse permission checks auditable; object and business checks remain close to PostgreSQL data.
- `(issuer, sub)` preserves stable local identity across profile changes and supports multiple issuers without collisions.
- Local JWT validation avoids per-request identity-provider calls and supports ordinary JWKS key rotation.

## Costs

- Keycloak is another stateful Tier-0 service with a JVM/runtime footprint, database, backups, patching, upgrades, configuration complexity, and an additional failure domain.
- Redis becomes required for login transactions and storefront sessions; it is no longer only a cache for private browser flows.
- The BFF must securely operate refresh, logout, CSRF, canonical URL, cookie, and proxy allowlist behavior.
- Role and claim contracts must be coordinated with the provider, and provider role changes take effect through newly issued or refreshed tokens.
- Identity-provider outages still block login and refresh; unknown signing keys cannot be fetched during an outage.
- The local Keycloak realm and automated tests do not by themselves prove production hardening, HA, backup, recovery, revocation latency, or end-to-end interoperability.

## Trigger For Reconsideration

Reconsider Keycloak if its operational burden exceeds its benefit, managed identity becomes preferable, organizational requirements change, or federation/compliance requirements materially change. Also reconsider the surrounding topology when native or third-party clients require direct API access, multiple issuers require issuer-specific claim adapters, a real service-to-service caller needs credentials, immediate revocation becomes mandatory, Redis-backed sessions cannot meet availability goals, or authorization rules move outside commerce data and can be justified in an external policy system.
