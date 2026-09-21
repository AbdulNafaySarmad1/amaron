# Payment Failure Modes

Failures are normalized as declined, authentication required, insufficient funds, invalid method, processor unavailable, network error, risk rejected, limits, cancellation, or unknown. Checkout retries use persisted idempotency keys. Provider outages must return a safe unavailable/pending result, not a paid order. Customers retry explicitly rather than silently failing over to another processor.
