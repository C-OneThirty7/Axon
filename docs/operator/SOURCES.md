# Axon offline release sources

Axon release bundles are assembled only on a connected build host. The target Windows host does not download dependencies.

Reviewed primary inputs are declared in `manifests/release-inputs.json`:

- Synapse releases: the official `element-hq/synapse` GitHub release API.
- Synapse container: `matrixdotorg/synapse` on Docker Hub.
- PostgreSQL and nginx: Docker Official Images.
- Docker Desktop: the official `desktop.docker.com` Windows AMD64 installer.
- WSL: the official `microsoft/WSL` GitHub release API and x64 MSI asset.
- Synapse administration: the official Admin API, User Admin API, room Admin
  API, room-membership Admin API, and shared-secret registration API at
  `element-hq.github.io/synapse/latest/admin_api/`.
- Matrix room creation, invitation, and kick behavior: the official
  Client-Server API at `spec.matrix.org`.

Room-control implementation references:

- https://element-hq.github.io/synapse/latest/admin_api/rooms.html
- https://element-hq.github.io/synapse/latest/admin_api/room_membership.html
- https://element-hq.github.io/synapse/latest/admin_api/user_admin_api.html
- https://spec.matrix.org/unstable/client-server-api/

`Build-OfflineBundle.ps1` rejects draft or prerelease API responses, downloads unmodified installers, resolves immutable upstream image digests, and exports each Linux/AMD64 image under an `axon.local` tag derived from that digest. Compose is set to `pull_policy: never`; the checked bundle cannot contact a registry or substitute a floating tag. The builder also publishes the self-contained Windows binary and writes `SHA256SUMS` covering every bundle payload. The expanded bundle is preserved alongside its ZIP for inspection.

Vendor binaries and generated release bundles are never committed to the Axon repository or uploaded to GitHub.
