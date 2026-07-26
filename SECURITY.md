# Security policy

## Supported versions

Only the newest Axon release line receives security fixes during the current
proof-of-concept phase.

## Reporting

Do not file public issues containing passwords, Matrix access tokens, signing
keys, registration secrets, database contents, packet captures, private
addresses, or production logs. Contact the repository owner privately before
sharing sensitive diagnostic material.

## Deployment rules

- Every installation generates a unique PostgreSQL password, Synapse
  registration secret, macaroon key, form secret, and Matrix server signing
  key on that host. Never copy runtime state or keys between deployments.
- Release archives contain templates and the public Axon release-verification
  key only. They must never contain an installed `.env`, rendered
  `homeserver.yaml`, server signing key, database, account credentials, or test
  environment state.
- Never expose PostgreSQL, Synapse port 8008, or Axon Control port 8780 to a
  client network or the internet.
- The HTTP client profile is only for trusted isolated networks or encrypted
  tunnels.
- Internet-facing deployments require HTTPS, deliberate DNS/server-name
  planning, external firewall controls, and an explicit public-cloud profile.
- Treat membership in the local `docker` group as root-equivalent access.
- Keep `/etc/axon`, Matrix signing keys, and the private Axon release-signing
  key private. The public release-verification key is intentionally shared and
  is not a deployment secret.
- Do not place Axon databases in long-lived backups when enforcing the 48-hour
  data lifetime. Configuration-only backups are handled separately.

## Retention limits

Synapse retention removes eligible events from the live database. It cannot
remove copies from packet captures, client databases, filesystem snapshots,
hypervisor snapshots, storage-provider backups, or previously exported
archives. Operational backup and capture policies must enforce the same
lifetime when strict deletion is required.
