import type { NextConfig } from "next";

// Third-party origins are allowed only when their integration is configured; unconfigured, the policy stays strict.
const gtm = !!process.env.NEXT_PUBLIC_GTM_ID;
const turnstile = !!process.env.NEXT_PUBLIC_TURNSTILE_SITE_KEY;
const contentSecurityPolicy = [
  "default-src 'self'",
  ["script-src 'self' 'unsafe-inline'", gtm && "https://www.googletagmanager.com", turnstile && "https://challenges.cloudflare.com"].filter(Boolean).join(" "),
  "style-src 'self' 'unsafe-inline'",
  "img-src 'self' data: https:",
  "media-src 'self' https:",
  ["connect-src 'self'", gtm && "https://www.googletagmanager.com https://*.google-analytics.com https://*.analytics.google.com"].filter(Boolean).join(" "),
  "font-src 'self' data:",
  turnstile ? "frame-src https://challenges.cloudflare.com" : "frame-src 'none'",
  "object-src 'none'",
  "base-uri 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join("; ");

const nextConfig: NextConfig = {
  reactStrictMode: true,
  poweredByHeader: false,
  output: "standalone",
  experimental: {
    optimizePackageImports: ["motion"],
    globalNotFound: true,
  },
  async headers() {
    return [{ source: "/(.*)", headers: [
      { key: "Content-Security-Policy", value: contentSecurityPolicy },
      { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
      // Browsers honour HSTS only over HTTPS, so this is inert in local HTTP development. No includeSubDomains/preload:
      // those bind sibling services on the domain and are the operator's decision.
      { key: "Strict-Transport-Security", value: "max-age=31536000" },
      { key: "X-Content-Type-Options", value: "nosniff" },
      { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(), payment=()" },
      { key: "Cross-Origin-Opener-Policy", value: "same-origin" }
    ] }];
  },
};

export default nextConfig;
