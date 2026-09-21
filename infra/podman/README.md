# Rootless Podman Quadlet

Copy these files to `~/.config/containers/systemd/`, replace `OWNER` and `COMMIT_SHA` with immutable GHCR references, and create protected `~/.config/amaron/*.env` files (`chmod 600`). Run `systemctl --user daemon-reload` then `systemctl --user start amaron-api amaron-storefront`.

These examples intentionally expose only loopback application ports. A separately managed Caddy/Nginx or Cloudflare Tunnel is the public ingress. PostgreSQL and Keycloak are persistent services that need dedicated volumes and protected environment files; use managed databases where practical. Apply EF migrations as an explicit release job before replacing API instances.
