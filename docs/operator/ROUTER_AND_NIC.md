# Router, addressing, and Windows NIC guide

Axon does not require one fixed subnet. It requires:

1. a stable private IPv4 address on the Windows host;
2. TCP 80 reachability from every client network;
3. a working return path from Windows to those clients; and
4. a Windows Firewall rule that permits the source addresses Windows actually sees.

Do not configure Axon as DNS. Axon serves Matrix traffic and its host-only
administration interface; unrelated application traffic remains outside Axon.

## Deployment worksheet

Record these values before running the installer:

| Value | Example from the proven POC | New environment |
|---|---|---|
| Axon host address | `10.20.30.2/24` | |
| Axon host adapter | Mac Wi-Fi during POC; dedicated Windows Ethernet for release | |
| Upstream gateway | `172.20.0.1` | |
| Main network gateway WAN | DHCP reservation, example `172.20.0.50` | |
| Main network gateway LAN | `10.30.0.1/24` | |
| Downstream router transit addresses | static, example `10.30.0.6` | |
| Downstream client DHCP networks | router-specific | |
| Element URL visible to clients | `http://10.20.30.2` in the POC | |
| Source address Windows sees | local gateway/NAT address or routed client address | |
| Windows Firewall remote scope | `LocalSubnet` or explicit CIDR list | |

Reserve the Axon host address in DHCP or configure it statically. If its address changes, the Element URL, Synapse `public_baseurl`, Compose binding, firewall rule, and router forward/route must change together.

## Pattern A: flat LAN or transparent bridge

Use one subnet when every intermediary is operating as a transparent bridge.

```text
Element clients -- bridged network -- Axon Windows host
```

- Give each infrastructure device a unique management address.
- Run DHCP on exactly one device.
- Disable DHCP and NAT on secondary bridges.
- Disable AP/client isolation.
- No port forwarding is required; clients connect directly to the Axon host address.

## Pattern B: routed or NATed network gateways

This matches the proven POC:

```text
Upstream LAN
  gateway 172.20.0.1
  |
  +-- Axon host 10.20.30.2
  |
  +-- main network gateway WAN (DHCP/reserved, e.g. 172.20.0.50)
       client-network address 10.30.0.1
       |
       +-- downstream routers (static transit addresses)
            |
            +-- each router may provide DHCP/NAT to its own clients
```

The main gateway may receive its WAN address by DHCP, but reserve that lease
whenever routes or forwards depend on it. Downstream routers may keep static
transit addresses and separate DHCP client pools.

The exact route or port-forward rule is environment-specific:

- If downstream traffic is NATed toward the upstream LAN, Axon normally sees the gateway's upstream address. `LocalSubnet` is an appropriate Windows Firewall scope.
- If client subnets are routed without NAT, the upstream router needs a return route for every downstream CIDR through the main gateway's upstream address. Pass those CIDRs to the installer.
- If a port forward is used, clients must enter the address on the incoming side of that forward, and the forward must target the stable Axon address on TCP 80.

Do not assume that ping proves or disproves Matrix reachability. macOS and many gateways can suppress ICMP. Test TCP 80 and the Matrix versions endpoint.

## Windows adapter checks

```powershell
Get-NetAdapter -Physical |
    Format-Table Name, InterfaceIndex, Status, MacAddress, LinkSpeed

Get-NetIPAddress -AddressFamily IPv4 |
    Format-Table InterfaceAlias, InterfaceIndex, IPAddress, PrefixLength

Get-NetRoute -AddressFamily IPv4 |
    Format-Table DestinationPrefix, NextHop, InterfaceAlias, RouteMetric
```

The default installer mode is `Preserve`. Configure the address in Windows first; Axon verifies the exact address and does not rewrite its IPv4, gateway, or DNS settings.

For a dedicated isolated NIC, a blank gateway and DNS are usually correct. For an Axon host on an existing upstream LAN, preserve the gateway and DNS already required by that Windows environment.

## Installer examples

NATed gateway traffic whose source appears local to Windows:

```powershell
.\scripts\Install-Axon.ps1 `
    -BindIp 10.20.30.2 `
    -InterfaceAlias "Ethernet" `
    -AllowedRemoteAddress LocalSubnet
```

Routed client networks:

```powershell
.\scripts\Install-Axon.ps1 `
    -BindIp 10.20.30.2 `
    -InterfaceAlias "Ethernet" `
    -AllowedRemoteAddress "10.30.0.0/24","10.40.0.0/24"
```

Only use `-NicMode Configure` when Axon is deliberately authorized to change that adapter. It adds and verifies the requested address before removing any older address and requires exact typed confirmation.

## Troubleshooting decision tree

From a Windows client:

```powershell
ipconfig
route print -4
tracert -d <AXON_IP>
Test-NetConnection <AXON_IP> -Port 80
curl.exe --noproxy "*" http://<AXON_IP>/_matrix/client/versions
```

- Cannot reach the client's own gateway: fix the local router/DHCP network.
- Can reach gateways but not TCP 80: inspect NAT, routes, client isolation, VLAN policy, and Windows Firewall scope.
- TCP 80 works but Matrix versions fails: run `Test-Axon.ps1` on the host and inspect the GUI logs.
- Matrix versions works but Element rejects the URL: record the exact Element build and HTTP-policy error; private-CA TLS is the planned fallback.

## Traffic policy

- LAN/client exposure: TCP 80 only.
- Host loopback only: Synapse TCP 8008 and Axon Control TCP 8780.
- Not published: PostgreSQL 5432 and Synapse federation 8448.
- Unrelated application traffic: no Axon service or firewall rule.
