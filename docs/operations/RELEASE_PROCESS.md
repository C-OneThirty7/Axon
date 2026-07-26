# Axon release process

## Source checks

```bash
dotnet test Axon.slnx --configuration Release
./installer/linux/test-contracts.sh
git status --short
```

## Build Linux archives

On a Docker-capable build host:

```bash
./packaging/linux/build-release.sh \
  --version 0.3.1 \
  --distro ubuntu-24.04 \
  --arch amd64

./packaging/linux/build-release.sh \
  --version 0.3.1 \
  --distro debian-13 \
  --arch amd64
```

ARM64:

```bash
./packaging/linux/build-release.sh \
  --version 0.3.1 \
  --distro ubuntu-24.04 \
  --arch arm64
```

Build output is ignored by Git. Inspect each checksum and conduct a clean
offline install before attaching an archive to a release.

## GitHub publication gate

Before any public push:

1. confirm repository visibility;
2. confirm the MIT license and third-party notices;
3. inspect `git diff --cached`;
4. run secret scanning;
5. confirm that `dist`, images, packages, `.env`, captures, and databases are
   absent from tracked files;
6. confirm that no internal workspace or planning artifacts are present in the
   tree or publishable history;
7. create the remote without initializing it with extra files;
8. push only after explicit operator approval.

Generated archives belong in GitHub Releases or another artifact store, not in
normal Git history.

## Sign release assets

Axon Control enables click-through installation only when a release contains
the archive, its `.sha256` file, and a valid `.sig` created with the protected
Axon release key. The private key is never stored in this repository.

```bash
export AXON_RELEASE_SIGNING_KEY=/protected/axon-release-private-key.pem
./scripts/Sign-ReleaseAsset.sh dist/Axon-v0.3.1-offline-win-x64.zip
./scripts/Sign-ReleaseAsset.sh \
  dist/Axon-v0.3.1-offline-ubuntu-24.04-amd64.tar.gz
```

Upload each archive, checksum, and signature together. The public verification
key is pinned in Axon Control and published at
`manifests/axon-release-public-key.pem`.

## Public asset organization

Use exact, platform-specific filenames:

```text
Axon-v<VERSION>-offline-win-x64.zip
Axon-v<VERSION>-offline-ubuntu-24.04-amd64.tar.gz
Axon-v<VERSION>-offline-debian-13-amd64.tar.gz
Axon-v<VERSION>-offline-ubuntu-24.04-arm64.tar.gz
```

Attach matching `.sha256` and `.sig` files for every archive. Release notes must
identify the host operating system, architecture, validation status, minimum
requirements, and the correct installation guide.

Do not advertise a platform in the README download table until its exact asset
has passed a clean-host install, restart, messaging, upgrade, and uninstall
test. Unsupported or source-only targets must be labeled as such.

The initial public release set is intentionally split:

- `v0.1.0`: tested Windows 11 x64 offline bundle;
- `v0.2.0`: validated Ubuntu Server 24.04 AMD64 offline bundle.
- `v0.3.0`: unified Windows 11 x64 and Ubuntu Server 24.04 AMD64
  packages with signed click-through updates;
- `v0.3.1`: Windows launcher version-label correction.

Users should select the release asset from the platform table in the root
README, not GitHub's automatic source archives.
