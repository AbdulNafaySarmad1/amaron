# PCI Scope Boundary

This architecture is designed to minimize PCI DSS scope; it does not claim PCI compliance. The application must receive provider tokens/references only, never PAN, CVV, track data, PIN, raw authentication secrets, provider secret keys, or webhook secrets in browser bundles. Provider-hosted fields/pages own card entry. Logs and metrics use payment IDs, provider, sanitized references, status, and normalized failure categories only.
