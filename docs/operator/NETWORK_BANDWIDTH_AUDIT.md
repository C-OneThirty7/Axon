# Axon network bandwidth audit

This audit separates Matrix application traffic from other traffic sharing the
client network. Complete a baseline before changing Synapse or Element
behavior.

## What each measurement means

Axon has two relevant measurement layers:

1. The Matrix layer: login, `/sync`, encrypted events, device keys, room state,
   typing, receipts, account data, and other client API requests.
2. The Ethernet/IP layer: TCP packets between Element and Axon's TCP 80
   listener.

Windows packet capture measures traffic at the Axon host interface. Record the
interface, negotiated link speed, route, latency, packet loss, retransmissions,
and reported network utilization for every run.

## Sensitive-data warning

Axon currently uses HTTP. Packet captures can contain passwords during login,
access tokens, user and device IDs, room IDs, timing metadata, and encrypted
event payloads. Do not share raw ETL, PCAPNG, or packet CSV files. The JSON
summary is the preferred artifact to share after review.

## Server capture

Open Windows PowerShell as Administrator in the original Axon folder:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\scripts\network-audit\Start-AxonTrafficAudit.ps1 `
    -BindIp 10.20.30.2 `
    -Scenario idle-two-clients `
    -DurationSeconds 300
```

Replace the example address with the Axon Windows address. The script:

- verifies which physical interface owns the address;
- filters TCP traffic involving Axon's address and port 80;
- captures NIC-level packets using Windows Packet Monitor;
- records adapter counters, routes, active TCP connections, Docker state, and
  five-second container resource samples;
- extracts Synapse `Processed request` lines for endpoint, response-size, and
  request-duration attribution;
- converts the capture to PCAPNG;
- writes the run beneath `%ProgramData%\Axon\network-audits`.

The default saves the first 256 bytes of each packet while retaining the
original on-wire frame length. This is enough for packet-size and flow analysis
but is still sensitive. Do not set `PacketBytes` to `0` unless full payload
capture is explicitly required.

If TShark is installed on the analysis system:

```powershell
.\scripts\network-audit\Summarize-AxonPcap.ps1 `
    -PcapPath C:\ProgramData\Axon\network-audits\<RUN>\axon-traffic.pcapng `
    -BindIp 10.20.30.2
```

The summary reports frame count, Ethernet/IP wire bytes, inbound/outbound bytes,
TCP payload bytes, average bit rate, packet-size distribution, remote
addresses, TCP conversations, and retransmission markers.

## Shared-path capture with comparison traffic

A capture from a shared gateway or switch can measure Matrix alongside other
traffic using the same network path. Capture the relevant interface without a
Matrix-only filter, use PCAPNG, and retain the original packet length.

Complete matched runs such as:

1. normal background traffic without Element;
2. background traffic plus two connected but idle Element clients;
3. background traffic plus the fixed steady-messaging scenario;
4. background traffic plus the offline-send and reconnect scenario.

Use equal durations and record exact UTC timestamps. If one long capture is
used, maintain a timestamped action log so the file can be divided into phases.

Optional UDP ports or multicast groups can be classified separately:

```powershell
.\scripts\network-audit\Summarize-AxonPcap.ps1 `
    -PcapPath .\shared-network.pcapng `
    -BindIp 10.20.30.2 `
    -ComparisonUdpPort 6969 `
    -ComparisonMulticastAddress 239.2.3.1
```

The values above are examples. The report separates Matrix, selected comparison
traffic, and all remaining traffic.

## Controlled test matrix

Use the same two accounts, room, clients, network path, and capture duration
throughout. Rebooting, changing rooms, or changing client versions invalidates
direct comparisons.

| Run | Duration | Client action |
|---|---:|---|
| Server only | 5 min | No Element client connected |
| Idle one | 5 min | One Element client foregrounded and idle |
| Idle two | 5 min | Two connected clients, no interaction |
| Login | 2 min | Start logged out; sign in once after capture begins |
| Single message | 2 min | Send one fixed 32-character message at 60 seconds |
| Steady messaging | 5 min | Alternate one fixed message every 5 seconds |
| Offline send | 5 min | Disconnect B, send ten messages from A |
| Reconnect | 5 min | Reconnect B after the first 60 seconds |
| Background | 10 min | Put both clients in the background |

Repeat the matrix separately for Element and Element X. Do not mix clients
inside one comparison unless that mix is the intended deployment.

For every run record:

- exact Element/Element X version and operating system;
- Axon/Synapse package version;
- client IP addresses, gateway, and route;
- host-interface link speed, latency, packet loss, and reported utilization;
- whether comparison traffic was active;
- selected UDP ports or multicast groups when used;
- JSON capture summary and interface counter deltas.

## Current Axon traffic surface

The current configuration already disables:

- media repository and URL previews;
- presence;
- federation listener exposure;
- Synapse usage reporting;
- open registration.

Client-visible traffic still necessarily includes:

- authentication and token use;
- long-poll or sliding synchronization;
- E2EE device keys and to-device events;
- room membership and state;
- encrypted message events;
- account data and push rules;
- optional typing indicators and receipts;
- TCP acknowledgements and connection maintenance.

Internal PostgreSQL traffic, Docker health checks, and Synapse-to-nginx traffic
do not traverse the client network. Only traffic reaching TCP 80 on the Axon
host crosses the client path.

## Tuning candidates to test, not assume

Baseline first, then change one item at a time:

1. Enable nginx gzip for Matrix JSON and compare wire bytes and CPU.
2. Explicitly disable Synapse push calculation if unread counts are not needed.
3. Confirm no client pusher is registered and no Synapse egress is attempted.
4. Compare Element and Element X idle synchronization overhead.
5. Disable typing indicators in client policy if users do not need them.
6. Preserve E2EE device-list and to-device flows; removing them breaks reliable
   encrypted messaging.
7. Keep one Synapse process and PostgreSQL. Workers or Redis add traffic and
   complexity without evidence of a server bottleneck.

No optimization is accepted unless two-client messaging, offline/reconnect
delivery, E2EE, and the 48-hour retention behavior still pass afterward.
