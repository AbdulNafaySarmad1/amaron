# Incident Response

1. Detect through structured security events, provider/edge alerts, and audit logs. Preserve trace IDs and relevant safe identifiers.
2. Contain: disable compromised accounts/sessions, remove compromised origin ingress, block abusive principals at edge and application layers, or disable an affected payment provider.
3. Rotate affected secret, database credential, OIDC client secret, or webhook secret through the secret manager; invalidate sessions when session/key material is affected.
4. Recover from tested backups/migrations, replay verified webhook events where applicable, and reconcile financial state before reopening checkout.
5. Verify with credential revocation checks, audit review, dependency rebuild/scan, targeted security tests, and monitoring for recurrence.

Credential stuffing, origin exposure, suspicious admin actions, dependency vulnerabilities, and leaked credentials require an incident owner and documented timeline. Do not place secrets or raw authentication/payment values in tickets or logs.
