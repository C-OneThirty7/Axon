# Axon cloud deployment profiles

## Private VM through WireGuard

This is the recommended cloud profile.

```text
Element clients -> client networks/routers -> WireGuard -> private Axon VM
```

- Bind Axon to the VM's private or WireGuard address.
- Permit only the client/VPN CIDRs.
- Do not create public rules for TCP 80, 8008, 8780, or 5432.
- Use an SSH tunnel for Axon Control.
- Keep cloud security groups restrictive even though Axon also installs a host
  firewall policy.

## Private VPC

A VPC-only VM uses the same Compose and systemd configuration. Routing,
site-to-site VPN, and security groups belong to the cloud/network layer. The
Axon host does not become a router.

## Public TLS

Do not expose Axon's default HTTP profile to the internet. A public deployment
requires:

- a deliberately chosen permanent Matrix server name;
- public DNS;
- HTTPS on TCP 443;
- a TLS reverse-proxy profile;
- public-cloud firewall and rate-limit policy;
- an explicit monitoring and update procedure;
- a backup design consistent with the retention policy.

Synapse's server name cannot be changed after deployment. `axon.home.arpa` is
appropriate for isolated deployments and should not be treated as a public
DNS name.

## Cloud images

The preferred VM images are Ubuntu Server 24.04 LTS and Debian 13. Use AMD64
unless ARM64 cost or power efficiency is a deliberate requirement. Begin with
4 vCPU, 8–16 GiB RAM, and a 100 GiB SSD for the planned 200-user messaging
profile, then resize from measured CPU, memory, database, and network use.

## Snapshot warning

Cloud disk snapshots and provider backups can retain encrypted events and
metadata past the configured 48-hour lifetime. If strict expiry is required,
disable database/disk snapshots or apply matching encrypted backup expiration.
Configuration-only exports can be retained separately.
