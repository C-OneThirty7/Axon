# Axon on macOS

## Current status

macOS was used during Axon's proof-of-concept testing, but it is not a supported
offline server platform in the current public release line. There is no macOS
release asset for end users to download.

Do not use the Windows ZIP or a Linux TAR archive on macOS. GitHub's automatic
source archives are also not offline installers.

## What is supported

- Element Desktop on macOS may be used as a Matrix client.
- Developers may build Axon Control for `osx-arm64` or `osx-x64`.
- The Docker Compose stack can be evaluated manually with Docker Desktop.

Those paths do not yet provide the complete offline packaging, lifecycle
automation, firewall integration, clean-host validation, or operator support
available for Windows and Linux.

## Planned release shape

A future supported macOS server release must include:

1. a self-contained Axon Control build for Apple Silicon and/or Intel;
2. offline Docker Desktop acquisition and license guidance;
3. immutable container image exports;
4. launchd-managed startup and recovery;
5. macOS Packet Filter and application-firewall validation;
6. clean-host installation, reboot, upgrade, and uninstall testing;
7. a dedicated user guide and checksummed release artifact.

Until that work is complete, use Windows 11 x64 or Ubuntu Server 24.04 AMD64 as
the Axon host.
