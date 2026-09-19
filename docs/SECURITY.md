# Security

Production requires an external authentication authority or a configured trusted proxy identity integration. Development header authentication is disabled unless `AUTH_ENABLED=false` in Development. CORS uses the configured frontend origin. Private responses are `no-store`; public output caching is limited to explicitly public endpoints.

The API applies HSTS in production, CSP, frame denial, MIME sniffing protection, a request body limit, RFC 7807 errors, server-side ownership checks, endpoint-specific rate limits, and bounded validation. Browser-supplied prices, totals, inventory, titles, and customer IDs are ignored. Secrets come from environment configuration. Logs exclude tokens, passwords, addresses, cache values, and payment data. This assignment does not collect card data.
