# Audited operations control plane

Status: Accepted

## Context

Pricing, demand planning, inventory, replenishment, and promotions need operator workflows without weakening checkout consistency or making model output authoritative. Directly overwriting variant prices or aggregate inventory loses history, prevents review, and makes reconciliation difficult. Forecasts and simulations are uncertain decision support, not facts.

## Decision

Keep PostgreSQL authoritative and add operational records inside the modular monolith. `ProductVariant.Price` and `InventoryItem.QuantityOnHand` remain checkout-compatible materialized values. Approved `PriceRecord` schedules and append-only `InventoryLedgerEntry` movements explain changes to those values. Warehouse stock changes update the aggregate inventory in the same transaction and serialize against checkout through inventory-row locks.

Price precedence is Promotion, Markdown, then Regular, with revision as the tie-breaker. A background activator takes a PostgreSQL advisory transaction lock, applies due schedules, expires ended records, rematerializes the effective variant price, and invalidates storefront read models. Checkout reads only the materialized variant price and snapshots it on the order.

Pricing policies enforce cost, margin, markdown, and change limits. Changes over the approval threshold require a different authenticated user to approve them. Promotions also require maker-checker approval and materialize governed per-variant promotion price records. Forecasts, recommendations, replenishment proposals, and simulations are advisory until an explicit operator action. Simulations never mutate live state and disclose when data is insufficient or assumptions are weak.

Use a dedicated Next.js admin BFF at a separate origin. Its browser session contains no bearer token, mutations require Origin and CSRF checks, and its proxy has an explicit operations route/method allowlist. Keycloak grants coarse operations roles; named ASP.NET policies remain authoritative for pricing, demand, inventory, replenishment, promotions, approvals, and audit access.

Seed only warehouse positions, opening ledger balances, opening price history, and policy defaults derived from existing development catalog data. Do not seed demand observations, forecasts, recommendations, or synthetic operational outcomes.

## Consequences

- Every governed price and stock mutation has an actor, reason, timestamp, and durable history.
- Checkout remains simple and consistent while scheduled prices can activate independently of admin traffic.
- Forecast quality is limited by observed data; empty and unavailable states are expected rather than replaced with fabricated metrics.
- Approval and role configuration require coordination between Keycloak and API policy deployments.
- The activator and application checks reduce races, but production deployments still need one migration job, database monitoring, and workflow-level alerting.

## Alternatives

- Let checkout resolve all price schedules: rejected because it duplicates operational precedence in the critical purchase path.
- Store only current price and inventory values: rejected because it cannot support audit, reconciliation, approval, or safe simulation.
- Let forecasting automatically change prices or purchase quantities: rejected because model output is uncertain and must remain reviewable.
- Put operational authorization entirely in Keycloak: rejected because guardrails, maker-checker rules, and resource state belong beside commerce data.
- Split each operational capability into a service now: rejected because no measured scaling or ownership boundary justifies distributed transactions and coordination.

## Trigger For Reconsideration

Reconsider the materialization worker when activation latency must be below its polling interval, pricing volume requires event-driven scheduling, or multiple regions require a stronger global ordering model. Reconsider the monolith boundary when a capability has an independent owner, workload, availability target, and stable contract that outweigh distributed-system costs.
