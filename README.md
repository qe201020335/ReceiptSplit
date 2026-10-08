# ReceiptSplit

> [!WARNING]
> This is completely vibe-coded. I (qe201020335) have not written a single line of code in this project.
>
> Use at your own risk!

A web app for splitting a shopping receipt between people. Upload a photo of the receipt, a local vision model
reads the lines, and the app checks them against the receipt's own subtotal, tax and total. Anything misread can be
corrected by hand. Once the receipt checks out, give each item to whoever pays for it, by shares or by dollar
amounts, and copy a summary of who owes what.

It has been tuned on receipts from Costco and T&T but should work with any generic receipt.

## Stack

- ASP.NET Core 10 with EF Core and SQLite, logging to the console with Serilog
- React, TypeScript and Vite with Mantine
- An OpenAI-compatible [llama.cpp](https://github.com/ggml-org/llama.cpp) server with a vision model

## Running it

With the .NET 10 SDK and Node.js installed, and the model server set in `src/ReceiptSplit/appsettings.json`:

```sh
dotnet run --project src/ReceiptSplit
```

Then open <http://localhost:5173>. In Development every request is signed in as `dev@example.com`, an admin
(`Auth:DevUser` and `Auth:Admins` in `appsettings.Development.json`). To host it, use `compose.yaml`, behind
Cloudflare Access as below. The app won't start unless the model server answers and offers the configured model.

## Hosting behind Cloudflare Access

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

Within a minute the person can sign in again and gets a new account. If the only admin is the one refused, stop
the app and clear the email in the database by hand, then start it again:

```sh
sqlite3 data/receiptsplit.db "UPDATE Users SET Email = NULL WHERE Email = 'admin@example.com';"
```

[access]: https://developers.cloudflare.com/cloudflare-one/applications/configure-apps/self-hosted-public-app/
