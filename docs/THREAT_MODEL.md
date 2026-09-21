# Threat Model

## Scope and Assets

Assets are user accounts and sessions, addresses, orders, catalog/pricing/inventory truth, payment references, API and infrastructure credentials, and immutable operations audit records. PostgreSQL is transactional authority; Valkey is cache only.

## Trust Boundaries

`Internet -> CDN/WAF -> restricted origin/ingress -> Next.js -> ASP.NET API -> PostgreSQL` and `ASP.NET API -> private Valkey` are separate boundaries. Browser input, authenticated users, CDN headers, cache values, provider webhooks, and external API responses are untrusted until validated at their receiving boundary.

## STRIDE Summary

| Threat | Primary mitigations | Status |
| --- | --- | --- |
| Spoofed identity/session | Keycloak JWT validation, HttpOnly server sessions, CSRF and origin checks | Implemented/tested in existing auth tests |
| BOLA/admin misuse | Resource ownership checks, explicit policies, audit entries | Implemented/test coverage exists; review every new route |
| Pricing/inventory/order tampering | Server-side cart reconstruction, locks, idempotency, ledger | Implemented/integration tested |
| Webhook replay/tampering | HMAC verification, event uniqueness, transition guards | Implemented; test-provider only |
| Injection/XSS | DTO binding, EF parameterization, bounded inputs, React escaping, CSP | Implemented baseline; no arbitrary HTML rendering found |
| DoS/resource abuse | request size, timeouts, endpoint rate limits, bounded pagination | Implemented; edge capacity is infrastructure-owned |
| CDN/origin bypass | application authorization/rate limits do not trust edge headers; restricted origin required | Infrastructure configuration required |
| Secret/dependency compromise | environment configuration, lockfiles, non-root images, audit/vulnerability checks | Partial; CI secret/SBOM scanner required |

## Failure Behavior

Authentication, authorization, signature validation, checkout validation, and admin actions fail closed. Cache, recommendations, and optional visual assets fail gracefully; cache failure never grants access.
