#!/usr/bin/env bash
set -Eeuo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source_root="$(cd -- "$script_dir/.." && pwd)"
asset_path="${1:-}"
private_key="${AXON_RELEASE_SIGNING_KEY:-}"
public_key="$source_root/manifests/axon-release-public-key.pem"

[[ -n "$asset_path" && -f "$asset_path" ]] || {
    echo "Usage: AXON_RELEASE_SIGNING_KEY=/protected/key.pem $0 RELEASE_ARCHIVE" >&2
    exit 2
}
[[ -n "$private_key" && -f "$private_key" ]] || {
    echo "AXON_RELEASE_SIGNING_KEY must identify the protected Axon release key." >&2
    exit 2
}
[[ -f "$public_key" ]] || {
    echo "Axon release public key is missing: $public_key" >&2
    exit 2
}

signature_path="$asset_path.sig"
openssl dgst -sha256 -sign "$private_key" -out "$signature_path" "$asset_path"
openssl dgst -sha256 -verify "$public_key" -signature "$signature_path" "$asset_path"
chmod 0644 "$signature_path"
printf '%s\n' "$signature_path"
