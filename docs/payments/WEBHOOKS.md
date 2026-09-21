# Webhooks

`POST /api/payments/webhooks/{provider}` accepts a raw body, verifies the provider signature before parsing it as a trusted event, stores a SHA-256 payload hash, and deduplicates `(provider, providerEventId)`. Raw payloads are not persisted. State transition guards prevent delayed events from regressing captured payments.
