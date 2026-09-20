# Checkout consistency and idempotency

Status: Accepted

## Context

Checkout changes inventory, creates an immutable order snapshot, clears shopper intent, and records replay state. A lost response can make commit outcome ambiguous, and concurrent requests can target the same cart or inventory.

## Decision

PostgreSQL is the checkout consistency boundary. Checkout starts one transaction, locks the shopper cart, reads cart items only after that lock, locks inventory rows in variant-ID order, revalidates product state, price, currency, and stock, then writes the order, inventory decrement, cart clear, and idempotency record before one commit.

Cart mutations use the same cart lock inside a short transaction. Checkout takes the cart lock before inventory locks. Automatic transaction retries are disabled. An idempotency key is scoped to customer identity; the same key and payload replays the original order, while a changed payload is rejected.

## Alternatives Considered

- Inventory reservation when items enter the cart.
- Serializable isolation for every commerce write.
- Distributed locks outside PostgreSQL.
- Automatic EF execution-strategy retries.
- A message-driven checkout saga.

## Why This Decision

This application is a modular monolith with one authoritative database and no payment or fulfillment side effects. Database locks and one transaction provide the required correctness with fewer failure modes than distributed coordination.

## Benefits

- A cart cannot be consumed twice by concurrent checkout keys.
- Oversell is prevented by authoritative inventory locks.
- Order snapshots and inventory changes cannot partially commit.
- Ambiguous responses can be recovered with the same key and payload.

## Costs And Trade-offs

- Checkout can wait on cart or inventory locks.
- Cart writes for one shopper are serialized.
- Idempotency records currently have no cleanup window and grow until an explicit retention policy is added.
- Concurrent product archival is not linearized with checkout; checkout uses the product state observed during its transaction.

## Consistency Guarantees

- Price, currency, product state, and inventory are revalidated at checkout.
- Cart display availability is advisory; checkout is authoritative.
- Different carts competing for stock cannot both consume the last unit.
- The same cart cannot create two orders concurrently, even with different keys.
- Exactly-once external payment is not claimed because payment is not implemented.

## Failure And Degradation Behavior

No order is created if validation fails before commit. If the response is lost after commit, the client must retry with the same key and identical payload. Cache invalidation occurs after commit and cannot change the committed result.

## Reconsider When

- Payment, fulfillment, or other external side effects enter checkout.
- Lock waits materially consume the 20-second checkout budget.
- Checkout spans databases or services.
- Idempotency retention or order deletion is introduced.
- Product archival must be strictly ordered against checkout.
