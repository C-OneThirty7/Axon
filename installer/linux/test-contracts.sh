#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
readonly ROOT

required_files=(
    deploy/compose.yaml
    deploy/systemd/axon-stack.service
    deploy/systemd/axon-control.service
    installer/linux/install.sh
    installer/linux/uninstall.sh
    installer/linux/configure-firewall.sh
    installer/linux/axon
    packaging/linux/build-release.sh
    docs/installation/INSTALL_LINUX.md
    docs/operations/LINUX_OPERATIONS.md
    SECURITY.md
    VERSION
)

for relative in "${required_files[@]}"; do
    [[ -s "$ROOT/$relative" ]] || {
        echo "Missing Linux deployment payload: $relative" >&2
        exit 1
    }
done

for script in "$ROOT"/installer/linux/* "$ROOT"/packaging/linux/*.sh; do
    [[ -f "$script" ]] || continue
    bash -n "$script"
done

grep -Fq 'ListenLocalhost(8780)' "$ROOT/src/Axon.Control/Program.cs"
grep -Fq "\${AXON_CONTROL_BIND_IP:-127.0.0.1}:\${AXON_SYNAPSE_ADMIN_PORT:-8008}:8008" "$ROOT/deploy/compose.yaml"
grep -Fq 'AXON-INGRESS' "$ROOT/installer/linux/configure-firewall.sh"
grep -Fq 'ProtectSystem=strict' "$ROOT/deploy/systemd/axon-control.service"
grep -Fq 'pull_policy: never' "$ROOT/deploy/compose.yaml"

if command -v shellcheck >/dev/null 2>&1; then
    shellcheck "$ROOT"/installer/linux/* "$ROOT"/packaging/linux/*.sh
else
    echo "shellcheck is unavailable; Bash syntax and contract checks still ran."
fi

echo "Axon Linux deployment contract tests passed."
