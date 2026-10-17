# Proposal

## Why

The app refuses to start whenever the model server's model list can't be read, so while the shared LAN server is
down or restarting, the container crash-loops. Viewing receipts, hand edits and splits don't need the model, but
they go down with it. Once the app has started, nothing watches the model server, and nothing outside the app can
tell whether the app is healthy: there is no health endpoint, and the image has no Docker `HEALTHCHECK`.

## What Changes

- The startup model server check sorts each failure into one of two kinds. **Misconfigured** covers settings that
  are wrong: the API key rejected, a base URL that isn't llama-server or isn't a valid URL, a model the server
  doesn't offer. **Unavailable** covers a server that is down, restarting, loading a model or not answering.
  Misconfigured still logs one fatal line and exits with code 1. Unavailable now logs a warning, and the app
  starts. **BREAKING** for anyone relying on a model server outage to stop a deploy.
- A malformed `Llm:BaseUrl` becomes a Misconfigured result with one fatal log line, instead of an unhandled
  exception.
- A monitor probes the model server's model list while the app runs, every `Llm:MonitorInterval` (30 s by default).
  It starts from the startup check's result and logs only when the state changes.
- A new `GET /health`, needing no sign-in, reports the monitor's last result without contacting the model server.
  It answers `200 Healthy` when the model server is available, `503 Degraded` while it is unavailable, and
  `503 Unhealthy` when it became misconfigured after a good start. The body is only that word.
- The image installs curl and declares a Docker `HEALTHCHECK` against `/health`, so `docker ps` shows the container
  unhealthy whenever receipts can't be read.
- README, HOSTING.md and AGENTS.md describe the new startup rule, the endpoint and how to read the container's
  health.

Out of scope: the client shows nothing new, and receipts uploaded during an outage still end as `Failed`.

## Capabilities

### New Capabilities

- `service-health`: whether the app can do its work: which model server failures stop it from starting, how the
  model server is watched while it runs, and how that is reported through `/health` and the container's health
  check.

### Modified Capabilities

None. `/health` is outside `/api`, so the token requirement in `user-identity` doesn't cover it, and the startup
model server check has no spec yet.

## Impact

- `ReceiptSplit.Extraction`: `ModelServerCheck` gains the classification and the seeding, plus a new monitor,
  state and health check, all registered by `AddExtraction()`. `LlmOptions` gains `MonitorInterval`.
- `ReceiptSplit` host: `Program.cs` maps `/health` anonymously with Degraded answering 503, and keeps the exit on
  Misconfigured.
- `Dockerfile`: installs curl in the runtime stage and adds `HEALTHCHECK`. The image gains about 15 Ubuntu packages
  (curl, libcurl and its Kerberos, LDAP, SASL and SSH libraries).
- Tests: `ModelServerCheckTests`, `StartupTests` and new monitor and endpoint tests. `FakeLlamaClient`'s
  `ListFailure` can already simulate each failure.
- Docs: README, HOSTING.md, AGENTS.md, possibly a comment in `compose.yaml`.
- No database change and no new NuGet packages: health checks are in the shared framework.
