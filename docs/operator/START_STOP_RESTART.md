# Axon start, stop, and restart procedures

## Supported v0.1.0 cold start

1. Power on Windows and sign in with the Windows account that installed Axon.
2. Start Docker Desktop and wait until its Linux engine reports running.
3. Confirm the `axon` container group is running or start the group in Docker
   Desktop.
4. Open the `Axon Control` Desktop shortcut at `http://127.0.0.1:8780`.

The containers use `restart: unless-stopped`, so they normally return when the
Docker engine starts. Axon v0.1.0 intentionally does not install experimental
Desktop operations launchers.

## GUI behavior

Axon Control is a Windows host process managed by the scheduled task
`Axon Control Panel`. It is not one of the Docker containers.

- `Pause all services` stops PostgreSQL, Synapse, and nginx.
- The GUI process remains running.
- `Start all` starts the three containers and waits for health.
- `Restart all` restarts the three containers.
- Individual service controls are diagnostic tools.
- There is no normal GUI button that stops Axon Control itself.

If an administrator is already signed in, the GUI can continue displaying
status and lifecycle controls while Synapse is stopped. Do not sign out while
Synapse is stopped: new GUI authentication currently depends on the local
Synapse administrator login endpoint.

## Host-only control recovery

If `127.0.0.1:8780` does not load, open Task Scheduler, select `Task Scheduler
Library`, right-click `Axon Control Panel`, and choose `Run`. If it already
reports running, choose `End` and then `Run`.

If login reports `Failed to fetch` or `Synapse is unavailable`, start the
complete `axon` group in Docker Desktop before logging in. Authentication
depends on local Synapse.

## Raw recovery commands

If the menu is unavailable:

```powershell
Start-ScheduledTask -TaskName "Axon Control Panel"

docker compose `
    --project-name axon `
    --env-file "$env:ProgramData\Axon\.env" `
    --file ".\deploy\compose.yaml" `
    up --detach --wait
```

The Windows address in `%ProgramData%\Axon\.env`, the Windows NIC, router
forward, firewall rules, and Element URL must remain aligned. Use a DHCP
reservation or static address for predictable cold starts.
