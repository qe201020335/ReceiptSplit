# Spec Delta

## Purpose

Tells the people running the app whether it can do its work. It decides which model server problems stop the app
from starting, watches the model server while the app runs, and reports the result to Docker and other monitors.

## ADDED Requirements

### Requirement: Wrong model server settings stop the app from starting
At startup the app SHALL check the model server. When the check shows the model server settings are wrong, the
app SHALL log one fatal line saying why and exit with code 1 before serving any request. The settings are wrong
when the API key is rejected, when the base URL isn't a valid http or https URL, when it answers with something
other than a model server, or when the configured model isn't offered by id or alias.

#### Scenario: API key rejected
- **WHEN** the app starts and the model server answers its model list with 401 or 403
- **THEN** it logs that the model server rejected the request and exits with code 1

#### Scenario: Model not offered
- **WHEN** the app starts and the model server's list names neither an id nor an alias equal to the configured
  model
- **THEN** it logs the models the server offers and exits with code 1

#### Scenario: Base URL isn't a model server
- **WHEN** the app starts and the base URL answers with another 4xx status other than 408 or 429, with a body that
  isn't a model list, with something that isn't HTTP, or fails its TLS handshake
- **THEN** it logs why and exits with code 1

#### Scenario: Malformed base URL
- **WHEN** the app starts with a base URL that isn't an absolute http or https URL
- **THEN** it logs that the base URL is invalid and exits with code 1, without an unhandled exception

### Requirement: An unavailable model server doesn't stop the app from starting
When the startup check shows only that the model server is unavailable, the app SHALL log a warning saying why and
start. The model server is unavailable when the connection fails or drops, when its name doesn't resolve, when it
gives no answer within 30 seconds, when it answers 408, 429 or any 5xx status, or when the failure is a network
error of no known kind.

#### Scenario: Server down
- **WHEN** the app starts and the model server refuses the connection
- **THEN** it logs a warning and starts, and receipts can be listed, opened, edited and split

#### Scenario: Server loading a model
- **WHEN** the app starts and the model server answers its model list with 503
- **THEN** it logs a warning and starts

#### Scenario: No answer
- **WHEN** the app starts and the model server doesn't answer within 30 seconds
- **THEN** it logs a warning and starts

### Requirement: The model server is watched while the app runs
While the app runs it SHALL check the model server at a configurable interval, 30 seconds by default, and sort
each result into available, unavailable or misconfigured by the same rules as the startup check. The startup
check's result SHALL be the first state, so the state is known from the first request. The app SHALL NOT exit
because of a result after startup.

#### Scenario: Server goes down
- **WHEN** the model server stops answering after the app started with it available
- **THEN** the state becomes unavailable at the next check, and the app keeps running

#### Scenario: Server comes back
- **WHEN** the app started while the model server was down, and the server then answers with the model offered
- **THEN** the state becomes available at the next check

#### Scenario: Settings break after a good start
- **WHEN** the model server starts rejecting the API key after the app started with it available
- **THEN** the state becomes misconfigured at the next check, and the app keeps running

### Requirement: Model server state changes are logged once
The app SHALL log when the model server's state changes, and SHALL NOT log a check whose result matches the
current state. A change to unavailable SHALL be a warning, a change to misconfigured an error, and a change back
to available an information line. Each log line SHALL say why the model server can't be used.

#### Scenario: Ongoing outage
- **WHEN** the model server stays down across several checks
- **THEN** one warning is logged when it went down, and none for the checks after it

#### Scenario: Recovery
- **WHEN** the model server becomes available after being unavailable
- **THEN** one information line is logged

### Requirement: The health endpoint reports the model server state
`GET /health` SHALL answer `200` with the body `Healthy` when the model server is available, `503` with `Degraded`
when it is unavailable, and `503` with `Unhealthy` when it is misconfigured. The body SHALL be only that word, with
no URL, model name or error text. Answering SHALL NOT contact the model server.

#### Scenario: Available
- **WHEN** the model server is available and `/health` is requested
- **THEN** the response is 200 with the body `Healthy`

#### Scenario: Unavailable
- **WHEN** the model server is unavailable and `/health` is requested
- **THEN** the response is 503 with the body `Degraded`

#### Scenario: Misconfigured after start
- **WHEN** the model server became misconfigured after the app started and `/health` is requested
- **THEN** the response is 503 with the body `Unhealthy`

#### Scenario: Requests don't reach the model server
- **WHEN** `/health` is requested many times between two checks
- **THEN** the model server receives no requests from them

### Requirement: The health endpoint needs no sign-in
`/health` SHALL answer requests without an Access token or an account, so a probe on the same host can reach it.

#### Scenario: Request without a token
- **WHEN** `/health` is requested without an Access token
- **THEN** it answers with the model server state, not 401

### Requirement: The container reports the app's health
The container image SHALL declare a health check that requests `/health` on the app's own port, and counts the
container as healthy only when the answer is 200. The probe's output SHALL include the response body, so the
container's health log shows whether the state is degraded or unhealthy.

#### Scenario: Model server down
- **WHEN** the container runs and `/health` answers 503 `Degraded` for longer than the health check's retries
- **THEN** Docker reports the container as unhealthy, and its health log shows `Degraded`

#### Scenario: Model server back
- **WHEN** `/health` answers 200 again
- **THEN** Docker reports the container as healthy at its next probe
