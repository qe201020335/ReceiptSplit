# Design

## Context

- `ModelServerCheck` (Extraction) lists the models with `ILlamaClient.ListModelsAsync` under a 30 s timeout and
  returns a bool. `CheckModelServerAsync()` resolves it in a scope, and `Program.cs` exits with 1 on false. Any
  `HttpRequestException` or `JsonException`, and the timeout, count as a failure. Other exceptions escape. That
  includes the `UriFormatException` from `new Uri(llm.BaseUrl…)` in the typed client's configuration, which runs
  when the client is first resolved, so a malformed `Llm:BaseUrl` crashes with an unhandled exception.
- `LlamaClient.EnsureSuccessAsync` throws `HttpRequestException` with `StatusCode` set for any non-success answer.
  Connection failures carry `HttpRequestError` (.NET 8+): `ConnectionError`, `NameResolutionError`,
  `SecureConnectionError`, `HttpProtocolError`, `InvalidResponse`, `ResponseEnded`, `Unknown` and others.
- The fallback authorization policy requires the `UserId` claim, so every endpoint needs an account unless it is
  marked `AllowAnonymous()`, as the static files are. `AccountMiddleware` lets requests without a token through,
  and only answers problems on `/api` paths.
- `mcr.microsoft.com/dotnet/aspnet:10.0` is Ubuntu 24.04 with bash and `perl-base`, but no curl, wget or nc, and
  no `HTTP::Tiny`. It sets `ASPNETCORE_HTTP_PORTS=8080`. CI builds the image with `cache-from: type=gha`.
- Health checks (`Microsoft.Extensions.Diagnostics.HealthChecks`) are in the shared framework that Extraction
  already references.
- `FakeLlamaClient.ListFailure` makes `ListModelsAsync` throw, and `Models` sets what it lists.
  `ManualTimeProvider` moves only the clock and has no timers.

## Goals / Non-Goals

**Goals:**
- One place sorts a model list result into available, unavailable or misconfigured, and both the startup check
  and the monitor use it.
- `/health` answers from memory: an anonymous request can never make the app contact the shared model server.
- Docker's view matches `/health`'s status code, with no probe logic beyond the status.

**Non-Goals:**
- Pausing the extraction queue while the model server is down, or feeding extraction failures into the state.
- Checking the database. SQLite opens or creates the file, so a connection check proves nothing. Disk-full and
  permission failures show only on a write, which a probe shouldn't make.
- A detailed health response (per-check JSON, reasons, timings).
- Changing what happens to a receipt uploaded during an outage.

## Decisions

### Three states, one classification
`ModelServerCheck` returns a result with the state (`Available`, `Unavailable`, `Misconfigured`) and a reason for
the log. It replaces the bool. Classification:

| Outcome of `GET /v1/models` | Signal | State |
|---|---|---|
| A model list naming `Llm:Model` by id or alias | — | Available |
| Connection refused, reset, host unreachable | `HttpRequestError.ConnectionError` | Unavailable |
| Name doesn't resolve | `NameResolutionError` | Unavailable |
| Dropped mid-response | `ResponseEnded` | Unavailable |
| Network error of no known kind | `Unknown`, or any other `HttpRequestError` not listed below | Unavailable |
| No answer within 30 s | the check's own timeout | Unavailable |
| 5xx (llama-server's 503 while loading a model, a proxy's 502/504) | `StatusCode` | Unavailable |
| 408, 429 | `StatusCode` | Unavailable |
| TLS failure | `SecureConnectionError` | Misconfigured |
| Not HTTP | `HttpProtocolError`, `InvalidResponse` | Misconfigured |
| 401, 403 | `StatusCode` | Misconfigured |
| Any other 4xx | `StatusCode` | Misconfigured |
| A body that isn't a model list | `JsonException` | Misconfigured |
| A list without `Llm:Model` | — | Misconfigured |
| `Llm:BaseUrl` isn't an absolute http/https URL | validated before the request | Misconfigured |

DNS failures and unknown errors count as Unavailable. Failing a deploy on a DNS hiccup brings back the crash loop
this change removes, and a hostname typo still shows: the warning log names it, and the container stays unhealthy.
The default leans the same way for errors we can't classify.

Alternative considered: sorting failures by matching exception messages. `HttpRequestError` and `StatusCode` are
typed and don't change between runtime versions.

### `Llm:BaseUrl` is validated before the client is resolved
The check validates `Llm:BaseUrl` with `Uri.TryCreate(…, UriKind.Absolute, …)` and an http/https scheme before it
resolves `ILlamaClient`, and returns Misconfigured with a reason naming the setting. That avoids the
`UriFormatException` from the client's configuration lambda. It also catches a value like `localhost:8000`, which
parses as a URI with the scheme `localhost`. The lambda stays as it is: once the check has passed, it can't throw.

### The startup check seeds the monitor's state
A singleton `ModelServerState` holds the latest result. `CheckModelServerAsync()` runs the check once, stores the
result in the state, and returns false only for Misconfigured. `Program.cs` keeps exiting with 1 on false. The
monitor, a `BackgroundService`, first waits one interval and then runs the same check on every tick
(`PeriodicTimer` over the registered `TimeProvider`), storing each result.

So there is no Unknown state. Kestrel can't serve `/health` before the startup check has finished, so the state
is always set by then. And the model server is asked once at boot, not twice.

The alternative, a monitor that probes immediately on start, needs a fourth state for the gap between Kestrel
starting and the first result (up to the 30 s timeout), and a rule for what `/health` answers during it.

`AddExtraction()` registers the state, the monitor, the health check and `ModelServerCheck` as a singleton (today
it is scoped, but everything it uses is a singleton or the typed client). `TimeProvider.System` is added with
`TryAddSingleton`, as Accounts does.

### Logging only on change
The state compares each new result with the previous one and logs only when the state differs. Reasons within one
state aren't compared, so an outage whose error text changes doesn't log again.

Levels:
- Critical: Misconfigured at startup, the one fatal line before exiting. Unchanged.
- Warning: Unavailable at startup ("starting without the model server: …"), and Available → Unavailable.
- Error: any change to Misconfigured after startup.
- Information: any change to Available, including the startup line logged today.

### `/health` maps the state, answering 503 for Degraded
`ModelServerHealthCheck : IHealthCheck` reads the state: Available → `Healthy`, Unavailable → `Degraded`,
Misconfigured → `Unhealthy`. It never calls the model server. `Program.cs` maps
`app.MapHealthChecks("/health", …).AllowAnonymous()` with `ResultStatusCodes[Degraded] = 503`, because Degraded's
default is 200. The default response writer writes only the status word as `text/plain`, which is the body the
spec asks for.

Degraded and Unhealthy both answer 503, so any HTTP monitor sees what Docker sees. The words still differ, so the
probe log can tell an outage from a broken setting.

`/health` sits outside `/api`, so the account middleware's problem answers and the `/api` 404 fallback don't apply,
and the explicit route wins over `MapFallbackToFile`. Behind Cloudflare Access the path still asks for a sign-in.
An outside monitor would need an Access bypass or a service token for it. The Docker probe goes to localhost and
needs neither.

### The Docker health check uses curl
In the `base` stage, as root before `USER $APP_UID`:

```dockerfile
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --start-interval=5s --retries=3 \
    CMD curl -sS --fail-with-body "http://localhost:${ASPNETCORE_HTTP_PORTS:-8080}/health" || exit 1
```

- `--fail-with-body` (curl 7.76+, the image has 8.5) fails on 503 but still prints `Degraded` or `Unhealthy`.
  Plain `-f` drops the body, so the health log would lose the word.
- `|| exit 1`, because curl exits 22 on an HTTP failure and Docker reserves exit code 2.
- Shell form, so `ASPNETCORE_HTTP_PORTS` is read when the probe runs. A value listing several ports isn't handled.
  The image sets one.
- `--start-period=60s` covers migrations plus the startup check's 30 s timeout. `--start-interval` (Engine 25+;
  older engines ignore it) shows a healthy container within seconds of starting.

Alternatives considered:
- `dotnet ReceiptSplit.dll --health`: no new packages, but a branch in `Program.cs`, and a .NET start every 30 s.
- bash `/dev/tcp`: free, but hand-written HTTP that can't be tested.
- A separate AOT probe project: a whole project for one GET.

## Risks / Trade-offs

- [A hostname typo in `Llm:BaseUrl` now starts the app instead of failing the deploy] → The warning log names the
  DNS failure, `docker ps` shows the container unhealthy, and `/health` answers `Degraded`. HOSTING.md says what
  each sign means.
- [Receipts uploaded while the model server is down still end as `Failed`] → Out of scope, and unchanged from
  today, where the server can go down after startup anyway. HOSTING.md tells admins to run extraction again once
  `/health` is healthy.
- [Docker learns about a change late: up to one monitor interval plus `retries` × 30 s, about two minutes] →
  Acceptable for a household app, and `Llm:MonitorInterval` can be lowered.
- [curl adds about 15 packages, which image scanners may flag] → They update whenever the base image is
  republished, like the rest of the OS. Nothing in the container calls anything but localhost with them.
- [A model server that lists models but fails completions looks healthy] → `/v1/models` doesn't load a model, by
  design, so the shared server isn't loaded every 30 s. Extraction failures still show on the receipt.
- [`/health` behind an Access bypass tells anyone whether the model server is up] → The body is one word, with no
  URL or error. HOSTING.md leaves the bypass optional.

## Migration Plan

There's no data change. Deploying the new image changes behaviour only when the model server is down at startup:
the container starts unhealthy instead of restarting. To roll back, pin the previous `sha-<commit>` image with
`RECEIPTSPLIT_IMAGE`.
