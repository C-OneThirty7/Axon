# Element onboarding

## HTTP proof of concept

1. Connect the client to the Axon LAN.
2. Confirm the environment-specific Axon address on TCP port 80 is reachable.
3. Open Element or Element X and choose **Sign in**.
4. Edit/change the homeserver.
5. Enter the exact URL issued by the operator, for example `http://192.168.0.113`.
6. Enter the Axon-created username and password.

The Matrix user ID remains `@username:axon.home.arpa` even though the connection URL is an IP address.

Stock client policy varies. Element Desktop was proven to accept HTTP on loopback during the Mac POC. If a current Windows, Android, or iOS build rejects HTTP on the LAN address, record the exact application version and error. The next step is Axon's private-CA profile using `https://axon.home.arpa`; do not weaken operating-system trust controls or begin custom Neural development solely to bypass the error.

## First messaging test

1. Create two non-admin users.
2. Sign in on two clients.
3. Create a private encrypted room on client A.
4. Invite client B by its full Matrix ID.
5. Exchange text in both directions.
6. Disconnect B, send from A, reconnect B within 48 hours, and confirm delivery.

No internet push notification is expected. Clients receive changes while connected or when they reconnect and synchronize.

After the first login, change an issued stock password under Element/Element X account or security settings. Stock clients do not enforce this automatically.

Axon v0.1.0 disables Synapse push processing, public room search, federation,
media, presence, and URL previews. Rooms created by local clients are encrypted
by default. These controls reduce unnecessary traffic and exposure but do not
turn HTTP into encrypted transport: use Axon only on the trusted closed network
described by the deployment plan.
