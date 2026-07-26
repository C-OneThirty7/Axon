# Axon Linux deployment architecture

## Common application stack

Every Axon host uses the same application topology:

```text
approved Matrix clients
          |
          | TCP 80 (private HTTP profile)
          v
host bind IP on an existing NIC
          |
          | Docker-published port guarded by AXON-INGRESS
          v
nginx gateway :80
          |
          | internal Docker network
          v
Synapse :8008 ---------------- PostgreSQL :5432
          ^
          |
127.0.0.1:8008
          |
Axon Control 127.0.0.1:8780
```

PostgreSQL is attached only to Docker's internal network. Synapse port 8008 is
published only on host loopback for Axon Control. The admin panel is a host
systemd service rather than a container, so it remains available when the
Matrix stack is intentionally stopped.

## Host responsibilities

Linux provides:

- Docker Engine and the Compose plugin
- systemd service ordering and restart behavior
- an existing IPv4 address and route
- `iptables` enforcement in Docker's `DOCKER-USER` path
- operator access through the local console or an SSH tunnel

Axon does not configure DHCP, gateways, DNS, interface addresses, cloud routes,
VPNs, or router port forwarding.

## Files and ownership

| Path | Purpose | Default access |
|---|---|---|
| `/opt/axon` | Immutable program and deployment files | root-owned, readable |
| `/etc/axon` | Secrets and rendered configuration | root:`axon`, restricted |
| `/var/lib/axon` | Control service state and config-only exports | `axon` service |
| `/var/log/axon` | Installer log | root-owned |
| Docker volumes | Synapse keys and PostgreSQL database | Docker-managed |

Membership in the Docker group is root-equivalent. Only the dedicated `axon`
service account and authorized operators should have that membership.

## Scaling boundary

The initial Linux profile is a single Synapse process and a local PostgreSQL
container. This is deliberate for a messaging-first deployment of roughly 200
registered users. Redis, Synapse workers, Kubernetes, and external database
services are deferred until measured load demonstrates a need.
