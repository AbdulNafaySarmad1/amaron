# Deployment

Local development uses `docker compose --env-file .env up --build` from `infra/`. The compose file is development-only and binds dependencies to loopback. Copy `.env.example` for placeholders; do not commit populated secrets.

Production images are OCI images built from the existing Dockerfiles and are runnable by Docker or Podman. GHCR tags use the immutable Git commit SHA. Deploy by digest where your registry tooling supports it. Application containers are disposable; PostgreSQL, Keycloak data, Cloudinary assets, and protected cryptographic key material are persistent and require backups outside container writable layers.

Rootless Podman Quadlet examples live in `infra/podman`. Provide runtime env files through protected host/secret-management injection, place a mature ingress or Cloudflare Tunnel in front of loopback ports, and configure `PUBLIC_HOSTS`, `TRUSTED_PROXY_IPS`, exact frontend origins, database credentials, and Data Protection persistence. PostgreSQL/Valkey never receive public ingress.

Run migrations explicitly once per release before scaling/replacing API containers. Roll back an application by redeploying a previous known-good image SHA/digest and checking `/health/live` and `/health/ready`; schema rollback is not automatic.
