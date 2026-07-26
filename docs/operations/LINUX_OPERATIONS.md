# Axon Linux operations

## Everyday commands

```bash
sudo axon status
sudo axon start
sudo axon stop
sudo axon restart
sudo axon logs
sudo axon test
```

`axon stop` stops nginx, Synapse, and PostgreSQL while leaving Axon Control
running. `axon stop-all` also stops the control panel.

After a host reboot, Docker, `axon-stack`, and `axon-control` start
automatically. No desktop login is required.

## Service-level control

The Axon Control panel can start, stop, and restart the individual gateway,
Synapse, and PostgreSQL services. If the panel itself is unavailable:

```bash
sudo systemctl restart axon-control
sudo systemctl status axon-control --no-pager
```

The whole Matrix stack:

```bash
sudo systemctl restart axon-stack
```

## Logs

```bash
sudo axon logs
sudo journalctl -u axon-stack -u axon-control --since today
sudo docker compose \
  --project-name axon \
  --env-file /etc/axon/.env \
  --file /opt/axon/deploy/compose.yaml \
  logs --tail 200
```

Do not publish raw logs without reviewing them for Matrix IDs, private
addresses, room information, and operational metadata.

## Users and administrators

Use Axon Control for individual or batch user creation, administrator status,
password resets, account lock/deactivation, rooms, and membership.

If no administrator exists:

```bash
sudo axon create-admin
```

## Configuration-only backup

```bash
sudo axon config-backup
```

This excludes PostgreSQL and Matrix event data. A normal database backup could
retain data beyond Axon's 48-hour policy and is therefore not enabled by
default.

## Firewall

The installer creates a dedicated `AXON-INGRESS` chain reached from Docker's
`DOCKER-USER` chain. This is necessary because Docker-published ports can bypass
ordinary UFW/firewalld expectations.

Reapply it with:

```bash
sudo axon firewall
```

The policy is also applied before the stack starts after every reboot.

## Safe uninstall

Preserve configuration, secrets, and Docker volumes:

```bash
sudo axon uninstall
```

Permanently remove Axon data:

```bash
sudo axon uninstall --purge-data
```

The purge requires typing `PURGE AXON`. Docker Engine remains installed.
