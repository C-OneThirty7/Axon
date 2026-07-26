# Axon v0.1.0 security and hardening audit

## Executive summary

Axon v0.1.0 is suitable for the stated proof-of-concept trust model: one
administrator-controlled Windows 11 host, a closed/private client network,
text-only Matrix messaging, no federation, and no internet dependency.

No known critical or high-severity code finding remains open inside that trust
model. The principal residual risk is intentional cleartext HTTP between
Element clients and Axon. Anyone able to observe or modify that LAN can capture
Matrix credentials and access tokens despite room E2EE. This release must not
be placed on an untrusted, shared, or internet-facing network.

The repository is primarily C#, PowerShell, JavaScript, YAML, and nginx
configuration. The available language-specific security reference did not
cover C# or PowerShell, so this review used general secure-design principles
and the current primary Synapse, Docker Desktop, and WSL documentation.

## Fixed findings

### AXSEC-001 - Forwarded client addresses could be spoofed

- Severity before fix: High
- Impact: A client-supplied `X-Forwarded-For` value could influence Synapse's
  view of the source address and weaken address-based login throttling.
- Resolution: nginx now replaces the header with its observed remote address
  rather than appending untrusted input.
- Evidence: `deploy/nginx/default.conf.template`, lines 21-38.

### AXSEC-002 - Client-created rooms were not encrypted by server default

- Severity before fix: High
- Impact: A stock client or operator mistake could create an unencrypted local
  room even though Axon is intended to be E2EE-first.
- Resolution: all locally-created rooms are encrypted by default.
- Evidence: `deploy/synapse/homeserver.yaml.template`, line 40.

### AXSEC-003 - Unneeded Synapse features and egress work remained enabled

- Severity before fix: Medium
- Impact: Push calculation, public room search, and permissive federation
  defaults added unnecessary behavior and traffic to a closed deployment.
- Resolution: push processing, room-list search, public directories, profile
  lookup over federation, and federation destinations are explicitly disabled.
- Evidence: `deploy/synapse/homeserver.yaml.template`, lines 32-54.

### AXSEC-004 - Axon Control lacked explicit browser hardening

- Severity before fix: Medium
- Impact: The host-only UI relied on browser defaults and SameSite cookies
  without a content policy or explicit cross-origin mutation check.
- Resolution: Axon Control remains bound to loopback and now adds CSP,
  anti-framing, no-sniff, no-referrer headers, plus rejects unsafe API requests
  carrying a non-loopback Origin.
- Evidence: `src/Axon.Control/Program.cs`, lines 47-95.

### AXSEC-005 - GUI-issued stock passwords accepted six characters

- Severity before fix: Medium
- Impact: Short shared stock passwords were easier to guess or reuse.
- Resolution: individual and batch GUI issuance now requires 10-256
  characters and no longer pre-populates a weak six-character password.
- Evidence: `src/Axon.Control/Matrix/SynapseAdminClient.cs`,
  `src/Axon.Control/wwwroot/index.html`.

### AXSEC-006 - Offline payload provenance and runtime secrets

- Severity: Pass
- Evidence: The release builder resolves immutable container digests, exports
  Linux/AMD64 images under digest-derived local tags, writes SHA-256 coverage
  for every payload, and Compose forbids pulls. On Windows, the verifier also
  rejects a missing or hash-mismatched Authenticode signature on the bundled
  Docker Desktop and WSL installers. Runtime secrets are generated locally and
  protected to the installing user, SYSTEM, and Administrators.
- Evidence: `scripts/Build-OfflineBundle.ps1`,
  `src/Axon.Control/Installation/RuntimeRenderer.cs`, and
  `deploy/compose.yaml`, lines 1-94.

### AXSEC-007 - Network and management exposure

- Severity: Pass
- Evidence: LAN exposure is limited to the selected host address on TCP 80.
  PostgreSQL is not published. Synapse administration is published only on
  loopback TCP 8008, Axon Control listens only on loopback TCP 8780, and nginx
  returns 404 for the Synapse Admin API.
- Evidence: `deploy/compose.yaml`, lines 9-76;
  `deploy/nginx/default.conf.template`, lines 17-19; and
  `src/Axon.Control/Program.cs`, line 48.

## Residual risks and accepted constraints

### AXRISK-001 - Cleartext HTTP on the client LAN

- Severity outside the closed-network model: High
- Status: Explicitly accepted for v0.1.0.
- E2EE protects message plaintext from Synapse, but HTTP login credentials,
  access tokens, user IDs, room IDs, timing, and encrypted event payloads are
  visible to a LAN observer. Use physical/network access control now. A private
  CA and `https://axon.home.arpa` are the planned upgrade if the trust boundary
  expands.

### AXRISK-002 - Docker and Windows administrator authority

- Severity: High
- Status: Accepted operational trust.
- A Windows administrator or account with Docker daemon access can control
  containers and read local runtime state. Axon does not protect against a
  compromised host administrator.

### AXRISK-003 - Temporary encrypted server storage

- Severity: Medium
- Status: Required for offline delivery.
- Synapse stores encrypted events and routing metadata so disconnected clients
  can synchronize. The configured maximum lifetime is 48 hours and purge jobs
  run hourly; deletion is not instantaneous and database artifacts may persist
  until Synapse completes its purge work.

### AXRISK-004 - Control login depends on Synapse

- Severity: Operational
- Status: Documented.
- A signed-out operator cannot authenticate to Axon Control while Synapse is
  stopped. Recovery is to start the `axon` group in Docker Desktop, then log in.
  The control task itself can be restarted from Windows Task Scheduler.

### AXRISK-005 - No internet push notification path

- Severity: Functional
- Status: Intended.
- Synapse push processing is disabled. Clients synchronize while active or
  after reconnecting; background alerts are not guaranteed without an
  explicitly designed local push service.

## Release validation requirements

Before distributing v0.1.0:

1. All automated tests and PowerShell parser checks must pass.
2. Docker Compose must validate with `pull_policy: never`.
3. The rendered Synapse configuration must pass `python -m synapse.config`
   using the bundled Synapse version.
4. Every PDF page must be rendered and visually inspected.
5. The final ZIP must be extracted independently and every `SHA256SUMS` entry
   recomputed.
6. The ZIP filename and manifest must identify Axon v0.1.0 and Windows x64.
