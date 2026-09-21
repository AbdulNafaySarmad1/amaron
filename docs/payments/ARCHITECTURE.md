# Payment Architecture

Commerce owns orders, payments, attempts, refunds, webhook receipts, and reconciliation. `IPaymentProvider` keeps processor calls behind adapters; the only implemented adapter is the deterministic `test` provider. It is not an external payment integration.

Synchronous test checkout captures immediately and preserves the existing atomic order/inventory behavior. Asynchronous providers use payment attempts, verified webhooks, and capture/refund operations.
