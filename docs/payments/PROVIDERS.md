# Providers

`IPaymentProvider` covers payment intent creation, confirmation, capture, cancellation, refund, status lookup, and webhook verification. Provider capabilities expose authorization, manual/partial capture, partial refund, installments, saved methods, 3DS, and asynchronous payment support.

`TestPaymentProvider` supports deterministic authorization, pending, decline, customer-action, capture, refund, and signed webhook simulation. It is local/test-only architecture and has not been externally verified. Raast and local PSPs require an authorized bank or PSP adapter and are not fabricated here.
