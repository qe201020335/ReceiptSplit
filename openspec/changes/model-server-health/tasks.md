# Tasks

Each numbered group is one commit on the branch `model-server-health`. Every commit builds and passes `dotnet test`,
and `CI=true dotnet build ReceiptSplit.slnx` stays free of warnings. The client doesn't change.

## 1. Sort model server failures into unavailable and misconfigured

- [ ] 1.1 Replace `ModelServerCheck`'s bool with a result holding the state (`Available`, `Unavailable`,
  `Misconfigured`) and a reason for the log, classified as in the design's table. Check `Llm:BaseUrl` (absolute,
  http or https) before resolving `ILlamaClient`, and make `ModelServerCheck` a singleton. Verify with
  `ModelServerCheckTests`, one test per row of the table, using `FakeLlamaClient.ListFailure` with
  `HttpRequestException(HttpRequestError…, …, statusCode: …)`, a `JsonException`, and a `TaskCanceledException`
  for the timeout. Include malformed URLs (`not a url`, `localhost:8000`, `ftp://host`) and the existing
  router-mode alias test.
- [ ] 1.2 Make `CheckModelServerAsync()` return false only for Misconfigured. It logs Critical for Misconfigured,
  a warning that the app starts without the model server for Unavailable, and Information for Available. Verify
  with `StartupTests`:
  - the existing "model not offered" case still fails to start
  - a `ConnectionError` from `ListFailure` starts, and `GET /api/receipts` answers 200
  - a 503 from `ListFailure` starts
  - `Llm:BaseUrl` set to `not a url` fails to start without an unhandled exception
- [ ] 1.3 Update README's "Running it" line to say the app won't start when the model server's settings are wrong
  (key rejected, URL not llama-server, model not offered), but starts when the server is only unreachable. Update
  AGENTS.md's **Startup checks** bullet to match. Verify by reading both against the design's table.

## 2. Watch the model server while the app runs

- [ ] 2.1 Add a singleton `ModelServerState` holding the latest result. It logs only when the state changes:
  Warning for a change to Unavailable, Error for one to Misconfigured, Information for one to Available.
  `CheckModelServerAsync()` stores its result there, as the first state. Verify with new `ModelServerStateTests`
  using `ListLogger`:
  - three Unavailable results in a row log one warning
  - Unavailable → Available logs one information line
  - Available → Misconfigured logs one error
  - the same state with a different reason logs nothing
- [ ] 2.2 Add `Llm:MonitorInterval` (default 30 s) to `LlmOptions`. Add the `ModelServerMonitor` background
  service, which waits one interval, then runs the check on every tick (`PeriodicTimer` over `TimeProvider`) and
  stores the result. Register it, and `TimeProvider.System` with `TryAddSingleton`, in `AddExtraction()`. Verify
  with an API test through `ReceiptApiFactory`, with `Llm:MonitorInterval` set to 100 ms: after the app starts
  with the model available, setting `ListFailure` to a `ConnectionError` makes the state Unavailable within a few
  seconds, and the app keeps serving `GET /api/receipts`.
- [ ] 2.3 Mention the monitor in AGENTS.md: in the Layout line for Extraction ("model server check and monitor")
  and in the **Startup checks** bullet (the startup result seeds the monitor, which never exits the app). Verify
  by reading.

## 3. Report the state at /health

- [ ] 3.1 Add `ModelServerHealthCheck : IHealthCheck` reading `ModelServerState` (Available → Healthy, Unavailable
  → Degraded, Misconfigured → Unhealthy), registered with `AddHealthChecks()` in `AddExtraction()`. Map
  `app.MapHealthChecks("/health", …).AllowAnonymous()` in `Program.cs` with Degraded answering 503. Add a call
  count for `ListModelsAsync` to `FakeLlamaClient`. Verify with new `HealthTests` in `ReceiptSplit.Tests`:
  - available: 200 `Healthy`
  - after a check with a `ConnectionError`: 503 `Degraded`
  - after a check with a 401: 503 `Unhealthy`
  - a request through `CreateClientWithToken` with no token gets the state, not 401
  - ten requests between two checks add no `ListModelsAsync` calls
  - the body is exactly the status word
- [ ] 3.2 Mention `/health` in AGENTS.md's "How the backend works": what each state answers, that it never
  contacts the model server, and that it needs no sign-in. Verify by reading.

## 4. Docker health check

- [ ] 4.1 In the Dockerfile's `base` stage, before `USER $APP_UID`, install curl with `--no-install-recommends`
  and clear the apt lists. Add the `HEALTHCHECK` from the design (curl `--fail-with-body` on
  `localhost:${ASPNETCORE_HTTP_PORTS:-8080}/health`, `|| exit 1`, start period 60 s). Verify with a local build
  (`sg docker -c`) and a container run with `ASPNETCORE_ENVIRONMENT=Development`, `Auth__DevUser` set and a
  scratchpad data folder:
  - with `Llm__BaseUrl` pointing at a closed port, the container starts, turns unhealthy, and `docker inspect`'s
    health log shows `Degraded`
  - with a stub server in the scratchpad answering `/v1/models` with the configured model, it turns healthy
  - stop and remove what was started
- [ ] 4.2 Add a **Health** section to HOSTING.md covering:
  - what healthy and unhealthy mean in `docker ps`, and that Docker only marks the container, never restarts it
  - `docker inspect --format '{{json .State.Health}}'` with `Degraded` (model server down, wait) versus `Unhealthy`
    (settings broke after a good start, read the logs)
  - a restart loop meaning the model server settings are wrong, with the reason in `docker compose logs`
  - that receipts uploaded during an outage fail and can be extracted again once healthy
  - that behind Access, `/health` still needs a sign-in unless the path gets a bypass policy or a service token
    for an outside monitor

  Add a line next to the `Llm__` settings in `compose.yaml` saying which model server problems stop the
  container. Verify by following the section's commands against the container from 4.1.

## Workflow follow-up

- Open a pull request when asked, and archive the change after it merges.
