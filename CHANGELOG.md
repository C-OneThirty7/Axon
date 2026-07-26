# Changelog

## [0.3.1] - 2026-07-26

### Fixed

- Windows launcher now reads and displays the bundled release version instead of a stale hard-coded label.

## [0.3.0] - 2026-07-26

### Added

- Native Docker Engine deployment for Ubuntu Server 24.04 and Debian 13.
- AMD64 and ARM64 Linux release packaging.
- systemd-managed Axon stack and host-only Axon Control services.
- Idempotent `axon` operator command.
- Scoped Docker ingress firewall rules for approved client CIDRs.
- Online and fully offline Linux installer modes.
- GitHub Actions validation and repository community metadata.
- Signed, click-through, platform-aware GitHub release updates in Axon Control.
- Windows rollback handoff and root-owned Linux systemd update service.
- Axon Control screenshots and administrator feature documentation.

### Changed

- Axon Control now publishes for Windows x64, Linux x64, and Linux ARM64.
- Windows and Ubuntu AMD64 offline bundles share one release version.

## [0.1.0] - 2026-07-26

- Reproducible offline Windows 11 deployment with Docker Desktop.
- Host-only Axon Control panel.
- Messaging-only hardened Synapse profile with 48-hour retention.
