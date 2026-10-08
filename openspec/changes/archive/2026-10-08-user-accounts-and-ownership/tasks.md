# Tasks

Each numbered group is one commit on the branch `users-and-ownership`. Every commit builds and passes the
solution's tests, run with the test command in AGENTS.md. Client groups also pass `npm run build` and
`npm run lint` in `receiptsplit.client`. Projects are named as in design.md, "Two account libraries". The
project's conventions apply throughout:
- types are internal unless another production project uses them
- package versions go in `Directory.Packages.props`
- shared test helpers go in `tests/ReceiptSplit.Testing`
- `CI=true dotnet build` has to stay free of warnings

## 1. Confirm the identity lookup (with the owner)

- [x] 1.1 Ask the owner to run `curl -s --cookie "CF_Authorization=<token>"
  https://<team>.cloudflareaccess.com/cdn-cgi/access/get-identity` with a token from their browser. If that is
  refused, try the same path on the app's hostname. Record which URL returns the identity JSON with `id`, `name`
  and `idp.type`, and use it in group 5. Result: the team-domain URL returned the full identity (`id`, `name`,
  `email`, `idp.type: google`), so the app's hostname isn't needed.

## 2. Account projects

- [x] 2.1 Create these projects and add them to `ReceiptSplit.slnx` under the `/src/` and `/tests/` solution
  folders:
  - `src/ReceiptSplit.Accounts`, referencing `ReceiptSplit.Data`
  - `src/ReceiptSplit.Accounts.CloudflareAccess`, referencing `ReceiptSplit.Accounts`
  - `tests/ReceiptSplit.Accounts.Tests`
  - `tests/ReceiptSplit.Accounts.CloudflareAccess.Tests`

  The host references both libraries. Verify that the solution builds, the tests match the count before the
  change, and `docker build .` restores the two new libraries without a Dockerfile change. Result: a test project
  without tests fails `dotnet test` (exit code 8, zero tests ran), so each test project is added with its first
  tests in 4.3, as AGENTS.md says. The tests matched (152, 3 skipped), and the image's build stage built both
  libraries with the Dockerfile unchanged.

## 3. Users, external identities and receipt owners

- [x] 3.1 In `ReceiptSplit.Data`, add `User`, `ExternalIdentity` and `IdentityProvider`, and configure them in
  `AppDbContext` as design.md's data model describes. Verify that the solution builds.
- [x] 3.2 Add `Receipt.OwnerId`, nullable, with its foreign key to `User` and the (`OwnerId`, `CreatedAt`) index.
  Generate the `AddUsers` migration in `ReceiptSplit.Data` with the `dotnet ef` command from AGENTS.md
  (`--project src/ReceiptSplit.Data --startup-project src/ReceiptSplit`). Verify that the generated SQL only creates
  tables, adds a nullable column and adds indexes. Result: EF's `AddForeignKey` rebuilt the `Receipts` table, so
  the column and its foreign key are added with one `ALTER TABLE` in `migrationBuilder.Sql` instead.
- [x] 3.3 Apply the migration to a copy of `data/` in the scratchpad by starting a scratch backend on it. Verify
  that `GET /api/receipts` still lists every existing receipt, and that `OwnerId` is null on all of them
  (`sqlite3`). Result: 28 receipts and 385 lines kept, all without an owner, and `foreign_key_check` is clean.
- [x] 3.4 Rewrite the Migrations section of AGENTS.md:
  - the data must be preserved
  - add a migration for every model change, and never edit, replace or collapse a merged one
  - fill in values for existing rows within the migration
  - check each migration against a copy of the dev database

  Verify that the section no longer says the data needn't be preserved.

## 4. Verify the Access token

- [x] 4.1 In `ReceiptSplit.Accounts.CloudflareAccess`:
  - add `CloudflareAccessOptions` (`Auth:CloudflareAccess`) and the `Microsoft.AspNetCore.Authentication.JwtBearer`
    package, with its version in `Directory.Packages.props` matching the other ASP.NET Core packages
  - configure the handler as design.md describes: the `Cf-Access-Jwt-Assertion` header, the issuer and audience,
    RS256, `MapInboundClaims = false`, and a key-set `IConfigurationRetriever` for `<team>/cdn-cgi/access/certs`
  - expose it through `AddCloudflareAccess()` on `IHostApplicationBuilder`, which calls `AddAccounts()` first

  In the host, add `UseAuthentication`, the fallback policy requiring an authenticated user, and `AllowAnonymous`
  on the static assets and the SPA fallback. Verify that the solution builds.
- [x] 4.2 In `ReceiptSplit.Accounts`, add `AuthOptions` (`Auth:Admins`, `Auth:DevUser`) and
  `DevelopmentAuthenticationHandler`, which is active only in Development. Register them through `AddAccounts()`
  on `IHostApplicationBuilder`, which calls `AddDatabase()` first and does nothing when called again. Set `DevUser`
  and the same email in `Admins` in `appsettings.Development.json`. Verify by running the backend in Development:
  `GET /api/receipts` succeeds without a token. Result: the development user is `dev@example.com`, and a scratch
  backend listed all 28 receipts without a token.
- [x] 4.3 Add `AccountSettingsCheck` (in `ReceiptSplit.Accounts`) and `CloudflareAccessCheck` (in the Cloudflare
  library), both internal, exposed as `CheckAccountSettingsAsync()` and `CheckCloudflareAccessAsync()` on
  `IServiceProvider`. Call them in `Program.cs` beside `CheckModelServerAsync()`. Between them, the app exits with
  code 1 when:
  - `DevUser` is set outside Development
  - the team domain or audience is missing, unless the development user is signing requests in
  - the Access keys can't be fetched

  It warns when `Admins` is empty. Verify with unit tests in each library's test project, one per failure, in the
  style of `ModelServerCheckTests`.
- [x] 4.4 Update `ReceiptApiFactory`:
  - clear `DevUser`
  - set a test team domain, audience and admin email
  - use a static `ConfigurationManager` with a generated RSA key
  - give `ConfigureClient` a token for a default member
  - add `CreateClientFor(...)` and a way to make raw tokens

  The RSA key and the method that makes Access-shaped tokens go in `ReceiptSplit.Testing` as a test token signer,
  so the account libraries' tests can use them too. Verify that all existing API tests pass unchanged.
- [x] 4.5 Add API tests in `ReceiptSplit.Tests` for the token scenarios in `specs/user-identity`:
  - no token → 401
  - forged email header only → 401
  - wrong key, audience or issuer → 401
  - expired → 401
  - `/` and a static file load without a token

  Verify by running the tests. Result: tests don't build the client, so the page test serves `/` and a client
  route from a temporary web root; a real asset is checked against a published build in 10.1.

## 5. Accounts, admins and sign-in refusals

- [x] 5.1 Add `IIdentityLookup` and its result types to `ReceiptSplit.Accounts`. Add `CloudflareIdentityLookup`
  to the Cloudflare library. It calls `<team>/cdn-cgi/access/get-identity` with the token as the
  `CF_Authorization` cookie (confirmed in 1.1), maps `idp.type` `google` and `google-apps` to `Google`, treats
  anything else as unsupported, and treats failures as unavailable. The response body is never logged. Register it
  as a typed `HttpClient`. Add `FakeIdentityLookup` to `ReceiptSplit.Testing`, able to return a given identity,
  "unsupported" or "unavailable", and use it in `ReceiptApiFactory`. Verify with unit tests in
  `ReceiptSplit.Accounts.CloudflareAccess.Tests` on a canned response, covering both Google types, an unknown type
  and a failure.
- [x] 5.2 Add `AccountService` to `ReceiptSplit.Accounts`, with the resolution rules from design.md:
  - found by identity, updating the name and the email unless the new email is held (then log a warning)
  - `account-conflict` when the email is held
  - create the user and identity together
  - retry once after a unique-constraint race
  - the development rule that finds or creates by email

  Verify with tests in `ReceiptSplit.Accounts.Tests` for each rule in `specs/user-identity`. Those tests use
  `AppDbContext` over in-memory SQLite.
- [x] 5.3 Add the account middleware to `ReceiptSplit.Accounts`, exposed as `UseAccounts()`, and call it after
  `UseAuthentication`. It:
  - caches by `identity_nonce` (a resolved user until `exp`, a refusal for one minute, failures not at all)
  - adds the user-id and admin claims
  - answers `/api` requests with 403 `unsupported-sign-in`, 403 `account-conflict` or 503 `identity-unavailable`
    problems, with a `code` extension and wording that names no provider
  - leaves other paths alone

  Switch the fallback policy to require the user-id claim. Add the `Admin` policy, with admins matched by token
  email against `Auth:Admins` ignoring case. Verify with API tests:
  - one identity lookup for repeat requests in a session
  - refusals carry the right status and code
  - a session already identified keeps working while lookups fail
  - admin by email, in any letter case
- [x] 5.4 Add `UsersController` to the host with `GET /api/me`, returning `AccountDto(Id, Email, Name, IsAdmin,
  SignOutUrl)` with `SignOutUrl` null in development. Add admin-only `POST /api/users/release-email` (204, 404 for
  an unknown email, 403 for members), with its logic in `AccountService`. Verify with API tests:
  - the `/api/me` fields for a member and for an admin
  - release-email as a member gets 403
  - an unknown email gets 404
  - releasing the email of a conflict lets that person in as a new user after the refusal cache expires, using a
    controllable clock or cache, while the old user keeps their receipts

## 6. Receipt ownership

- [x] 6.1 Make `ReceiptSplit.Receipts` reference `ReceiptSplit.Accounts`; Accounts must not reference Receipts.
  Pass `Actor` (from `ReceiptSplit.Accounts`) to every `ReceiptService` method, and move `List` and
  `Get` out of `ReceiptsController` into the service. Add one visibility helper: admins see everything, members see
  their own. Set `OwnerId` in `CreateAsync`. The controller builds the `Actor` from the claims. Verify that the
  solution builds.
- [x] 6.2 Add API tests for `specs/receipt-access`:
  - an upload is owned by the uploader
  - a member's list holds only their own receipts
  - a member gets 404 for someone else's receipt on get, image, PUT, PATCH, extract and DELETE
  - a member gets 404 for a receipt without an owner
  - an admin's list includes other users' receipts and receipts without an owner
  - an admin can edit and delete another user's receipt

  Verify by running the tests.

## 7. All-or-nothing bulk delete

- [x] 7.1 Rewrite `ReceiptService.DeleteManyAsync` to run in one transaction. It returns not found (with ids) when
  any id is missing or not visible, busy (with ids) when any is `Processing`, and otherwise deletes them all.
  Photos are deleted after the commit. The controller answers 204, or 404 or 409 problems with an `ids`
  extension and a `detail` saying nothing was deleted. Remove `ReceiptsDeletedDto` and `ReceiptsDeleted`.
- [x] 7.2 Replace the bulk delete tests with the scenarios in `specs/receipt-access`:
  - all deletable → 204
  - someone else's receipt → 404, nothing deleted
  - an unknown id → 404, nothing deleted
  - a receipt being read (`FakeLlamaClient.Block()`) → 409, nothing deleted
  - empty → 400

  Verify by running the tests.
- [x] 7.3 Update the client:
  - `api.deleteReceipts` returns nothing
  - `ApiError` keeps the problem detail
  - `ReceiptManager` shows a green notification on success, or the problem's detail in red on 404 or 409, and
    reloads the list either way
  - remove the `ReceiptsDeleted` type

  Verify with `npm run build` and `npm run lint`.
- [x] 7.4 Update AGENTS.md. Add the rule that bulk operations are all or nothing and list what blocked them.
  Replace the bulk delete sentence under "Statuses", which says it leaves busy receipts. Verify by reading the
  section.

## 8. Account menu and sign-in problem pages

- [x] 8.1 In `api.ts`:
  - add the `Account` type and `api.me()`
  - make `request` use `redirect: 'manual'` and turn an `opaqueredirect` into a `signed-out` problem
  - turn problems with code `unsupported-sign-in`, `account-conflict` or `identity-unavailable` into sign-in
    problems
  - publish sign-in problems through a small subscribe function

  Verify with `npm run build`.
- [x] 8.2 Add `useAccount`, and `AccountMenu`: a Mantine `Avatar` with `name` and `color="initials"`, triggering
  a `Menu` with the name, email, an Admin badge, and Sign out only when `signOutUrl` is set. Place it on the right
  of the header. Verify with `npm run build` and `npm run lint`.
- [x] 8.3 Add `SignInProblemPage` with the four messages and actions in `specs/user-identity`, worded without
  any provider name. `App` renders it in place of the routes while a problem is set. Verify with `npm run build`
  and `npm run lint`. Result: the page can't get the sign-out URL from `/api/me` when that is refused, so the
  sign-in problems also carry `signOutUrl` from `IIdentityLookup`.
- [x] 8.4 Check in headless Chromium, against a scratch backend and Vite on spare ports, at 1280px and 390px in
  light and dark mode:
  - the avatar and the menu (by click and by keyboard)
  - the admin badge for the dev admin, and its absence for a dev member (restart with another `Auth__DevUser`)
  - each problem page, by having the scratch backend's `/api/me` return each problem; the signed-out page by
    making a fetch get an opaque redirect
  - no sideways scroll

  Result: checked by script, with `/api/me` intercepted in the browser for an account with a sign-out URL, each
  problem and a 302; 76 checks passed in two runs.

## 9. Deployment and docs

- [x] 9.1 In `compose.yaml`, publish on `${RECEIPTSPLIT_BIND:-127.0.0.1}:${RECEIPTSPLIT_PORT:-8080}:8080` and pass
  through the `Auth__CloudflareAccess__TeamDomain`, `Auth__CloudflareAccess__Audience` and `Auth__Admins__0`
  environment variables, with comments. Verify with `docker compose config`. Result: like the existing settings,
  they are read from `.env` as `ACCESS_TEAM_DOMAIN`, `ACCESS_AUD` and `RECEIPTSPLIT_ADMIN`.
- [x] 9.2 Add a README section on hosting behind Cloudflare Access:
  - the team domain and AUD tag
  - admins
  - the loopback binding, and why Docker's published ports need it
  - cloudflared's optional `access.required`
  - the release-email `fetch` snippet for the browser console
  - the SQL for clearing an email when the only admin is refused
  - the supported sign-in methods

  Verify by following the snippet's endpoint and body against `UsersController`. Result: the section later moved
  to its own HOSTING.md, which the README links to.
- [x] 9.3 In AGENTS.md:
  - replace "No authentication in the app yet" with how sign-in works: the Access JWT, the two account
    libraries and the rule that only the Cloudflare one knows about Cloudflare, `AccountService`, external
    identities, admins from config, `Actor` in `ReceiptService`, and the test factory's tokens
  - add the two account libraries and their test projects to Layout, along with the dependency
    Receipts → Accounts → Data

  Verify by reading the section against the code.

## 10. Integration check

- [x] 10.1 Run the full check from a clean build. All must pass; report the test counts.
  - `CI=true dotnet test`, so warnings are errors
  - `npm run build`
  - `npm run lint`
  - `docker build .`

  Result: 214 tests, 211 passed and 3 skipped (the sample and live model tests), with no warnings; the client
  built and linted clean; the image built. The image, run with the owner's team domain and a stub model server,
  served `/`, a client route, the favicon and a built asset without a token, and answered 401 on the API without a
  token, with only the email header and with a bogus token.
- [x] 10.2 On a scratch backend over a copy of `data/`, signed in as the dev admin:
  - existing receipts appear on the start page, in the manager and in Multi Split
  - an upload is read to Completed
  - a bulk delete of two receipts succeeds

  Then as a dev member, the existing receipts are gone and their URLs give the not-found state. Stop the servers,
  and delete the copy and the browser profile. Result: 28 receipts on the start page and in the manager, a Multi
  Split of two, a T&T photo read to Completed and owned by the dev admin, and a bulk delete going from 29 to 27. As
  a dev member: no receipts, and a receipt's page shows Not Found.
- [x] 10.3 Ask the owner to deploy the branch's image behind Access and confirm:
  - sign-in works
  - `/api/me` shows their name and admin status
  - a direct request to the host's LAN address on the app port is refused or unreachable

  Result: deployed to a staging Access application. Sign-in and `/api/me` worked. With the port published on
  every interface on purpose, the LAN address loaded the page but the API answered 401 and no account showed, so
  even an exposed port gives nothing away.

## Workflow follow-up

- Push and open a PR when the owner asks. After the merge, switch to master, pull, and delete the local branch.
- Archive the change after merging (`/opsx:archive`), which creates `openspec/specs/user-identity` and
  `openspec/specs/receipt-access`.
