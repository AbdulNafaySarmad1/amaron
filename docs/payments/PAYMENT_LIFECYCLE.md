# Payment Lifecycle

Payment state is independent of order state. Supported transitions are created/pending/customer-action to authorized or captured, then partially refunded/refunded. Terminal states do not regress on delayed webhooks. Failed attempts remain historical records.

Provider callbacks are authoritative only after signature verification. Browser redirects and client state are not payment proof.
