#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly SCRIPT_DIR
SOURCE_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
readonly SOURCE_ROOT
readonly APP_ROOT="/opt/axon"
readonly CONFIG_ROOT="/etc/axon"
readonly STATE_ROOT="/var/lib/axon"
readonly LOG_ROOT="/var/log/axon"
readonly CLIENT_PORT=80

bind_ip=""
interface_name=""
allowed_cidrs=""
install_mode="auto"
non_interactive=0
strict_preflight=0
skip_firewall=0
skip_initial_user=0

usage() {
    cat <<'EOF'
Usage: sudo ./installer/linux/install.sh [OPTIONS]

Options:
  --bind-ip ADDRESS       Existing host IPv4 address Axon will publish on
  --interface NAME        Existing interface to use; NIC settings are preserved
  --allowed-cidr CIDR     Approved client network; repeat or comma-separate
  --offline               Require bundled Docker packages and image archives
  --online                Install Docker and pull images from official sources
  --non-interactive       Never prompt; required values must resolve uniquely
  --strict-preflight      Turn capacity warnings into installation failures
  --skip-firewall         Do not install the DOCKER-USER ingress policy
  --skip-initial-user     Do not offer interactive administrator creation
  -h, --help              Show this help

Supported reference targets: Ubuntu Server 24.04 LTS and Debian 13.
Compatible targets: Ubuntu 22.04/26.04 and Debian 12.
EOF
}

log() {
    printf '[Axon] %s\n' "$*"
}

warn() {
    printf '[Axon warning] %s\n' "$*" >&2
}

fail() {
    printf '[Axon error] %s\n' "$*" >&2
    exit 1
}

on_error() {
    local exit_code=$?
    printf '[Axon error] Installation stopped at line %s (exit %s).\n' "${BASH_LINENO[0]}" "$exit_code" >&2
    printf '[Axon error] Review %s/install.log and rerun the same command after correcting the cause.\n' "$LOG_ROOT" >&2
    exit "$exit_code"
}

append_allowed_cidr() {
    local value="$1"
    if [[ -z "$allowed_cidrs" ]]; then
        allowed_cidrs="$value"
    else
        allowed_cidrs="$allowed_cidrs,$value"
    fi
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --bind-ip) [[ $# -ge 2 ]] || fail "--bind-ip requires a value"; bind_ip="$2"; shift ;;
        --interface) [[ $# -ge 2 ]] || fail "--interface requires a value"; interface_name="$2"; shift ;;
        --allowed-cidr) [[ $# -ge 2 ]] || fail "--allowed-cidr requires a value"; append_allowed_cidr "$2"; shift ;;
        --offline) install_mode="offline" ;;
        --online) install_mode="online" ;;
        --non-interactive) non_interactive=1 ;;
        --strict-preflight) strict_preflight=1 ;;
        --skip-firewall) skip_firewall=1 ;;
        --skip-initial-user) skip_initial_user=1 ;;
        -h|--help) usage; exit 0 ;;
        *) fail "Unknown option: $1" ;;
    esac
    shift
done

[[ "$EUID" -eq 0 ]] || fail "Run the installer with sudo."
[[ "$(uname -s)" == "Linux" ]] || fail "This installer runs on Linux only."
[[ -r /etc/os-release ]] || fail "/etc/os-release is unavailable."

# shellcheck disable=SC1091
source /etc/os-release
os_id="${ID,,}"
version_id="${VERSION_ID:-unknown}"
case "$os_id" in
    ubuntu)
        [[ "$version_id" == "22.04" || "$version_id" == "24.04" || "$version_id" == "26.04" ]] ||
            fail "Unsupported Ubuntu release: $version_id"
        ;;
    debian)
        [[ "$version_id" == "12" || "$version_id" == "13" ]] ||
            fail "Unsupported Debian release: $version_id"
        ;;
    rhel)
        [[ "$version_id" == 9* || "$version_id" == 10* ]] ||
            fail "Unsupported RHEL release: $version_id"
        ;;
    rocky|almalinux)
        [[ "$version_id" == 9* || "$version_id" == 10* ]] ||
            fail "Unsupported RHEL-compatible release: $os_id $version_id"
        warn "$PRETTY_NAME is a compatibility target; Docker does not verify derivative distributions."
        ;;
    *)
        fail "Unsupported distribution: $PRETTY_NAME"
        ;;
esac

machine_arch="$(uname -m)"
case "$machine_arch" in
    x86_64) axon_arch="amd64"; dotnet_rid="linux-x64" ;;
    aarch64|arm64) axon_arch="arm64"; dotnet_rid="linux-arm64" ;;
    *) fail "Axon Linux supports x86_64 and arm64; detected $machine_arch." ;;
esac

install -d -m 0755 "$LOG_ROOT"
exec > >(tee -a "$LOG_ROOT/install.log") 2>&1
trap on_error ERR
umask 077

log "Axon Linux installer for $PRETTY_NAME ($axon_arch)"

verify_checksums() {
    local manifest="$SOURCE_ROOT/manifests/SHA256SUMS"
    [[ -f "$manifest" ]] || {
        warn "No release checksum manifest is present; source-tree installation mode is active."
        return
    }

    log "Verifying release payload checksums."
    (
        cd "$SOURCE_ROOT"
        sha256sum --check manifests/SHA256SUMS
    )
}

capacity_check() {
    local available_memory_kib available_disk_kib
    available_memory_kib="$(awk '/^MemTotal:/ {print $2}' /proc/meminfo)"
    available_disk_kib="$(df --output=avail -k /var | awk 'NR==2 {print $1}')"

    if (( available_memory_kib < 4 * 1024 * 1024 )); then
        if [[ "$strict_preflight" -eq 1 ]]; then
            fail "Less than 4 GiB RAM is available."
        fi
        warn "Less than 4 GiB RAM is available; this is a small lab profile."
    elif (( available_memory_kib < 16 * 1024 * 1024 )); then
        warn "Less than the recommended 16 GiB RAM is available; capacity below 200 users is expected."
    fi

    if (( available_disk_kib < 20 * 1024 * 1024 )); then
        if [[ "$strict_preflight" -eq 1 ]]; then
            fail "Less than 20 GiB free space is available under /var."
        fi
        warn "Less than 20 GiB free space is available; image extraction may fail."
    elif (( available_disk_kib < 100 * 1024 * 1024 )); then
        warn "Less than the recommended 100 GiB free space is available."
    fi
}

list_candidates() {
    ip -o -4 addr show scope global |
        awk '$2 !~ /^(lo|docker[0-9]*|br-|veth|virbr)/ {
            split($4, address, "/");
            print $2 "|" address[1] "|" address[2]
        }'
}

network_cidr() {
    local address="$1" prefix="$2" a b c d value mask network
    IFS=. read -r a b c d <<< "$address"
    value=$(( (a << 24) | (b << 16) | (c << 8) | d ))
    mask=$(( (0xFFFFFFFF << (32 - prefix)) & 0xFFFFFFFF ))
    network=$(( value & mask ))
    printf '%d.%d.%d.%d/%d' \
        $(( (network >> 24) & 255 )) \
        $(( (network >> 16) & 255 )) \
        $(( (network >> 8) & 255 )) \
        $(( network & 255 )) \
        "$prefix"
}

validate_network_inputs() {
    [[ "$interface_name" =~ ^[A-Za-z0-9_.:-]{1,15}$ ]] ||
        fail "The selected interface name cannot be stored safely: $interface_name"

    local normalized="" cidr address prefix octet
    IFS=',' read -r -a cidr_values <<< "$allowed_cidrs"
    for cidr in "${cidr_values[@]}"; do
        cidr="${cidr//[[:space:]]/}"
        [[ "$cidr" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}/([0-9]|[12][0-9]|3[0-2])$ ]] ||
            fail "Invalid approved client CIDR: $cidr"
        address="${cidr%/*}"
        prefix="${cidr#*/}"
        IFS=. read -r -a octets <<< "$address"
        for octet in "${octets[@]}"; do
            (( 10#$octet <= 255 )) || fail "Invalid approved client CIDR: $cidr"
        done
        cidr="$(network_cidr "$address" "$prefix")"
        if [[ -z "$normalized" ]]; then
            normalized="$cidr"
        else
            normalized="$normalized,$cidr"
        fi
    done
    [[ -n "$normalized" ]] || fail "At least one approved client CIDR is required."
    allowed_cidrs="$normalized"
}

select_network() {
    mapfile -t candidates < <(list_candidates)
    [[ "${#candidates[@]}" -gt 0 ]] || fail "No active global IPv4 interface was found."

    local filtered=() candidate iface address prefix
    for candidate in "${candidates[@]}"; do
        IFS='|' read -r iface address prefix <<< "$candidate"
        if [[ -n "$interface_name" && "$iface" != "$interface_name" ]]; then
            continue
        fi
        if [[ -n "$bind_ip" && "$address" != "$bind_ip" ]]; then
            continue
        fi
        filtered+=("$candidate")
    done

    if [[ "${#filtered[@]}" -eq 0 ]]; then
        fail "The selected interface does not currently own the requested IPv4 address. Axon does not rewrite NIC settings."
    fi

    if [[ "${#filtered[@]}" -gt 1 ]]; then
        if [[ "$non_interactive" -eq 1 ]]; then
            fail "Multiple IPv4 candidates remain; pass --interface and/or --bind-ip."
        fi
        echo "Available Axon interfaces:"
        local index=1
        for candidate in "${filtered[@]}"; do
            IFS='|' read -r iface address prefix <<< "$candidate"
            printf '  %d) %s  %s/%s\n' "$index" "$iface" "$address" "$prefix"
            ((index+=1))
        done
        read -r -p "Select an interface number: " selection
        [[ "$selection" =~ ^[0-9]+$ ]] || fail "Interface selection must be a number."
        (( selection >= 1 && selection <= ${#filtered[@]} )) || fail "Interface selection is out of range."
        candidate="${filtered[$((selection - 1))]}"
    else
        candidate="${filtered[0]}"
    fi

    IFS='|' read -r interface_name bind_ip prefix_length <<< "$candidate"
    if [[ -z "$allowed_cidrs" ]]; then
        allowed_cidrs="$(network_cidr "$bind_ip" "$prefix_length")"
    fi
    log "Using existing interface $interface_name at $bind_ip/$prefix_length."
    log "Approved client networks: $allowed_cidrs"
}

docker_ready() {
    command -v docker >/dev/null 2>&1 &&
        docker version --format '{{.Server.Version}}' >/dev/null 2>&1 &&
        docker compose version >/dev/null 2>&1
}

install_docker_online_deb() {
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y ca-certificates curl gpg iproute2 iptables
    install -m 0755 -d /etc/apt/keyrings
    curl --fail --silent --show-error --location \
        "https://download.docker.com/linux/$os_id/gpg" \
        --output /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc

    local suite
    suite="${UBUNTU_CODENAME:-${VERSION_CODENAME:-}}"
    [[ -n "$suite" ]] || fail "Unable to determine the Docker APT suite."
    cat > "/etc/apt/sources.list.d/docker.sources" <<EOF
Types: deb
URIs: https://download.docker.com/linux/$os_id
Suites: $suite
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
EOF
    apt-get update
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
}

install_docker_online_rpm() {
    dnf -y install dnf-plugins-core iproute iptables
    dnf config-manager --add-repo https://download.docker.com/linux/rhel/docker-ce.repo
    dnf -y install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
}

install_docker_offline() {
    local package_dir="$SOURCE_ROOT/packages/${os_id}-${version_id}/${axon_arch}"
    [[ -d "$package_dir" ]] || fail "Offline Docker packages are missing: $package_dir"
    case "$os_id" in
        ubuntu|debian)
            compgen -G "$package_dir/*.deb" >/dev/null || fail "No .deb packages were found in $package_dir"
            export DEBIAN_FRONTEND=noninteractive
            if ! dpkg -i "$package_dir"/*.deb; then
                log "Resolving the bundled package configuration order with APT in offline mode."
            fi
            apt-get --fix-broken install -y --no-download
            [[ -z "$(dpkg --audit)" ]] || fail "Offline Docker packages remain partially configured."
            ;;
        rhel|rocky|almalinux)
            compgen -G "$package_dir/*.rpm" >/dev/null || fail "No .rpm packages were found in $package_dir"
            dnf -y install "$package_dir"/*.rpm --disablerepo='*'
            ;;
    esac
}

ensure_docker() {
    if docker_ready; then
        systemctl enable docker.service
        log "Existing Docker Engine and Compose plugin are ready."
        return
    fi

    if command -v docker >/dev/null 2>&1; then
        systemctl start docker.service 2>/dev/null || true
        if docker_ready; then
            systemctl enable docker.service
            log "Existing Docker Engine and Compose plugin are ready."
            return
        fi
        fail "An existing Docker installation is present but unusable. Repair or remove it explicitly before Axon installation."
    fi

    if [[ "$install_mode" == "auto" ]]; then
        if compgen -G "$SOURCE_ROOT/packages/${os_id}-${version_id}/${axon_arch}/*" >/dev/null; then
            install_mode="offline"
        else
            install_mode="online"
        fi
    fi

    log "Installing Docker Engine in $install_mode mode."
    if [[ "$install_mode" == "offline" ]]; then
        install_docker_offline
    else
        case "$os_id" in
            ubuntu|debian) install_docker_online_deb ;;
            rhel|rocky|almalinux) install_docker_online_rpm ;;
        esac
    fi

    systemctl enable --now docker.service
    docker_ready || fail "Docker Engine or the Docker Compose plugin did not become ready."
}

json_value() {
    local key="$1" file="$2"
    sed -nE "s/.*\"$key\"[[:space:]]*:[[:space:]]*\"([^\"]+)\".*/\\1/p" "$file" | head -n 1
}

install_payload() {
    local control_source=""
    for candidate in \
        "$SOURCE_ROOT/bin/Axon.Control" \
        "$SOURCE_ROOT/artifacts/$dotnet_rid/Axon.Control"; do
        if [[ -x "$candidate" ]]; then
            control_source="$(dirname "$candidate")"
            break
        fi
    done
    [[ -n "$control_source" ]] ||
        fail "The self-contained $dotnet_rid Axon Control payload is missing. Install from a Linux release bundle."

    install -d -m 0755 "$APP_ROOT" "$APP_ROOT/bin" "$APP_ROOT/deploy" \
        "$APP_ROOT/installer/linux" "$APP_ROOT/docs" "$APP_ROOT/manifests"
    cp -a "$SOURCE_ROOT/deploy/." "$APP_ROOT/deploy/"
    cp -a "$SOURCE_ROOT/installer/linux/." "$APP_ROOT/installer/linux/"
    cp -a "$SOURCE_ROOT/docs/." "$APP_ROOT/docs/"
    cp -a "$SOURCE_ROOT/manifests/." "$APP_ROOT/manifests/"
    cp -a "$control_source/." "$APP_ROOT/bin/"
    install -m 0644 "$SOURCE_ROOT/VERSION" "$APP_ROOT/VERSION"
    chmod 0755 "$APP_ROOT/bin/Axon.Control" "$APP_ROOT/installer/linux/"*.sh "$APP_ROOT/installer/linux/axon"
}

load_or_pull_images() {
    local manifest="$SOURCE_ROOT/manifests/image-digests.json"
    [[ -r "$manifest" ]] || fail "Image digest manifest is missing: $manifest"
    synapse_image="$(json_value synapse "$manifest")"
    postgres_image="$(json_value postgres "$manifest")"
    nginx_image="$(json_value nginx "$manifest")"
    [[ "$synapse_image" == *@sha256:* || "$synapse_image" == axon.local/synapse:sha256-* ]] ||
        fail "Synapse image is not immutable."
    [[ "$postgres_image" == *@sha256:* || "$postgres_image" == axon.local/postgres:sha256-* ]] ||
        fail "PostgreSQL image is not immutable."
    [[ "$nginx_image" == *@sha256:* || "$nginx_image" == axon.local/nginx:sha256-* ]] ||
        fail "nginx image is not immutable."

    if compgen -G "$SOURCE_ROOT/images/*.tar" >/dev/null; then
        log "Loading bundled Docker images."
        local archive
        for archive in "$SOURCE_ROOT"/images/*.tar; do
            docker load --input "$archive"
        done
    elif [[ "$install_mode" == "offline" ]]; then
        fail "Offline Docker image archives are missing."
    else
        docker pull "$synapse_image"
        docker pull "$postgres_image"
        docker pull "$nginx_image"
    fi

    docker image inspect "$synapse_image" "$postgres_image" "$nginx_image" >/dev/null
}

render_runtime() {
    install -d -m 0750 -o root -g axon "$CONFIG_ROOT" "$CONFIG_ROOT/runtime"
    if [[ -e "$CONFIG_ROOT/.env" ]]; then
        for required in \
            "$CONFIG_ROOT/.env" \
            "$CONFIG_ROOT/runtime/synapse/homeserver.yaml" \
            "$CONFIG_ROOT/runtime/nginx/default.conf"; do
            [[ -s "$required" ]] || fail "Existing Axon runtime is incomplete: $required"
        done
        saved_bind_ip="$(sed -nE 's/^AXON_BIND_IP=(.+)$/\1/p' "$CONFIG_ROOT/.env" | head -n 1)"
        [[ "$saved_bind_ip" == "$bind_ip" ]] ||
            fail "Existing Axon runtime is bound to $saved_bind_ip, not $bind_ip. Use the original address or perform an explicit migration."
        log "Reusing existing Axon secrets and runtime configuration."
    else
        "$APP_ROOT/bin/Axon.Control" render-runtime \
            "$APP_ROOT" \
            "$CONFIG_ROOT" \
            "$bind_ip" \
            "$synapse_image" \
            "$postgres_image" \
            "$nginx_image"
    fi

    chown -R root:axon "$CONFIG_ROOT"
    chmod 0750 "$CONFIG_ROOT" "$CONFIG_ROOT/runtime" \
        "$CONFIG_ROOT/runtime/synapse" "$CONFIG_ROOT/runtime/nginx"
    chmod 0640 "$CONFIG_ROOT/.env" \
        "$CONFIG_ROOT/runtime/synapse/homeserver.yaml" \
        "$CONFIG_ROOT/runtime/nginx/default.conf"

    cat > "$CONFIG_ROOT/install.conf" <<EOF
AXON_BIND_IP='$bind_ip'
AXON_CLIENT_PORT='$CLIENT_PORT'
AXON_ALLOWED_CIDRS='$allowed_cidrs'
AXON_INTERFACE='$interface_name'
AXON_FIREWALL_ENABLED='$((1 - skip_firewall))'
EOF
    chown root:root "$CONFIG_ROOT/install.conf"
    chmod 0600 "$CONFIG_ROOT/install.conf"
}

initialize_synapse_volume() {
    docker volume create \
        --label com.docker.compose.project=axon \
        --label com.docker.compose.volume=axon_synapse \
        axon_axon_synapse >/dev/null
    docker run --rm \
        --user root \
        --volume axon_axon_synapse:/data \
        --entrypoint chown \
        "$synapse_image" \
        -R 991:991 /data
}

install_services() {
    getent group axon >/dev/null || groupadd --system axon
    id axon >/dev/null 2>&1 || useradd \
        --system \
        --gid axon \
        --groups docker \
        --home-dir "$STATE_ROOT" \
        --shell /usr/sbin/nologin \
        axon
    usermod -a -G docker axon
    install -d -m 0750 -o axon -g axon "$STATE_ROOT"

    install -m 0644 "$APP_ROOT/deploy/systemd/axon-stack.service" /etc/systemd/system/axon-stack.service
    install -m 0644 "$APP_ROOT/deploy/systemd/axon-control.service" /etc/systemd/system/axon-control.service
    install -m 0755 "$APP_ROOT/installer/linux/axon" /usr/local/bin/axon
    systemctl daemon-reload
    systemctl enable axon-stack.service axon-control.service
}

start_and_test() {
    "$APP_ROOT/installer/linux/configure-firewall.sh"
    docker compose \
        --project-name axon \
        --env-file "$CONFIG_ROOT/.env" \
        --file "$APP_ROOT/deploy/compose.yaml" \
        config --quiet
    systemctl restart axon-stack.service axon-control.service
    curl --retry 20 --retry-delay 2 --retry-all-errors \
        --fail --silent --show-error \
        http://127.0.0.1:8008/health >/dev/null
    curl --retry 20 --retry-delay 2 --retry-all-errors \
        --fail --silent --show-error \
        http://127.0.0.1:8780/api/session >/dev/null
}

offer_initial_user() {
    [[ "$skip_initial_user" -eq 0 && "$non_interactive" -eq 0 ]] || return
    read -r -p "Create the initial Matrix administrator now? [Y/n] " answer
    if [[ ! "$answer" =~ ^[Nn] ]]; then
        docker exec -it axon-synapse-1 \
            register_new_matrix_user \
            --admin \
            --config /config/homeserver.yaml \
            http://127.0.0.1:8008
    fi
}

verify_checksums
capacity_check
select_network
validate_network_inputs
ensure_docker
getent group docker >/dev/null || fail "Docker did not create its operator group."
getent group axon >/dev/null || groupadd --system axon
install_payload
load_or_pull_images
render_runtime
initialize_synapse_volume
install_services
start_and_test
offer_initial_user

cat > "$STATE_ROOT/install-state" <<EOF
version=$(cat "$APP_ROOT/VERSION")
installed_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)
distribution=$os_id
distribution_version=$version_id
architecture=$axon_arch
interface=$interface_name
bind_ip=$bind_ip
allowed_cidrs=$allowed_cidrs
EOF
chown axon:axon "$STATE_ROOT/install-state"
chmod 0640 "$STATE_ROOT/install-state"

log "Axon installation completed successfully."
log "Element homeserver: http://$bind_ip"
log "Matrix identity domain: axon.home.arpa"
log "Local administration: http://127.0.0.1:8780"
log "Remote administration: ssh -L 8780:127.0.0.1:8780 USER@$bind_ip"
log "Operations: sudo axon status"
