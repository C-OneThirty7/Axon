# Axon Control operator guide

Axon Control is the host-only administration surface for the Axon Matrix
deployment. It listens only on:

```text
http://127.0.0.1:8780
```

It is not reachable from Matrix client networks. Sign in with a local Matrix
server-administrator account. The password is sent only to the loopback
Synapse listener, is not stored, and produces an eight-hour in-memory session.

## Overview and service control

The Overview page shows:

- Synapse availability plus account, room, and recently-active counts;
- PostgreSQL, Synapse, and nginx container health;
- current per-container CPU and memory use;
- start, stop, and restart controls for each service;
- start, restart, and pause controls for the complete stack.

Prefer whole-stack controls for normal operations. PostgreSQL is a dependency
of Synapse, and nginx is the client-facing gateway. Stopping one service is
intended for diagnosis and immediately interrupts the function it supplies.

`Pause all services` stops the Axon containers but does not delete configuration,
accounts, rooms, messages, or Docker volumes. It also stops client messaging
until `Start all` is selected.

The current GUI login is intentionally verified by local Synapse. If the
operator signs out after pausing Synapse, the GUI cannot authenticate a new
session. Recover by starting the complete `axon` group in Docker Desktop, then
refresh this page. If the page itself is unavailable, run the `Axon Control
Panel` task from Windows Task Scheduler.

## Users

The Users page supports:

- search by Matrix ID or display name;
- individual standard or administrator account creation;
- password reset with existing-device logout;
- promote/demote and lock/unlock;
- last-seen activity categories;
- batches of 1-200 standard or administrator accounts;
- download of the newly issued credentials as CSV.

Axon intentionally does not claim exact online/offline presence. Synapse
presence traffic is disabled for the low-traffic deployment, so the GUI derives
`Recently active`, `Active this hour`, `Active today`, and `Not recent` from
Synapse last-seen timestamps. These values can lag.

Existing usernames are never overwritten during batch issuance. Treat issued
CSV files as sensitive, distribute them privately, and delete them when no
longer needed.

Axon requires newly issued GUI passwords to contain at least 10
characters. Use a unique stock password for each operational batch and require
users to change it after first login.

## Rooms

The Rooms page supports:

- search by room name, alias, or room ID;
- private, encrypted, non-federated room creation;
- optional invitation of existing local users;
- room member inspection;
- adding a local user;
- removing a local user;
- permanent room deletion, blocking, and asynchronous purge.

Rooms created in Axon Control use end-to-end encryption. Synapse still stores
encrypted events and routing metadata temporarily so disconnected recipients
can synchronize within the retention window.

### Taking room control

Matrix room permissions belong to the room, not merely to the Synapse server.
When an administrator adds or removes a member from a room created by someone
else, Axon first grants room-level authority to the signed-in server
administrator and visibly joins that administrator to the room. This is
deliberate and is disclosed in the room inspector. Axon does not silently
impersonate another user.

### Deleting a room

`Delete and purge room` is destructive. Axon requires the exact room name before
starting the Synapse delete operation. The operation removes local members,
blocks rejoining, and asynchronously purges the room from this Synapse server.
It is not a substitute for a data backup and cannot recover a deleted room.

## Logs and troubleshooting

The Logs page returns bounded recent logs for PostgreSQL, Synapse, and nginx.
Refresh after a failed operation and check the affected service first. For a
complete host audit, run:

```powershell
.\scripts\Test-Axon.ps1 -BindIp <AXON_IP>
```

If the GUI is unavailable but Matrix clients still work, verify that the
scheduled task named `Axon Control Panel` is running and that TCP 8780 answers
only on `127.0.0.1`.

## Release updates

The Updates page can check the public
[`C-OneThirty7/Axon`](https://github.com/C-OneThirty7/Axon) GitHub Releases
feed. The request occurs only when an authenticated administrator selects
`Check for updates`; Axon does not create periodic update traffic.

The checker detects the host operating system and architecture and ignores
incompatible release assets. Prereleases are excluded unless the administrator
explicitly enables them.

Select `Download and verify` to stage the matching package. Axon verifies the
published SHA-256 value and the Axon release signature before it enables
`Install update`. Installation requires a separate confirmation, preserves the
existing identity domain, address, secrets, accounts, rooms, and retention
configuration, advances the pinned container images, and restarts the required
services. The page reconnects after Axon Control returns.

On Windows, an elevated detached PowerShell helper performs the handoff and can
restore the previous runtime if installation fails. On Linux, the unprivileged
panel writes a narrowly validated request for a root-owned systemd updater; the
root helper independently verifies the signature and archive again before
executing any release content.
