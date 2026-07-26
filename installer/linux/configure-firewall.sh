#!/usr/bin/env bash
set -Eeuo pipefail

readonly CONFIG_FILE="${AXON_INSTALL_CONFIG:-/etc/axon/install.conf}"
readonly CHAIN="AXON-INGRESS"

if [[ ! -r "$CONFIG_FILE" ]]; then
    echo "Axon firewall configuration is unavailable: $CONFIG_FILE" >&2
    exit 1
fi

# The file is root-owned mode 0600 and contains installer-generated shell
# assignments only.
# shellcheck source=/dev/null
source "$CONFIG_FILE"

if [[ "${AXON_FIREWALL_ENABLED:-1}" != "1" ]]; then
    echo "Axon host firewall integration was explicitly disabled."
    exit 0
fi

for required in AXON_BIND_IP AXON_CLIENT_PORT AXON_ALLOWED_CIDRS; do
    if [[ -z "${!required:-}" ]]; then
        echo "Missing $required in $CONFIG_FILE" >&2
        exit 1
    fi
done

if ! command -v iptables >/dev/null 2>&1; then
    echo "iptables is required to protect Docker-published Axon ports." >&2
    exit 1
fi

iptables -N "$CHAIN" 2>/dev/null || true
if ! iptables -C DOCKER-USER -j "$CHAIN" >/dev/null 2>&1; then
    iptables -I DOCKER-USER 1 -j "$CHAIN"
fi
iptables -F "$CHAIN"

iptables -A "$CHAIN" \
    -m conntrack --ctstate ESTABLISHED,RELATED \
    -j RETURN

IFS=',' read -r -a allowed_cidrs <<< "$AXON_ALLOWED_CIDRS"
for cidr in "${allowed_cidrs[@]}"; do
    cidr="${cidr//[[:space:]]/}"
    [[ -n "$cidr" ]] || continue
    iptables -A "$CHAIN" \
        -p tcp \
        -s "$cidr" \
        -m conntrack \
        --ctorigdst "$AXON_BIND_IP" \
        --ctorigdstport "$AXON_CLIENT_PORT" \
        -m comment --comment "Axon approved client network" \
        -j RETURN
done

iptables -A "$CHAIN" \
    -p tcp \
    -m conntrack \
    --ctorigdst "$AXON_BIND_IP" \
    --ctorigdstport "$AXON_CLIENT_PORT" \
    -m comment --comment "Axon deny unapproved client network" \
    -j DROP
iptables -A "$CHAIN" -j RETURN

echo "Axon Docker ingress allows ${AXON_ALLOWED_CIDRS} to ${AXON_BIND_IP}:${AXON_CLIENT_PORT}."
