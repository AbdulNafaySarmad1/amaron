# Transactional supply-chain execution

Status: Accepted

## Context

Supplier sourcing, purchase commitments, expected shipments, fiscal documents, physical receiving, and stock disposition must agree without allowing an invoice or ASN scan to create sellable inventory. The checkout path already depends on `InventoryItem.QuantityOnHand` and `WarehouseStock`, so a replacement inventory model would create avoidable migration and consistency risk.

## Decision

Keep the capability inside the modular monolith and PostgreSQL transaction boundary. Supplier organizations own source terms, portal memberships, purchase orders, ASNs, and invoices. Keycloak grants coarse capabilities; supplier ownership is enforced again in application queries through `supplier_users`.

Purchase-order line values are copied from an explicit supplier source or an explicitly awarded quotation so cost, lead time, MOQ, order multiple, and reliability remain visible. RFQs invite named supplier organizations, quotations snapshot every commercial term, and no score or cheapest-price rule can award business. A human with the approval permission selects a quotation, records a reason, and creates a draft PO that still requires maker-checker approval. Purchase orders use explicit states and maker-checker approval. Supplier ASNs and fiscal invoices are expected-document records only. Invoice resolvers are allowlisted adapters; scanned URLs are syntax-checked and retained as evidence but are never fetched by the service.

Physical receipt posting is the sole inbound authority. It takes a PostgreSQL transaction, an idempotency advisory lock, a purchase-order row lock, and stable inventory-row locks. A receipt line must reconcile physical quantity to accepted plus damaged plus quarantined quantities. It updates disposition balances and appends one ledger entry per nonzero disposition. Only accepted quantity updates the checkout-compatible aggregate inventory. Invoice matching compares purchase-order terms, physical received quantity, and invoice lines after the documents exist independently.

Cycle counts are blind warehouse tasks. Planning snapshots expected state without returning it to the counter, submission records physical observations without changing stock, and a different authorized operator reconciles differences. Reconciliation locks aggregate inventory, updates the state balance, appends disposition-specific ledger adjustments, and updates checkout-compatible inventory only for the available state.

Inter-warehouse transfers use draft, in-transit, and received states. Dispatch and receipt are independently idempotent. Dispatch removes units from source available and checkout-compatible inventory while creating destination in-transit balances; receipt consumes in-transit balances and restores destination available inventory. Both transitions append state-specific ledger entries and complete their warehouse tasks in the same transaction.

Large documents remain in object storage; PostgreSQL stores only provider metadata, hashes, and document references.

## Consequences

- Expected quantities cannot become sellable stock before warehouse confirmation.
- Duplicate receipt submission replays the original result; a changed request with the same key conflicts.
- Checkout remains compatible while state-specific balances support warehouse control.
- Supplier portal access requires both a Keycloak permission and one active database membership.
- State transitions and matching exceptions remain auditable and queryable.

## Alternatives

- Treat invoices or ASNs as receipts: rejected because expected documents do not prove physical custody or condition.
- Replace checkout inventory immediately: rejected because it expands the critical-path migration without improving receipt correctness.
- Use distributed locks or a message broker: rejected because all authoritative writes share one PostgreSQL database.
- Fetch scanned fiscal URLs: rejected because it introduces SSRF and falsely implies authority verification.

## Trigger For Reconsideration

Reconsider the module boundary when procurement or warehouse execution has an independent owner, workload, and availability target. Reconsider aggregate inventory compatibility after allocation and fulfillment workflows can migrate checkout without a dual-authority period.
