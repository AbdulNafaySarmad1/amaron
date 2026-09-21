# ADR 018: OCI Runtime Portability

Build OCI-compatible images from existing multi-stage Dockerfiles, use Compose for local development, and rootless Podman Quadlet examples for a small production host. This avoids Docker-only assumptions and Kubernetes complexity. Reconsider when multi-host scheduling or HA requirements are real.
