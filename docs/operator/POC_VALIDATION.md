# Axon proof-of-concept validation record

Do not record passwords, access tokens, registration secrets, or signing keys here.

## Build

- Bundle timestamp:
- `versions.json` reviewed:
- `SHA256SUMS` result:
- Windows version/build:
- Docker Desktop version:
- WSL version:

## Network

- Axon adapter name/index/MAC:
- Axon address/prefix:
- Windows Firewall allowed source(s):
- Main gateway WAN address/reservation:
- Main gateway client-network address:
- Downstream router static transit addresses:
- Downstream DHCP client networks:
- Link speed:
- Gateway firmware and mode:
- Client A address:
- Client B address:
- Comparison multicast group/port:

## Automated host audit

```powershell
.\scripts\Test-Axon.ps1 -BindIp <AXON_IP>
```

Before installation, record the standalone bundle result from `.\scripts\Test-AxonBundle.ps1`. After installation, `docker compose ps` should show `postgres`, `synapse`, and `gateway` as healthy. If any service fails, preserve `%ProgramData%\Axon` and capture the bounded diagnostics printed by the installer before running repair.

- Audit result:
- `homeserver.yaml` exists under `%ProgramData%\Axon\runtime\synapse`:
- TCP 80 reachable:
- Matrix versions response:
- Admin path returned 404:
- TCP 8008 blocked:
- TCP 8780 blocked:
- TCP 5432 blocked:
- Loopback TCP 8008 reachable:
- Loopback TCP 8780 reachable:
- Axon Control administrator login:
- Individual user creation:
- Batch creation and issued-account CSV:

## Client A

- Device/OS:
- Element/Element X version:
- Homeserver URL:
- Login result:
- E2EE room result:

## Client B

- Device/OS:
- Element/Element X version:
- Homeserver URL:
- Login result:
- E2EE room result:

## Messaging

- A to B:
- B to A:
- Offline/reconnect delivery:
- Temporary shortened-retention expiry test:
- Background traffic continued during Matrix traffic:

## HTTP compatibility

- Exact error, if any:
- Private-CA TLS profile required: yes/no
