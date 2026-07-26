# Contributing

## Local workflow

1. Create a branch from the current development branch.
2. Keep secrets and generated release payloads out of Git.
3. Run `dotnet test Axon.slnx --configuration Release`.
4. Run `./installer/linux/test-contracts.sh`.
5. Run `docker compose --env-file tests/fixtures/compose.env -f
   deploy/compose.yaml config --quiet` when Docker is available.
6. Update `CHANGELOG.md` for operator-visible changes.

Commits should be focused and use an imperative summary. Pull requests should
describe the target environment, test evidence, migration impact, and any
changes to networking, retention, or security boundaries.

Do not upload real packet captures, credentials, Matrix databases, signing
keys, or offline release bundles to issues or pull requests.
