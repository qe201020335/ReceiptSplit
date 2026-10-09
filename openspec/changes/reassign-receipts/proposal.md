# Proposal

## Why

Receipts have owners now, but nobody can change one. The receipts from before accounts existed have no owner, so
only admins see them. The last change left them that way "until reassigning exists". A receipt uploaded from the
wrong account also stays with that account. Admins need to see who owns each receipt and hand receipts to the
right person, one at a time or many at once.

## What Changes

- Receipt summaries (`GET /api/receipts`) carry the owner's id, or null for a receipt without an owner.
- New admin-only `GET /api/users` lists every user's id, email and name, for the picker and the filter. Members
  get 403.
- New admin-only `POST /api/receipts/reassign` gives one or more receipts a new owner, or none.
  - It's all or nothing, like the bulk delete. Unknown ids give 404 listing them, an unknown user gives 400, and
    members get 403.
  - It works in every status, including while the model reads the receipt, because extraction never touches the
    owner.
- The receipt manager changes for admins only. Members see the page as it is today.
  - Each row shows the owner as an initials avatar and name, or "No owner".
  - Each row gets a Reassign button. It opens a searchable picker of people with a separate No owner choice, and
    the choice applies right away.
  - The selection footer gets Reassign *N*. After picking someone, a confirmation lists the owners being replaced.
  - The header gets an owner filter: Everyone, No owner, then each person, with counts. It's kept in the URL
    (`/receipts?owner=none` or `?owner={userId}`).
  - Bulk actions act only on selected receipts that the filter shows.
- Selection changes for everyone. Receipts being read can now be selected.
  - While any selected receipt is being read, Delete looks disabled. Clicking it shows a toast saying why instead
    of sending the request.
  - Before this change, those rows couldn't be selected at all.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `receipt-access`:
  - adds admins reassigning receipts, singly or in bulk, including back to no owner
  - adds the receipt manager showing and filtering owners for admins
  - changes selection: receipts being read can be selected, and Delete refuses in the client
- `user-identity`: adds admins listing users.

## Impact

- **Backend:**
  - `ReceiptSummary` and `ReceiptSummaryDto` gain `OwnerId`.
  - `ReceiptService` gains the bulk reassign.
  - `AccountService` gains the user list, with a public `UserSummary`.
  - `ReceiptsController` and `UsersController` get the new endpoints, with DTOs in `ReceiptDtos.cs` and
    `UserDtos.cs`.
  - There's no schema change and no migration: `Receipts.OwnerId` and its foreign key already exist.
- **Client:**
  - `api.ts` gets the new types and calls.
  - `useRoute.ts` gets the manager's `owner` parameter.
  - `ReceiptManager.tsx` and its CSS change.
  - A new `OwnerPicker.tsx` component.
  - `App.tsx` passes the account to the manager.
- **Tests:**
  - API tests in `ReceiptAccessTests` and `AccountsApiTests`.
  - `AccountService` tests in `ReceiptSplit.Accounts.Tests`.
- **Docs:** the ownership notes in AGENTS.md.
- **Compatibility:** summaries gain a field, and the endpoints are new, so existing callers keep working. The
  only other client of the API is this app.
