## Summary

Describe the operator-visible result.

## Target environments

- [ ] Windows 11 / Docker Desktop
- [ ] Ubuntu Server AMD64
- [ ] Debian AMD64
- [ ] Linux ARM64
- [ ] Private cloud/VPN
- [ ] Public TLS profile

## Validation

- [ ] `dotnet test Axon.slnx --configuration Release`
- [ ] `./installer/linux/test-contracts.sh`
- [ ] Docker Compose validation
- [ ] Clean-install or upgrade path tested

## Security and operations

Describe changes to ports, CIDRs, secrets, retention, backups, services, or
privilege boundaries. Confirm that no credentials, databases, packet captures,
or generated release archives are included.
