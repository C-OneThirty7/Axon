# Install Axon on Linux

## Reference platforms

- Ubuntu Server 24.04 LTS, AMD64
- Debian 13, AMD64

Ubuntu 22.04/26.04, Debian 12, and Linux ARM64 are compatible targets included
in the installer and release tooling. RHEL 9/10 is supported by the online
installer; a dedicated offline RPM builder remains a separate release target.

Use native Docker Engine. Docker Desktop for Linux is unnecessary.

## Hardware guidance

| Profile | CPU | RAM | Free SSD |
|---|---:|---:|---:|
| Small lab | 2–4 cores | 4–8 GiB | 40 GiB |
| Up to 200 text users | 4+ modern cores | 8–16 GiB | 100 GiB |
| Heavy soak testing | 8 cores | 16–32 GiB | 200 GiB |

Capacity shortfalls produce warnings. They stop installation only when
`--strict-preflight` is supplied.

## Prepare networking

Configure the Linux host's address, gateway, DNS, VLAN, VPN, and routes using
the operating system or network manager before starting Axon. The installer
will not change them.

Confirm the intended address:

```bash
ip -br -4 address
ip -4 route
```

For example:

```text
Axon host:       10.20.30.2/24
Approved CIDR:   10.20.30.0/24
Router/gateway:  determined by the deployment network
```

Axon does not require the Matrix host to be the clients' default gateway.

## Offline installation

Extract the matching release:

```bash
tar -xzf Axon-v0.3.0-offline-ubuntu-24.04-amd64.tar.gz
cd Axon-v0.3.0-offline-ubuntu-24.04-amd64
sudo ./installer/linux/install.sh --offline
```

If the host has one active non-container IPv4 address, the installer selects
it. With several addresses, select interactively or provide exact values:

```bash
sudo ./installer/linux/install.sh \
  --offline \
  --interface enp3s0 \
  --bind-ip 10.20.30.2 \
  --allowed-cidr 10.20.30.0/24
```

Multiple routed client networks are supported:

```bash
sudo ./installer/linux/install.sh \
  --offline \
  --bind-ip 10.20.30.2 \
  --allowed-cidr 10.30.0.0/24 \
  --allowed-cidr 10.40.0.0/24
```

The installation:

1. verifies the complete release checksum manifest;
2. checks OS, architecture, memory, disk, and interfaces;
3. installs bundled Docker Engine packages when required;
4. loads immutable Synapse, PostgreSQL, and nginx images;
5. creates secrets and `homeserver.yaml` before Compose starts;
6. initializes Synapse volume ownership;
7. applies the approved-client firewall policy;
8. installs and starts `axon-stack` and `axon-control`;
9. tests Synapse and Axon Control locally;
10. offers to create the first Matrix administrator.

## Online installation

An online release bundle without OS packages can use:

```bash
sudo ./installer/linux/install.sh --online
```

Docker packages come from Docker's official repository. Runtime images must
still be supplied through the release's immutable image manifest.

## Verify

```bash
sudo axon test
sudo axon status
curl -fsS http://127.0.0.1:8008/health
curl -fsS http://HOST_IP/axon-health
```

Use `http://HOST_IP` as the custom homeserver in Element. Matrix accounts remain
in the `axon.home.arpa` identity domain.

## Admin panel

On the host, browse to:

```text
http://127.0.0.1:8780
```

From another operator computer:

```bash
ssh -L 8780:127.0.0.1:8780 operator@HOST_IP
```

Then browse to the same loopback URL. Do not publish port 8780.

## Reinstallation and repair

Rerunning the same release installer preserves an existing complete runtime and
its secrets. It refuses to silently repair a partial runtime because replacing
individual secrets or signing material could strand accounts.

Use:

```bash
sudo axon status
sudo axon logs
sudo axon test
```

before rerunning installation.
