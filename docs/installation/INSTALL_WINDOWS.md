# Install Axon v0.1.0 on Windows 11

## Requirements

- Windows 11 x64 build 22631 or newer.
- Administrator account and hardware virtualization enabled.
- 8 GiB RAM is Docker Desktop's supported baseline; 16 GiB is recommended for
  the planned 200-user messaging deployment.
- 20 GiB free disk is a practical installation floor; 100 GiB is the
  recommended operating target.
- Docker Desktop operator signed into Windows interactively.
- Authorization to accept and use the current Docker Desktop license. Docker
  may require a paid subscription for larger enterprises or government use.
- Expanded, checksummed Axon offline bundle on a local NTFS drive.
- A stable private IPv4 address reachable from the intended client networks.

The Razer 18 with 32 GiB RAM and SSD/NVMe storage is comfortably suitable.
Memory and disk shortfalls produce visible capacity warnings but do not stop a
normal installation. They may reduce the user count or soak duration the host
can support. `-StrictPreflight` is available only when an organization
deliberately wants sizing recommendations enforced as policy.

The target host does not download from GitHub, package feeds, or container registries. The bundle includes Docker Desktop, WSL, Linux/AMD64 images, the self-contained Axon Control executable, scripts, checksums, and guides.

## Before installation

1. Complete the environment worksheet in `docs\operator\ROUTER_AND_NIC.md`.
2. Reserve or statically configure the intended Axon address.
3. Configure the Windows adapter in Network Settings.
4. Leave the correct address, gateway, and DNS intact. `NicMode Preserve` is the default.
5. Extract the ZIP completely to a local NTFS folder. Do not run from inside
   the ZIP, removable media, or a network share.

## Install

Double-click `Install Axon.cmd` in the extracted bundle and approve the Windows
administrator prompt. The launcher uses a process-only execution-policy bypass;
it does not change Windows PowerShell policy.

The installer verifies every bundled file before executing a payload. It lists
connected physical adapters and their current IPv4 addresses. If exactly one
usable adapter and address exist, Axon selects them. Otherwise, enter the exact
adapter name and address displayed by Windows. The default `Preserve` mode
never rewrites the selected NIC, gateway, or DNS.

If Windows enables WSL features or installs a component that needs a reboot,
restart Windows and double-click `Install Axon.cmd` again. Existing installation
state is detected and reused.

Advanced routed deployments may still use PowerShell to supply explicit source
CIDRs:

For routed client networks, provide every permitted source CIDR:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-Axon.ps1 `
  -AllowedRemoteAddress "10.77.0.0/24","10.88.0.0/24"
```

The installer:

- verifies bundle checksums before executing payloads;
- enables required Windows/WSL features and requests a restart when necessary;
- installs bundled WSL and Docker Desktop components;
- waits for Docker's Linux engine;
- loads immutable offline images;
- resolves and verifies the selected NIC/address before rendering runtime files;
- renders and verifies `.env`, `homeserver.yaml`, and nginx configuration;
- preserves the selected NIC by default;
- creates a source-scoped TCP 80 Windows Firewall rule;
- idempotently creates and repairs Docker volume ownership;
- validates Compose before startup;
- starts PostgreSQL, Synapse, and nginx and prints bounded diagnostics on failure;
- prompts for the first Matrix server administrator;
- installs the host-only Axon Control scheduled task;
- creates only the proven `Axon Control` desktop shortcut and opens
  `http://127.0.0.1:8780`.

Installer progress is recorded in `%ProgramData%\Axon\install-state.json`.

## Cold start and recovery

After a normal restart, sign in and start Docker Desktop. The Axon containers
use `restart: unless-stopped` and normally return automatically. Open the
`Axon Control` Desktop shortcut.

If the control page does not load, open Windows Task Scheduler, select
`Task Scheduler Library`, right-click `Axon Control Panel`, and choose `Run`.
If it already reports running, choose `End` and then `Run`.

If Synapse was stopped before sign-out, GUI login cannot authenticate until the
containers are running. Start the complete `axon` group from Docker Desktop,
then refresh Axon Control. This host recovery path was proven during the
v0.1.0 Windows restart test.

## Axon Control GUI

Axon Control listens only on:

```text
http://127.0.0.1:8780
```

Sign in with a Matrix server-administrator account. The password is sent only to local Synapse; it is not stored. The resulting access token remains in memory for an eight-hour host-only session.

The GUI provides:

- stack health, live per-container CPU/memory use, and per-service start/stop/restart;
- whole-stack start, restart, and pause without deleting data;
- bounded PostgreSQL, Synapse, and nginx logs;
- local account search/listing with last-seen activity categories;
- individual standard or administrator account creation;
- batches of 1-200 accounts with prefix, start number, padding, stock password, and role;
- a downloadable issued-account CSV;
- password reset with device logout;
- promote/demote and lock/unlock;
- room search, private encrypted room creation, and membership inspection;
- add/remove local room members with disclosed room-control elevation;
- exact-name-confirmed room deletion, blocking, and asynchronous purge.

Existing usernames are never overwritten during batch issuance.

Synapse presence is disabled, so activity categories are based on last-seen
timestamps and are not a promise of exact online/offline status. Room member
changes in user-created rooms visibly join the signed-in administrator after
granting room-level authority. See `docs\operator\AXON_CONTROL.md` before using
service stop or room deletion controls.

Stock Element and Element X allow users to change passwords from account/security settings. They do not support a Synapse-enforced first-login password change. Treat the issued CSV as sensitive, distribute credentials privately, and delete it when no longer needed.

## Validate

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Axon.ps1
```

The audit confirms:

- TCP 80 and Matrix versions are reachable at the Axon address;
- nginx blocks the LAN Admin API;
- PostgreSQL, Synapse, and nginx are healthy;
- control TCP 8780 and Synapse admin TCP 8008 work only on loopback;
- ports 8008, 8780, and 5432 are not reachable through the Axon LAN address.

## Repair

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Repair-Axon.ps1
```

Repair preserves existing secrets and database data. It recreates only Axon firewall rules, rechecks runtime completeness, repairs Synapse volume ownership even after an interrupted first attempt, validates Compose, and reinstalls/restarts Axon Control.

If the protected runtime is only partially present, the installer stops instead of overwriting it. Preserve `%ProgramData%\Axon` and review the printed diagnostic logs.

## Uninstall

```powershell
.\scripts\Uninstall-Axon.ps1
```

Normal uninstall stops Axon, unregisters the control task, removes the desktop shortcut and Axon firewall rules, and preserves `%ProgramData%\Axon` plus Docker volumes.

Permanent deletion requires:

```powershell
.\scripts\Uninstall-Axon.ps1 -PurgeData
```

and typing `PURGE AXON` exactly.
