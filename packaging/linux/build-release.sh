#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly SCRIPT_DIR
SOURCE_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd)"
readonly SOURCE_ROOT

version="$(tr -d '[:space:]' < "$SOURCE_ROOT/VERSION")"
arch="amd64"
distro="ubuntu-24.04"
output_root="$SOURCE_ROOT/dist"
include_packages=1

usage() {
    cat <<'EOF'
Usage: ./packaging/linux/build-release.sh [OPTIONS]

Options:
  --version VERSION       Release version (default: VERSION file)
  --arch amd64|arm64      Target architecture
  --distro TARGET         ubuntu-24.04, ubuntu-26.04, debian-13
  --output DIRECTORY      Output directory (default: dist)
  --skip-docker-packages  Build an online-install bundle without OS packages
  -h, --help              Show help

The normal output is a fully offline Linux archive containing pinned Docker
images, Docker Engine packages, Axon Control, checksums, and documentation.
EOF
}

fail() {
    echo "Axon Linux packager: $*" >&2
    exit 1
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) [[ $# -ge 2 ]] || fail "--version requires a value"; version="$2"; shift ;;
        --arch) [[ $# -ge 2 ]] || fail "--arch requires a value"; arch="$2"; shift ;;
        --distro) [[ $# -ge 2 ]] || fail "--distro requires a value"; distro="$2"; shift ;;
        --output) [[ $# -ge 2 ]] || fail "--output requires a value"; output_root="$2"; shift ;;
        --skip-docker-packages) include_packages=0 ;;
        -h|--help) usage; exit 0 ;;
        *) fail "Unknown option: $1" ;;
    esac
    shift
done

[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail "Version must use X.Y.Z."
case "$arch" in
    amd64) dotnet_rid="linux-x64" ;;
    arm64) dotnet_rid="linux-arm64" ;;
    *) fail "Architecture must be amd64 or arm64." ;;
esac
case "$distro" in
    ubuntu-24.04) distro_image="ubuntu:24.04"; distro_id="ubuntu"; distro_version="24.04" ;;
    ubuntu-26.04) distro_image="ubuntu:26.04"; distro_id="ubuntu"; distro_version="26.04" ;;
    debian-13) distro_image="debian:13"; distro_id="debian"; distro_version="13" ;;
    *) fail "Unsupported package target: $distro" ;;
esac

for command_name in docker dotnet curl git python3 tar; do
    command -v "$command_name" >/dev/null 2>&1 || fail "$command_name is required."
done
docker version >/dev/null
docker buildx version >/dev/null

work_root="$(mktemp -d)"
trap 'rm -rf -- "$work_root"' EXIT
if [[ "$include_packages" -eq 1 ]]; then
    bundle_flavor="offline"
else
    bundle_flavor="online"
fi
bundle_name="Axon-v${version}-${bundle_flavor}-${distro}-${arch}"
bundle_root="$work_root/$bundle_name"
mkdir -p \
    "$bundle_root/bin" \
    "$bundle_root/deploy" \
    "$bundle_root/docs" \
    "$bundle_root/images" \
    "$bundle_root/installer/linux" \
    "$bundle_root/manifests" \
    "$bundle_root/packages/${distro_id}-${distro_version}/${arch}"

copy_tracked_path() {
    local relative_path="$1"
    [[ -f "$SOURCE_ROOT/$relative_path" ]] ||
        fail "Tracked release input is missing: $relative_path"
    mkdir -p "$bundle_root/$(dirname -- "$relative_path")"
    cp -a "$SOURCE_ROOT/$relative_path" "$bundle_root/$relative_path"
}

while IFS= read -r tracked_path; do
    copy_tracked_path "$tracked_path"
done < <(
    git -C "$SOURCE_ROOT" ls-files \
        deploy \
        docs \
        installer/linux \
        manifests/release-inputs.json \
        README.md \
        SECURITY.md \
        LICENSE \
        THIRD_PARTY_NOTICES.md \
        VERSION \
        CHANGELOG.md
)

dotnet publish "$SOURCE_ROOT/src/Axon.Control/Axon.Control.csproj" \
    --configuration Release \
    --runtime "$dotnet_rid" \
    --self-contained true \
    --output "$bundle_root/bin"
[[ -x "$bundle_root/bin/Axon.Control" ]] || fail "Axon Control publish did not produce $dotnet_rid."

image_inputs_text="$(
    python3 - "$SOURCE_ROOT/manifests/release-inputs.json" <<'PY'
import json
import sys
import urllib.request

with open(sys.argv[1], encoding="utf-8") as stream:
    inputs = json.load(stream)

request = urllib.request.Request(
    inputs["synapseLatestReleaseApi"],
    headers={"Accept": "application/vnd.github+json", "User-Agent": "Axon-Linux-Packager"},
)
with urllib.request.urlopen(request, timeout=30) as response:
    release = json.load(response)
if release.get("draft") or release.get("prerelease"):
    raise SystemExit("Synapse latest release is not stable.")
tag = release.get("tag_name", "")
if not tag.startswith("v"):
    raise SystemExit("Synapse latest release tag is invalid.")

print(f'{inputs["synapseImage"]}:{tag}')
print(inputs["postgresImage"])
print(inputs["nginxImage"])
print(tag)
PY
)"
synapse_tag="$(printf '%s\n' "$image_inputs_text" | sed -n '1p')"
postgres_tag="$(printf '%s\n' "$image_inputs_text" | sed -n '2p')"
nginx_tag="$(printf '%s\n' "$image_inputs_text" | sed -n '3p')"
synapse_version="$(printf '%s\n' "$image_inputs_text" | sed -n '4p')"
[[ -n "$synapse_tag" && -n "$postgres_tag" && -n "$nginx_tag" && -n "$synapse_version" ]] ||
    fail "Unable to resolve release image inputs."

get_repo_digest() {
    local reference="$1" digest
    digest="$(docker image inspect "$reference" --format '{{index .RepoDigests 0}}')"
    [[ "$digest" =~ @sha256:[a-f0-9]{64}$ ]] || fail "No immutable digest was found for $reference."
    printf '%s' "$digest"
}

export_image() {
    local source_digest="$1" component="$2" digest_hex local_reference context
    digest_hex="${source_digest##*@sha256:}"
    local_reference="axon.local/${component}:sha256-${digest_hex}"
    context="$work_root/image-$component"
    mkdir -p "$context"
    printf 'FROM %s\n' "$source_digest" > "$context/Dockerfile"
    docker buildx build \
        --platform "linux/$arch" \
        --pull \
        --tag "$local_reference" \
        --output "type=docker,dest=$bundle_root/images/${component}-linux-${arch}.tar" \
        "$context"
    [[ -s "$bundle_root/images/${component}-linux-${arch}.tar" ]] ||
        fail "Image export failed for $component."
    printf '%s' "$local_reference"
}

for reference in "$synapse_tag" "$postgres_tag" "$nginx_tag"; do
    docker pull --platform "linux/$arch" "$reference"
done
synapse_digest="$(get_repo_digest "$synapse_tag")"
postgres_digest="$(get_repo_digest "$postgres_tag")"
nginx_digest="$(get_repo_digest "$nginx_tag")"
synapse_local="$(export_image "$synapse_digest" synapse)"
postgres_local="$(export_image "$postgres_digest" postgres)"
nginx_local="$(export_image "$nginx_digest" nginx)"

python3 - "$bundle_root/manifests/image-digests.json" <<PY
import json
import sys

payload = {
    "synapse": "$synapse_local",
    "postgres": "$postgres_local",
    "nginx": "$nginx_local",
    "upstream": {
        "synapse": "$synapse_digest",
        "postgres": "$postgres_digest",
        "nginx": "$nginx_digest",
    },
}
with open(sys.argv[1], "w", encoding="utf-8") as stream:
    json.dump(payload, stream, indent=2)
    stream.write("\\n")
PY

if [[ "$include_packages" -eq 1 ]]; then
    package_output="$bundle_root/packages/${distro_id}-${distro_version}/${arch}"
    docker run --rm \
        --platform "linux/$arch" \
        --volume "$package_output:/out" \
        --env AXON_DISTRO_ID="$distro_id" \
        "$distro_image" \
        bash -Eeuo pipefail -c '
            export DEBIAN_FRONTEND=noninteractive
            rm -f /etc/apt/apt.conf.d/docker-clean
            apt-get update
            apt-get install -y ca-certificates curl gpg
            cp -a /var/cache/apt/archives/*.deb /out/
            install -m 0755 -d /etc/apt/keyrings
            curl --fail --silent --show-error --location \
                "https://download.docker.com/linux/${AXON_DISTRO_ID}/gpg" \
                --output /etc/apt/keyrings/docker.asc
            chmod a+r /etc/apt/keyrings/docker.asc
            . /etc/os-release
            suite="${UBUNTU_CODENAME:-${VERSION_CODENAME}}"
            cat > /etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/${AXON_DISTRO_ID}
Suites: ${suite}
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
EOF
            apt-get update
            apt-get install -y --download-only \
                docker-ce \
                docker-ce-cli \
                containerd.io \
                docker-buildx-plugin \
                docker-compose-plugin \
                curl \
                iproute2 \
                iptables
            cp -a /var/cache/apt/archives/*.deb /out/
        '
    compgen -G "$package_output/*.deb" >/dev/null || fail "Docker package collection was empty."
else
    rm -rf -- "$bundle_root/packages"
fi

python3 - "$bundle_root/manifests/versions.json" <<PY
import datetime
import json
import sys

payload = {
    "axon": "$version",
    "builtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "target": "$distro-$arch",
    "synapse": "$synapse_version",
    "postgres": "$postgres_tag",
    "nginx": "$nginx_tag",
    "offlineDockerPackages": bool($include_packages),
}
with open(sys.argv[1], "w", encoding="utf-8") as stream:
    json.dump(payload, stream, indent=2)
    stream.write("\\n")
PY

python3 - "$bundle_root" "$version" "$synapse_version" <<'PY'
import datetime
import hashlib
import json
import pathlib
import sys
import urllib.parse
import uuid

root = pathlib.Path(sys.argv[1])
axon_version = sys.argv[2]
synapse_version = sys.argv[3]
with (root / "manifests" / "image-digests.json").open(encoding="utf-8") as stream:
    images = json.load(stream)

def hash_file(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()

components = [{
    "type": "application",
    "bom-ref": f"pkg:generic/axon@{axon_version}",
    "name": "Axon",
    "version": axon_version,
    "purl": f"pkg:generic/axon@{axon_version}",
}]

for name in ("synapse", "postgres", "nginx"):
    upstream = images["upstream"][name]
    repository, digest = upstream.split("@sha256:", 1)
    components.append({
        "type": "container",
        "bom-ref": f"pkg:oci/{urllib.parse.quote(repository, safe='/')}@sha256:{digest}",
        "name": repository,
        "version": f"sha256:{digest}",
        "purl": f"pkg:oci/{urllib.parse.quote(repository, safe='/')}@sha256:{digest}",
        "hashes": [{"alg": "SHA-256", "content": digest}],
    })

for package in sorted((root / "packages").rglob("*.deb")) if (root / "packages").exists() else []:
    name, version, architecture = package.stem.rsplit("_", 2)
    version = urllib.parse.unquote(version)
    purl = (
        f"pkg:deb/{urllib.parse.quote(name)}@{urllib.parse.quote(version)}"
        f"?arch={urllib.parse.quote(architecture)}"
    )
    components.append({
        "type": "library",
        "bom-ref": purl,
        "name": name,
        "version": version,
        "purl": purl,
        "hashes": [{"alg": "SHA-256", "content": hash_file(package)}],
    })

dependency_file = root / "bin" / "Axon.Control.deps.json"
if dependency_file.exists():
    with dependency_file.open(encoding="utf-8") as stream:
        dependencies = json.load(stream)
    for library in sorted(dependencies.get("libraries", {})):
        if "/" not in library:
            continue
        name, library_version = library.rsplit("/", 1)
        purl = (
            f"pkg:nuget/{urllib.parse.quote(name)}"
            f"@{urllib.parse.quote(library_version)}"
        )
        components.append({
            "type": "library",
            "bom-ref": purl,
            "name": name,
            "version": library_version,
            "purl": purl,
        })

payload = {
    "bomFormat": "CycloneDX",
    "specVersion": "1.6",
    "serialNumber": f"urn:uuid:{uuid.uuid4()}",
    "version": 1,
    "metadata": {
        "timestamp": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "component": components[0],
    },
    "components": components[1:],
}
with (root / "manifests" / "sbom.cdx.json").open("w", encoding="utf-8") as stream:
    json.dump(payload, stream, indent=2)
    stream.write("\n")

sources = {
    "axon": {"version": axon_version, "repository": "Git repository containing this release"},
    "synapse": {
        "version": synapse_version,
        "repository": "https://github.com/element-hq/synapse",
        "sourceArchive": f"https://github.com/element-hq/synapse/archive/refs/tags/{synapse_version}.tar.gz",
        "image": images["upstream"]["synapse"],
    },
    "postgres": {
        "repository": "https://github.com/docker-library/postgres",
        "image": images["upstream"]["postgres"],
    },
    "nginx": {
        "repository": "https://github.com/nginx/docker-nginx",
        "image": images["upstream"]["nginx"],
    },
    "dockerPackages": "https://download.docker.com/linux/",
}
with (root / "manifests" / "sources.json").open("w", encoding="utf-8") as stream:
    json.dump(sources, stream, indent=2)
    stream.write("\n")
PY

cat > "$bundle_root/README_FIRST.txt" <<EOF
AXON v$version - OFFLINE LINUX INSTALLER
========================================

Target: $distro $arch

1. Copy and extract this complete archive on the target Linux host.
2. Enter the extracted directory.
3. Run: sudo ./installer/linux/install.sh --offline
4. Select an existing interface/address if prompted.
5. Create the initial Matrix administrator when prompted.
6. Run: sudo axon status

Axon preserves existing NIC, gateway, route, and DNS configuration.
Administration remains local at http://127.0.0.1:8780.
See docs/installation/INSTALL_LINUX.md before deploying.
EOF

chmod 0755 "$bundle_root/bin/Axon.Control" "$bundle_root/installer/linux/"*
python3 - "$bundle_root" <<'PY'
import hashlib
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
manifest = root / "manifests" / "SHA256SUMS"
with manifest.open("w", encoding="ascii", newline="\n") as output:
    for path in sorted(item for item in root.rglob("*") if item.is_file() and item != manifest):
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        output.write(f"{digest} *{path.relative_to(root).as_posix()}\n")
PY

mkdir -p "$output_root"
archive_path="$output_root/$bundle_name.tar.gz"
[[ ! -e "$archive_path" ]] || fail "Output already exists: $archive_path"
tar --create --gzip --file "$archive_path" --directory "$work_root" "$bundle_name"
python3 - "$archive_path" <<'PY'
import hashlib
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
digest = hashlib.sha256(path.read_bytes()).hexdigest()
path.with_name(path.name + ".sha256").write_text(
    f"{digest} *{path.name}\n",
    encoding="ascii",
)
PY

echo "Axon Linux release: $archive_path"
echo "SHA-256 file: $archive_path.sha256"
