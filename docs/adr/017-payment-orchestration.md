# ADR 017: Provider-Neutral Payment Orchestration

## Decision

Commerce owns provider-independent payment, attempt, refund, webhook, and reconciliation state. Processors are infrastructure adapters behind `IPaymentProvider`.

## Alternatives

Direct processor calls throughout checkout, a hardcoded PSP, and custom card processing were rejected.

## Consequences

This adds persistence and adapter maintenance but improves portability, auditability, testability, and multi-provider readiness. Reconsider a dedicated orchestration platform only when provider, regional, or transaction scale justifies it.
