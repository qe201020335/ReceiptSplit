# Proposal

## Why

ReceiptSplit is about to be shared with other people through Cloudflare Access, which signs everyone in with
Google. Right now the app has no idea who is asking: every visitor sees and can delete every receipt, and the
container port is published on every interface, so anything on the LAN can bypass Access entirely. Receipts need
an owner, and the app needs its own user accounts that don't depend on any one sign-in provider's ids.

## What Changes

- The app verifies the JWT that Cloudflare Access adds to each request (signature, issuer, audience, expiry) and
  rejects requests without a valid one. Headers alone are never trusted.
- New user accounts with their own UUIDs. Accounts are linked to external identities (the provider's stable
  account id), not keyed by email. The first sign-in creates the account. Later sign-ins find it by the external
  id and pick up a changed email or name.
- The user's full identity (stable account id, name, sign-in method) is fetched from Cloudflare once per Access
  session and cached. It is never fetched on every request.
- Sign-ins the app can't safely accept are refused with a page explaining why, and a Sign out button:
  - **Unsupported method:** the sign-in method isn't one the app supports.
  - **Email conflict:** the email already belongs to an account linked to a different external identity.
  - **Identity unavailable:** the identity can't be confirmed right now. This one gets 503 and a Try again
    button.
- Admins are the emails listed in configuration. An admin-only API call releases an email from the account that
  holds it, which resolves an email conflict without moving one person's receipts to another.
- Receipts get an optional owner. New uploads are owned by the uploader, and existing receipts stay without an
  owner.
  - **Members** see and act on only their own receipts.
  - **Admins** see and act on all receipts.
  - **Someone else's receipt** looks the same as one that doesn't exist.
- **BREAKING:** Bulk delete is all or nothing:
  - Someone else's receipts or unknown ids reject the whole request with 404.
  - A receipt being read rejects it with 409.
  - Success returns 204 instead of the deleted and busy lists.
- The header shows the signed-in user as an initials avatar. It opens a menu with the name, email, an admin
  badge and Sign out. An expired Access session shows a "signed out" page instead of failing requests.
- **BREAKING (deployment):** `compose.yaml` publishes the port on `127.0.0.1` by default
  (`RECEIPTSPLIT_BIND`). The app refuses to start without the Access settings outside Development.
- The project docs change in three ways:
  - Migrations must preserve data from now on.
  - Bulk operations are all or nothing.
  - The "no authentication yet" note is replaced by how sign-in works.

## Capabilities

### New Capabilities
- `user-identity`: covers how a request's user is established and how accounts are managed. That means
  verifying the proxy's JWT, linking accounts to external identities, refusing sign-ins it can't accept, admins,
  releasing an email, the current-user API, and the account menu and sign-in problem pages in the client.
- `receipt-access`: covers who can see and act on which receipts. That means ownership, member and admin
  visibility, and all-or-nothing bulk operations on receipts.

### Modified Capabilities

None. There are no specs yet.

## Impact

- **Projects:**
  - `src/ReceiptSplit.Accounts`, new: the account rules, the identity lookup interface, the account middleware,
    admins, `Actor` and development sign-in.
  - `src/ReceiptSplit.Accounts.CloudflareAccess`, new: Access token verification and the get-identity lookup.
  - `src/ReceiptSplit.Data`: the `User` and `ExternalIdentity` entities, `Receipt.OwnerId` and the migration.
  - `src/ReceiptSplit.Receipts`: ownership in `ReceiptService`, which now references `ReceiptSplit.Accounts`
    for `Actor`.
  - New test projects for both account libraries under `tests/`.
- **Backend:**
  - New authentication and account code, and a `Users` and `ExternalIdentities` model with an additive migration.
  - `Receipt.OwnerId`.
  - `ReceiptService` methods take the acting user, and receipt listing and loading move into it from the
    controller.
  - New `UsersController` with `GET /api/me` and `POST /api/users/release-email`.
  - New package `Microsoft.AspNetCore.Authentication.JwtBearer`, with its version in `Directory.Packages.props`.
  - New `Auth` configuration section.
- **API:** every `/api` endpoint requires a signed-in user. `POST /api/receipts/delete` changes its responses.
- **Client:**
  - `api.ts`: current-user call, sign-in problem detection, bulk delete errors.
  - The header account menu and a sign-in problem page.
  - Receipt manager delete messages.
- **Tests:** the test host signs its own Access tokens, and it fakes the identity lookup.
- **Deployment:**
  - `compose.yaml`: port binding and the Access settings.
  - README: hosting behind Cloudflare Access.
  - AGENTS.md.
- **Data:** existing receipts are kept, without an owner. Only admins see them until reassigning exists.
