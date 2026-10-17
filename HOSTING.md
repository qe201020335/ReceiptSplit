# Hosting ReceiptSplit

Deploying needs only `compose.yaml` and a `.env` file next to it: the image is built and published by CI, and the
compose file's comments list the other settings, such as the model server. The app runs behind Cloudflare Access.

## Cloudflare Access

The app doesn't sign people in itself. It runs behind a [Cloudflare Access][access] self-hosted application,
reached through `cloudflared`, and checks the signed token Access adds to every request, refusing any without
one. It won't start without these settings, set in the `.env` file next to `compose.yaml`:

| `.env` | Setting | What it is |
|---|---|---|
| `ACCESS_TEAM_DOMAIN` | `Auth:CloudflareAccess:TeamDomain` | The team domain, as `myteam.cloudflareaccess.com` |
| `ACCESS_AUD` | `Auth:CloudflareAccess:Audience` | The Access application's Audience (AUD) tag |
| `RECEIPTSPLIT_ADMIN` | `Auth:Admins:0` | The admin's email; add more in `compose.yaml` as `Auth__Admins__1` |

Zero Trust shows the team domain under Settings, and the AUD tag on the application's page under Access.

Everyone gets an account of their own on their first sign-in, and sees only their own receipts. Admins see every
receipt, including those uploaded before there were accounts. Accounts are linked to the Google account people
sign in with, personal or Workspace, so turn on Google as the Access application's login method; the app refuses
other methods with a page saying so.

**Keep the port off the network.** `compose.yaml` publishes the app on `127.0.0.1`, for `cloudflared` running on
the same host. Docker's published ports go around host firewalls such as ufw, so publishing on every interface
would let anything on the network reach the app directly. The token check still refuses such requests, but there
is no reason to offer them. If `cloudflared` runs elsewhere, set `RECEIPTSPLIT_BIND` to the address it reaches.

Optionally, `cloudflared` can check the token too, and drop requests without one before they reach the app:

```yaml
ingress:
  - hostname: receipts.example.com
    service: http://localhost:8080
    originRequest:
      access:
        required: true
        teamName: myteam
        audTag: [<AUD tag>]
```

**When someone's email belongs to another account.** If a person signs in with a Google account whose email
another account here already has (for example, a new Google account reusing an old address), the app refuses
them rather than hand them that account's receipts. An admin can release the email from the account holding it,
which keeps its receipts. Signed in as an admin, run this in the browser console on the app's page:

```js
await fetch('/api/users/release-email', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'person@example.com' }),
}).then((response) => response.status) // 204 released, 404 no account has that email
```

Within a minute the person can sign in again and gets a new account. To give them the old account's receipts,
open Manage receipts, filter by the old account, select its receipts and reassign them to the new one. If the only admin is the one refused, stop
the app and clear the email in the database by hand, then start it again:

```sh
sqlite3 data/receiptsplit.db "UPDATE Users SET Email = NULL WHERE Email = 'admin@example.com';"
```

## Health

The app checks the model server when it starts and every 30 seconds after (`Llm__MonitorInterval`), and the image's
health check asks the app for the result. `docker ps` shows the container as healthy while receipts can be read,
and unhealthy while they can't. Docker only marks the container: it doesn't restart it, and the app keeps serving
receipts, edits and splits.

To see why a container is unhealthy:

```sh
docker inspect --format '{{json .State.Health}}' <container>
```

Its log ends with what the app answered:

| Answer | Meaning | What to do |
|---|---|---|
| `Healthy` | The model server answers and offers the model | Nothing |
| `Degraded` | The model server is down, restarting or not answering | Wait; the container turns healthy on its own once the server answers |
| `Unhealthy` | The model server's settings stopped working after a good start, such as an API key changed on the server | Read `docker compose logs`, which name the problem once, then fix the settings |

**A container that keeps restarting** has model server settings that are wrong from the start: the server rejects
the API key, `LLM_BASE_URL` isn't llama-server, or the model isn't offered. The app logs one fatal line saying which
and exits, and `restart: unless-stopped` starts it again. `docker compose logs` shows the line. A server that is
only down doesn't stop the app.

Receipts uploaded while the model server is down end as failed. Once the container is healthy, run extraction on
them again.

The app answers the health check at `/health` without a sign-in, so Docker's probe can reach it inside the
container. Through Cloudflare, Access still asks for a sign-in on that path like any other. An outside monitor would
need an Access bypass policy for `/health` or a service token. The answer is only the one word, with no URL or error
text.

[access]: https://developers.cloudflare.com/cloudflare-one/applications/configure-apps/self-hosted-public-app/
