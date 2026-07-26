#!/usr/bin/env bash
set -Eeuo pipefail

readonly APP_ROOT="/opt/axon"
readonly CONFIG_ROOT="/etc/axon"
readonly STATE_ROOT="/var/lib/axon"
purge_data=0
assume_yes=0

usage() {
    cat <<'EOF'
Usage: sudo ./uninstall.sh [--purge-data] [--yes]

By default, Axon services and program files are removed while configuration,
secrets, Docker volumes, and Matrix data are preserved.

--purge-data  Also remove Axon configuration, secrets, containers, and volumes.
--yes         Confirm destructive data removal without an interactive prompt.

Docker Engine is never removed automatically.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --purge-data) purge_data=1 ;;
        --yes) assume_yes=1 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
    shift
done

if [[ "$EUID" -ne 0 ]]; then
    echo "Run the uninstaller with sudo." >&2
    exit 1
fi

if [[ "$purge_data" -eq 1 && "$assume_yes" -ne 1 ]]; then
    read -r -p "Type PURGE AXON to permanently remove server data: " answer
    [[ "$answer" == "PURGE AXON" ]] || {
        echo "Purge cancelled."
        exit 1
    }
fi

systemctl disable --now axon-stack.service axon-control.service 2>/dev/null || true

if command -v iptables >/dev/null 2>&1; then
    while iptables -C DOCKER-USER -j AXON-INGRESS >/dev/null 2>&1; do
        iptables -D DOCKER-USER -j AXON-INGRESS
    done
    iptables -F AXON-INGRESS 2>/dev/null || true
    iptables -X AXON-INGRESS 2>/dev/null || true
fi

if [[ "$purge_data" -eq 1 ]]; then
    if [[ -r "$CONFIG_ROOT/.env" && -r "$APP_ROOT/deploy/compose.yaml" ]]; then
        docker compose \
            --project-name axon \
            --env-file "$CONFIG_ROOT/.env" \
            --file "$APP_ROOT/deploy/compose.yaml" \
            down --volumes --remove-orphans || true
    fi
    rm -rf -- "$CONFIG_ROOT"
    rm -rf -- "$STATE_ROOT"
    userdel axon 2>/dev/null || true
    echo "Axon program files, configuration, secrets, containers, and volumes were removed."
    echo "This purge is not recoverable unless an external backup exists."
else
    echo "Axon configuration and Docker volumes were preserved."
fi

rm -f -- /etc/systemd/system/axon-stack.service
rm -f -- /etc/systemd/system/axon-control.service
rm -f -- /usr/local/bin/axon
rm -rf -- "$APP_ROOT"
systemctl daemon-reload

echo "Docker Engine was left installed."
