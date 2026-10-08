# Design

## Context

See proposal.md (Why). Current state that shapes the approach:

- **No authentication.** `Program.cs` calls `UseAuthorization()`, but no authentication is registered.
  `ReceiptsController` reads `AppDbContext` directly to list and get receipts. Every other action goes through
  `ReceiptService`, which other entry points (a future Discord bot) are meant to reuse.
- **The worker needs no user.** `ExtractionWorker` and `ReceiptExtractor` work on receipt ids in the background.
- **The deployment.** The app runs in Docker from `compose.yaml`, with cloudflared installed on the host. The
  owner's Access application signs people in with Google. Its get-identity output shows these fields:
  - `id`: Google's stable account id
  - `name`
  - `email`
  - `idp.type`: `google`
  - `user_uuid`: Cloudflare's own id
  - session data that we don't want

  Its Access token carries these claims:
  - `iss`: the team domain, `https://<team>.cloudflareaccess.com`
  - `aud`: a list holding the application's AUD tag
  - `email`
  - `identity_nonce`
  - `sub`: the same value as `user_uuid`
  - `exp`: 24 hours after `iat`, the application's session length
  - `country`, `policy_id` and an internal field, none of them used
- **Tests.** `WebApplicationFactory` runs the app in the Development environment, so `appsettings.Development.json`
  applies to tests. `ReceiptApiFactory` already overrides settings this way (`Storage:Root`, `Llm:Model`).
- **No user-facing API depends on the bulk delete response** except `ReceiptManager`.
- **Project structure.** The backend is split into:
  - `src/ReceiptSplit`, a thin host (`Program.cs`, controllers, DTOs)
  - `src/ReceiptSplit.Receipts`: `ReceiptService`, the extractor, worker and checks
  - `src/ReceiptSplit.Data`: every entity, `AppDbContext`, the migrations and `Precision`
  - `src/ReceiptSplit.Extraction`: the model client and parser
  - per-library test projects under `tests/`, sharing `tests/ReceiptSplit.Testing`

  Each library registers its services with an `Add…` extension on `IHostApplicationBuilder` (`AddDatabase`,
  `AddExtraction`, `AddReceipts`) that calls the ones it depends on first. Types are internal unless another
  production project uses them, package versions live in `Directory.Packages.props`, and warnings are errors in
  CI. The root `Dockerfile` copies every `src/*/*.csproj` with `COPY --parents` before restoring, so a new
  library needs no Dockerfile change.

## Goals / Non-Goals

**Goals:**
- Only one component knows about Cloudflare: token verification and the identity lookup. Accounts, admins,
  ownership and the client depend only on "a verified email plus an optional external identity".
- No network call on the request path after a session's first request.
- The migration only adds things and keeps the existing data.

**Non-Goals:**
- A generic signed-JWT handler for other identity-aware proxies, or providers other than Google.
- Profile photos, user management screens, showing owners, reassigning receipts.
- Roles stored in the database. Admins come from configuration.
- Running several app instances. The identity cache is in-memory, which suits one instance on SQLite.

## Decisions

### Two account libraries, with Cloudflare at the edge
The account code lives in two new projects under `src/`, beside the existing libraries:

```
 ReceiptSplit (host) --+--> ReceiptSplit.Receipts ----------------+--> ReceiptSplit.Data
                       |        |      |                          |
                       |        |      +--> ReceiptSplit.Extraction
                       |        v                                 |
                       +--> ReceiptSplit.Accounts ----------------+
                       |        ^
                       +--> ReceiptSplit.Accounts.CloudflareAccess
```

- **The database tables belong to `ReceiptSplit.Data`**, as every other table does: `User`, `ExternalIdentity`,
  `IdentityProvider`, their configuration in `AppDbContext`, `Receipt.OwnerId` and the `AddUsers` migration.
  `AccountService` uses `AppDbContext` directly, as `ReceiptService` does.
- **`ReceiptSplit.Accounts`** holds the account logic that doesn't depend on the proxy:
  - `AccountService` and `IIdentityLookup`
  - the account middleware and its cache
  - `Actor`, the claim names, the `Admin` policy, `AuthOptions` and development sign-in
  - `AccountSettingsCheck`
  - `AddAccounts()` on `IHostApplicationBuilder`, which calls `AddDatabase()` first and does nothing when called
    again, as the other libraries' `Add…` methods do
  - `UseAccounts()` on the application, which adds the account middleware to the pipeline

  It references `ReceiptSplit.Data` and the ASP.NET Core shared framework, but nothing from Cloudflare.
- **`ReceiptSplit.Accounts.CloudflareAccess`** holds:
  - the JWT bearer setup and the key-set retriever
  - `CloudflareIdentityLookup`
  - `CloudflareAccessOptions` (`Auth:CloudflareAccess`) and `CloudflareAccessCheck`
  - `AddCloudflareAccess()` on `IHostApplicationBuilder`, which calls `AddAccounts()` first

  It's the only project that references `Microsoft.AspNetCore.Authentication.JwtBearer`, and it doesn't touch the
  database.
- **`ReceiptSplit.Receipts`** references `ReceiptSplit.Accounts` only for `Actor`. Accounts never references
  Receipts.
- **The host** wires both libraries in `Program.cs` and owns `UsersController` and `AccountDto`. A later proxy
  would be another library beside the Cloudflare one, and `ReceiptSplit.Accounts` wouldn't change.
- **Visibility:** following the internal-by-default rule, the public surface is what the host and Receipts use:
  - `AddAccounts`, `AddCloudflareAccess` and `UseAccounts`
  - `Actor` and how to read it from claims
  - `AccountService`'s release-email and current-user operations
  - `CheckAccountSettingsAsync()` and `CheckCloudflareAccessAsync()` on `IServiceProvider`, which run the startup
    checks the way `CheckModelServerAsync()` does

  Everything else is internal.
- **Tests** follow the project's convention of one test project per library:
  - `tests/ReceiptSplit.Accounts.Tests` covers the resolution rules, against `AppDbContext` over in-memory SQLite.
  - `tests/ReceiptSplit.Accounts.CloudflareAccess.Tests` covers the lookup parsing and the Cloudflare check.
  - The API tests stay in `ReceiptSplit.Tests`.
  - Helpers that more than one project needs, such as `FakeIdentityLookup` and the token signer, go in
    `ReceiptSplit.Testing`.

*Alternative:* the account entities in `ReceiptSplit.Accounts`, exposed through an `IAccountsDb` interface that
`AppDbContext` implements. Rejected, because Data owns every table. It also gives
`Receipt.OwnerId` an ordinary foreign key with nothing in between.

### Verifying the Access token with the JWT bearer handler
The `Microsoft.AspNetCore.Authentication.JwtBearer` handler is configured as follows:
- **Token source:** `OnMessageReceived` reads the token from `Cf-Access-Jwt-Assertion`.
- **What's checked:**
  - the issuer is the team domain (`https://<team>.cloudflareaccess.com`)
  - the audience is the application's AUD tag
  - the algorithm is RS256
  - the expiry is enforced
- **Claims:** `MapInboundClaims = false`, so claims keep their JWT names (`email`, `identity_nonce`).
- **Keys:** Cloudflare publishes them at `<team>/cdn-cgi/access/certs`, but serves no OpenID discovery document
  for this kind of app. A small `IConfigurationRetriever` reads that key set into the handler's
  `ConfigurationManager`, which caches and refreshes the keys on its own.

*Alternatives:*
- **A hand-written handler:** rejected, because it would reimplement key rotation and validation that the
  library already gets right.
- **Trusting the email header:** rejected, because anyone who can reach the port can forge it.

### Account resolution as its own step after authentication
Middleware placed after `UseAuthentication` turns a verified principal into an app user. It:
1. reads the token's `email` and `identity_nonce`
2. resolves the account, through the cache
3. either adds the app's claims (user id, admin) or answers `/api` requests with a problem response
4. leaves non-API paths alone, so the page can load and explain a refusal

The Cloudflare-specific part is an `IIdentityLookup` interface ("full identity for this token"). Its Cloudflare
implementation calls get-identity and returns a provider, subject and name, or "unsupported" or "unavailable".
The resolution rules live in an `AccountService`, which knows nothing about Cloudflare.

*Alternatives:*
- **Resolving inside `JwtBearerEvents.OnTokenValidated`:** rejected. A failure there can only become a 401
  challenge, not the 403 or 503 problem responses the client needs, and it would mix Cloudflare verification
  with account rules.
- **`IClaimsTransformation`:** rejected. It runs again on every `AuthenticateAsync` call and can't
  short-circuit with a response.

### The full identity comes from get-identity, cached per Access session
The cache is in memory, keyed by the token's `identity_nonce`. Cloudflare documents that claim as the cache key
for the identity, and it changes when someone signs in to Access again. On a miss, the app calls
`https://<team>.cloudflareaccess.com/cdn-cgi/access/get-identity` and sends the token as the `CF_Authorization`
cookie. A plain request made this way, outside a browser, returned the full identity from the owner's deployment.

From the response it uses only `idp.type`, `id` and `name`. The response body is never logged, because it holds
the IP address, location and device sessions.

The email comes from the verified token, not from get-identity. Cloudflare's `idp.type` values map to
`IdentityProvider`:
- `google` and `google-apps` become `Google`, since both carry Google's account id
- any other value is unsupported

Cache lifetimes:

| Result | Cached for |
|---|---|
| Resolved user | until the token's `exp` |
| Refusal (`unsupported-sign-in`, `account-conflict`) | one minute, so a released email takes effect soon without the cache needing to be invalidated |
| Lookup failure | not cached; the next request tries again |

*Alternatives:*
- **Calling get-identity on every request:** rejected for latency, and because it would make Cloudflare a
  dependency of every request.
- **Having the browser fetch the identity and post it:** rejected, because the server can't trust it, and the
  external id decides which account someone gets.

### Data model
These live in `ReceiptSplit.Data`, configured in `AppDbContext` next to the receipts.

- **`Users`:**
  - `Id`: a UUID v7, like receipts
  - `Email`: optional, stored lowercased, unique when present (SQLite's unique index allows several nulls)
  - `Name`: optional
  - `CreatedAt`
- **`ExternalIdentities`:**
  - `Id`, `UserId` (foreign key, cascade), `Provider` (the `IdentityProvider` enum stored as a string, as
    `ReceiptStatus` is), `Subject` and `LinkedAt`
  - unique on (`Provider`, `Subject`) and on (`UserId`, `Provider`)
- **`Receipts.OwnerId`:** an optional foreign key to `Users`, restrict on delete (users aren't deleted yet),
  with an index on (`OwnerId`, `CreatedAt`) for listing.

The migration (`AddUsers`) creates two tables and adds one nullable column, so existing rows need no backfill.

*Alternative:* a column per provider (`GoogleId`) on `Users`. Rejected, because each new provider would need a
migration, and the table maps one-to-one onto the "one identity per provider" rule.

### Resolution rules and races
In `AccountService`:
1. **The identity exists:** that's the user. Update the name, and the email unless another user holds the new
   one. In that case keep the old email and log a warning with both user ids.
2. **No identity, but the email is held:** `account-conflict`.
3. **Neither:** create the user and the identity together, in one `SaveChanges` call.

Two first requests for the same person can race and hit a unique index. A `DbUpdateException` on create is
handled by resolving once more; the winner's row is then found by rule 1.

Development sign-in has no external identity, so it uses a separate rule: find or create the user by email.
That rule can only be reached through the Development scheme.

### The acting user is passed into `ReceiptService`
Each `ReceiptService` method takes an `Actor(Guid UserId, bool IsAdmin)` rather than reading `HttpContext`, so
a future bot can act as a user too. Visibility is one query helper: admins see everything, and members see
`OwnerId == UserId`. Every lookup by id goes through it, so a missing receipt and someone else's both come back
as `NotFound`.

`List` and `Get` move from the controller into the service. The controller builds the `Actor` from the claims
the account middleware added.

### Authorization
- **Default for endpoints:** a fallback policy requires the app's user-id claim.
- **The page:** `MapStaticAssets` and the SPA fallback (`MapFallbackToFile`) are marked `AllowAnonymous`. The
  page holds no data, and it has to load to show a refusal.
- **Admin-only endpoints:** an `Admin` policy, used as `[Authorize(Policy = "Admin")]`.
- **Admin status:** decided per request from the token's email against `Auth:Admins`, ignoring case.

### Problem responses
Every sign-in refusal is a `ProblemDetails` with a `code` extension: `unsupported-sign-in`, `account-conflict`
or `identity-unavailable`. Its `detail` is wording that doesn't name any provider.

Bulk delete failures use 404 or 409 problems with an `ids` extension, and a `detail` the manager shows as it is,
for example "1 receipt is being read; nothing was deleted."

### Bulk delete is all or nothing
The request runs in one transaction. Microsoft.Data.Sqlite starts transactions as `IMMEDIATE`, so the worker
can't switch a receipt to `Processing` between the check and the delete. In that transaction it:
1. loads the requested receipts visible to the actor
2. answers 404 if any id is missing, or 409 if any is `Processing`
3. otherwise deletes them all and commits

Photos are deleted after the commit, as now. The response becomes 204, and `ReceiptsDeletedDto` and
`ReceiptsDeleted` are removed. AGENTS.md records the all-or-nothing rule for future bulk endpoints.

### Configuration, development and the startup check
The `Auth` section. `AuthOptions` lives in `ReceiptSplit.Accounts`, and `CloudflareAccessOptions` binds its
subsection in the Cloudflare library:

| Setting | Purpose |
|---|---|
| `CloudflareAccess:TeamDomain` | the Access team domain |
| `CloudflareAccess:Audience` | the application's AUD tag |
| `Admins` | admin emails (a list) |
| `DevUser` | the development user's email |

`appsettings.Development.json` sets `DevUser` and lists the same email in `Admins`, so the owner's dev stack
still sees the existing receipts, which have no owner. In Development with `DevUser` set, a
`DevelopmentAuthenticationHandler` signs every request in as that email.

Two startup checks run before the app starts, next to `CheckModelServerAsync()`. Each library exposes its check as
an `IServiceProvider` extension and keeps the check class internal. Either failing exits with code 1:
- **`AccountSettingsCheck`** in `ReceiptSplit.Accounts` fails when `DevUser` is set outside Development. It logs a
  warning when `Admins` is empty, since then nobody can see receipts without an owner.
- **`CloudflareAccessCheck`** in the Cloudflare library runs unless the development user is signing requests in.
  It fails when the team domain or audience is missing, or when the Access keys can't be fetched, which catches a
  mistyped team domain at deploy time.

### Tests
`ReceiptApiFactory` does the following:
- clears `DevUser`
- sets a test team domain, audience and admin email
- swaps the handler's `ConfigurationManager` for a static one holding an RSA key it generated
- replaces `IIdentityLookup` with `FakeIdentityLookup`, which can return a given identity, "unsupported" or
  "unavailable"

`ConfigureClient` gives every client a token for a default member, so existing tests keep working.
`CreateClientFor(email, subject, ...)` makes a client for another user. The same factory then exercises the real
token checks.

`FakeIdentityLookup` and the test token signer (the RSA key and a method that makes Access-shaped tokens) live in
`tests/ReceiptSplit.Testing`. Both `ReceiptSplit.Tests` and the account libraries' tests use them.
`ReceiptApiFactory` stays in `ReceiptSplit.Tests`, because it needs the host.

### Client
- **Requests:** in `api.ts`, `request` uses `redirect: 'manual'`. Our API never redirects, so an
  `opaqueredirect` response means Access is sending the user to sign in, which becomes a `signed-out` problem.
- **Sign-in problems:** problem responses with a sign-in `code` raise a sign-in problem through a small
  subscribe function. `App` keeps the current problem in state and renders `SignInProblemPage` instead of the
  routes.
- **Current user:** `useAccount` loads `/api/me` once.
- **Header:** `AccountMenu` is a Mantine `Menu` triggered by `Avatar` with `name` and `color="initials"`, placed
  on the right of the header. Its Sign out item is a plain link to the `signOutUrl` from `/api/me`
  (`/cdn-cgi/access/logout`, null in development).
- **Receipt manager:** shows the bulk delete problem's `detail` in a red notification and reloads the list.

### Deployment
- **`compose.yaml`:** publishes on `${RECEIPTSPLIT_BIND:-127.0.0.1}:${RECEIPTSPLIT_PORT:-8080}:8080`, and passes
  through `Auth__CloudflareAccess__TeamDomain`, `Auth__CloudflareAccess__Audience` and `Auth__Admins__0` from
  `.env`. Docker's published ports bypass host firewalls, so the loopback binding is what keeps the LAN out.
- **HOSTING.md**, linked from the README: hosting behind Cloudflare Access, covering:
  - where to find the team domain and AUD tag
  - admins
  - cloudflared's optional `originRequest.access.required` check
  - releasing an email from the browser console
  - the SQL for the case where the only admin is the one refused
- **`Dockerfile`** (at the root): unchanged. Its `COPY --parents src/*/*.csproj` picks up the two new libraries,
  and the test projects stay out of the image.

## Risks / Trade-offs

- **[Risk]** Cloudflare changes the get-identity format, or it's down. → Requests fail visibly as
  `identity-unavailable`, and the cause is logged without the response body. Sessions already identified keep
  working.
- **[Trade-off]** The cache is lost on restart. → One extra get-identity call per active session after a deploy.
- **[Trade-off]** A Google account whose email was reused by another account is refused until an admin
  releases the email. → That's intended: a human decides who owns the receipts.
- **[Risk]** The only admin hits `account-conflict` and can't call release email. → HOSTING.md documents the
  SQL to clear the email by hand.
- **[Trade-off]** The admin list is matched against the token's current email. → If an admin's email changes,
  the configuration has to change too, which is the same step as updating the Access policy.
- **[Trade-off]** Receipts without an owner are only visible to admins. → `AuthSettingsCheck` warns when no
  admins are configured. Reassigning comes later.
- **[Trade-off]** Bulk delete now fails as a whole on a race with extraction. → The manager already prevents
  selecting receipts being read, so this only happens in a race, and the message says to try again.

## Migration Plan

1. Before merging, apply the migration to a copy of the owner's `data/`. Check that existing receipts load and
   have no owner.
2. In Cloudflare, note the team domain and the application's AUD tag.
3. Set them in `.env` along with the admin email:
   - `Auth__CloudflareAccess__TeamDomain`
   - `Auth__CloudflareAccess__Audience`
   - `Auth__Admins__0`
4. Optionally, turn on `access.required` in cloudflared's ingress rule.
5. Pull the new image with the new `compose.yaml`, which binds the port to `127.0.0.1`, and restart. cloudflared
   on the host still reaches `127.0.0.1:8080`.
6. **Rollback:** go back to the previous image. The migration only adds tables and a nullable column, so the
   older app keeps working on the migrated database and ignores them.

## Open Questions

- Does Cloudflare's get-identity return a stable `id` for providers other than Google, and in the same field?
  This only matters when another provider is added, and doesn't change this design.
