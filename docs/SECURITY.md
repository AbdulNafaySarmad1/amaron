# Security

Production requires an external authentication authority. Development header authentication is disabled unless `ALLOW_DEVELOPMENT_IDENTITY=true` in Development. CORS uses the configured frontend origin. Cart, checkout, and order responses are `no-store`; public output caching is limited to explicitly public endpoints. Admin reads currently rely on authenticated transport and are not output-cached.

The API applies HSTS in production, CSP, frame denial, MIME sniffing protection, a request body limit, RFC 7807 errors, server-side ownership checks, endpoint-specific rate limits, and bounded validation. Browser-supplied prices, totals, inventory, titles, and customer IDs are ignored. Secrets come from environment configuration. Logs exclude tokens, passwords, addresses, cache values, and payment data. This assignment does not collect card data.

Administrative writes require an `admin` role or `catalog.write` scope and an `If-Match` resource version. The `X-Admin` and `X-Customer-Id` headers are accepted only when both the Development environment and `ALLOW_DEVELOPMENT_IDENTITY=true` are set. Compose binds this development API to loopback. These headers are not production authentication mechanisms. Set `TRUSTED_PROXY_IPS` to an explicit comma-separated allowlist before accepting forwarded client addresses.

The current storefront does not implement production login, token acquisition, or bearer propagation. `NEXT_PUBLIC_DEMO_CUSTOMER_ID` is a Development-only demonstration identity and is sent only when explicitly configured. Public catalog deployment is possible without it; production cart, checkout, and orders require ADR 0004 to be resolved.

Checkout persists recipient and shipping-address fields in PostgreSQL. A production deployment must define access, retention, deletion, and data-subject handling before collecting real customer information.
