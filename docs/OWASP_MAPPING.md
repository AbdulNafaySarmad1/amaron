# Security Control Matrix

This is an implementation and verification tracker, not a compliance assertion.

| Baseline | Implementation | Location | Validation | Status |
| --- | --- | --- | --- | --- |
| OWASP A01/API1 BOLA/ASVS V4 | ownership-aware orders, admin policies | `CheckoutService`, `CommerceAuthorization` | integration ownership tests | Implemented/tested |
| OWASP A02/API8/ASVS V14 | host allowlist, exact CORS, headers, trusted proxies | `Program.cs`, Next configs | build; configuration review | Implemented; production config required |
| OWASP A03/ASVS V10 | lockfiles, pinned base images; vulnerability audit | package locks, Dockerfiles | `npm audit` | Partial: CI/SBOM scanner required |
| OWASP A04/ASVS V6 | TLS by deployment, platform crypto, no card data | auth/payment docs | code review | Partial: infrastructure required |
| OWASP A05/API3/ASVS V5 | explicit request DTOs and server commercial truth | contracts/services | integration checkout tests | Implemented/tested |
| OWASP A06/API4/ASVS V13 | body limits, timeouts, pagination bounds, rate policies | `Program.cs`, services | integration tests | Implemented/tested baseline |
| OWASP A07/API2/ASVS V2 | Keycloak JWT validation, server session/BFF and CSRF | identity, Next auth modules | auth/security tests | Implemented/tested |
| OWASP A08/ASVS V11 | webhook HMAC, idempotency, migrations | `Payments.cs` | integration baseline; provider contract unverified | Partial |
| OWASP A09/ASVS V7 | JSON logs, trace IDs, audit entries, OTEL | `Program.cs`, services | runtime inspection | Implemented; alert routing required |
| OWASP A10/API10/ASVS V8 | controlled ProblemDetails, timeouts, fail-closed checks | `Program.cs` | integration malformed/error tests | Implemented/tested baseline |
| API7 SSRF | only configured identity/API upstreams, bounded HttpClient | API/Next auth clients | code review | Implemented; future URL fetches need dedicated validation |
