# Edge And Origin Security

Application security is edge-provider independent. ASP.NET only applies forwarded headers when `TRUSTED_PROXY_IPS` is configured and receives a request from that allowlist. The production `PUBLIC_HOSTS` setting is mandatory and rejects wildcard hosts outside development.

## Production Topology

Preferred: `Internet -> Cloudflare Tunnel -> private origin -> application`. A tunnel does not need public origin ingress. If public ingress is necessary, use one edge pattern: Cloudflare authenticated origin pulls/mTLS with strict firewall ingress, or Akamai Site Shield/origin ACL plus authenticated origin connectivity and strict TLS. Do not rely on provider CIDRs as authentication alone.

Cloudflare/Akamai WAF, bot controls, and edge rate limits are additive. `/auth`, `/checkout`, `/account`, and `/api/admin` require stricter edge rules. Public catalog/asset paths may be CDN cached; cart, order, account, checkout, and payment responses are no-store/private.

## Required Infrastructure Configuration

- Set `PUBLIC_HOSTS`, `TRUSTED_PROXY_IPS`, exact `FRONTEND_ORIGINS`, `AUTH_AUTHORITY`, database/cache credentials, and protected Data Protection key storage in production.
- Restrict database and Valkey to private networks; use runtime database roles rather than migration/superuser credentials.
- Terminate TLS with strict origin validation, disable public origin DNS where a tunnel is used, and configure ingress/firewall policy.
- Do not forward client-supplied `X-Forwarded-*`, `CF-Connecting-IP`, or `True-Client-IP` through ingress unnormalized.
