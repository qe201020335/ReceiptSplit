# Tasks

Each numbered group is one commit on the branch `reassign-receipts`. Every commit builds and passes `dotnet test`,
and `CI=true dotnet build ReceiptSplit.slnx` stays free of warnings. Groups that touch the client also pass
`npm run build` and `npm run lint` in `receiptsplit.client`. Browser checks use the following setup:
- a scratch backend on a free port (not 5015), with `Storage__Root` pointing at a scratchpad copy of `data/` and
  `Auth__DevUser` set
- Vite on a free port (not 5173), with `ASPNETCORE_URLS` set to that backend
- snap Chromium over CDP, at 1280px and 390px, in light and dark mode

Stop the backend and Vite after each check. The admin is the dev user listed in `Auth__Admins`, and the member is
a dev user who isn't.

## 1. Owner id on receipt summaries

- [x] 1.1 Add `Guid? OwnerId` to `ReceiptSummary` and `ReceiptSummaryDto`. Fill it in the projection in
  `ReceiptService.ListAsync` and in `ReceiptMappings.ToDto`, and add `ownerId: string | null` to `ReceiptSummary`
  in `api.ts`. Verify with new `ReceiptAccessTests`:
  - a member's listed receipt carries their `/api/me` id
  - an admin's list shows null for a receipt added without an owner, using the test's `AddUnownedReceiptAsync`

## 2. Admins list users

- [x] 2.1 Add public `UserSummary(Guid Id, string? Email, string? Name)` and `AccountService.ListUsersAsync`,
  sorted by name and then email. Verify with `AccountServiceTests` over in-memory SQLite: the order, and a user
  with a released email listed with a null email.
- [x] 2.2 Add `UserSummaryDto` to `UserDtos.cs`, and `GET /api/users` to `UsersController` with the Admin policy.
  Add `UserSummary` and `api.listUsers()` to `api.ts`. Verify with `AccountsApiTests`:
  - an admin lists themselves and two members with their ids, emails and names
  - a member gets 403

## 3. Reassign receipts

- [x] 3.1 Add `ReceiptActionResult.UnknownUser` and `ReceiptService.ReassignManyAsync` as design.md describes:
  - non-admins get `NotFound` for every id
  - an unknown user is checked first
  - missing ids come back listed
  - one immediate transaction, and no busy check

  Verify that the solution builds, and that the existing switches on `ReceiptActionResult` still fall through to
  404, since they don't handle the new value.
- [x] 3.2 Add `ReceiptReassignDto` with `[property: JsonRequired]` on `OwnerId`. Add `POST /api/receipts/reassign`
  with the Admin policy, mapping the results to 204, 400 "User not found" and a 404 `BulkProblem`. Add
  `api.reassignReceipts(ids, ownerId)` to `api.ts`. Verify with a test that a body without `ownerId` gets 400 and
  leaves the owner unchanged. If the attribute is ignored, use the fallback design.md names, and record the result
  here. Result: System.Text.Json honors `JsonRequired` on the record's constructor parameter. A body without
  `ownerId` gets 400 before the action runs, so no fallback is needed.
- [x] 3.3 Add `ReceiptAccessTests` for every scenario under "Admins reassign receipts" and "Reassigning is refused
  as a whole" in the delta spec:
  - to another user (the previous owner gets 404)
  - an unowned receipt to a member
  - back to no owner
  - several receipts with mixed owners
  - an unknown id (404, the `ids` extension, nothing changed)
  - an unknown user (400)
  - a member, for their own receipt and for another's (403)
  - an empty request (400)
  - a receipt being read: `FakeLlamaClient.Block()`, reassign, `Release()`, then wait for the result. It keeps
    the new owner and has the lines.

  Verify that `dotnet test` passes.
- [x] 3.4 In AGENTS.md:
  - in the Ownership bullet, add that admins reassign receipts, in any status, through the bulk endpoint, and that
    members get 403
  - in the bulk operations bullet, name `POST /api/receipts/reassign` beside the bulk delete

  In HOSTING.md, after releasing an email, say that the old account's receipts can be moved to the new one from
  the receipt manager. Verify by reading both against the controller.

## 4. Select receipts being read

- [x] 4.1 In `ReceiptManager.tsx`, remove `selectable()`, so every row and month checkbox can select receipts
  being read. Render Delete with `data-disabled` and `aria-disabled="true"` while any selected receipt is
  Processing. Its click then shows "*N* selected receipt(s) is/are being read and can't be deleted yet." and sends
  nothing. Verify in the browser:
  - mark one receipt Processing with `sqlite3` on the copy only after the scratch backend has started. The worker
    re-queues unfinished receipts at startup and would send it to the shared model server.
  - select it with others: Delete looks disabled, a click shows the toast, and no request is sent (DevTools
    network)
  - after resetting the status and reloading, the same selection deletes

  Verify that keyboard focus still reaches Delete.

## 5. Owners and the owner filter

- [x] 5.1 Change the manager route to `{ page: 'manage'; owner: string | null }` in `useRoute.ts`, reading and
  writing `?owner=`, and update every caller. Verify:
  - `/receipts?owner=none` survives a reload
  - from the manager, Open then Back returns to the same URL
  - changing the filter adds no history entry: Back from the manager still leaves it in one step
- [x] 5.2 Pass `account` from `App.tsx` to `ReceiptManager`. For admins:
  - load `api.listUsers()` in an effect with a `current` flag
  - render the owner line (avatar and name, or the dashed "No owner") in `[data-with-owner]` rows, in
    `ReceiptManager.module.css`, as design.md's row layout shows
  - add the owner `Select` (Everyone, No owner, then people, each with counts) to the header, with "*N* of *M*
    receipts" while filtering
  - compute `selectedIds` and the month checkboxes from the filtered list
  - show an unknown `owner` as Everyone

  A failed user list shows an Alert. Verify in the browser as the admin, on a copy of `data/` where some receipts
  have no owner:
  - the owners and "No owner" rows
  - each filter choice and its counts
  - a selection hidden by the filter isn't counted in the footer
  - no sideways scroll at 390px

  As the member, the page matches master: compare screenshots. Result: it matched. Later, at the owner's request,
  members' rows got the Open button too, so it no longer does.
- [x] 5.3 In AGENTS.md, add `/receipts?owner=none|{userId}` to the Routing line of the frontend conventions. Verify
  that it matches `readRoute`.

## 6. Reassigning from the manager

- [x] 6.1 Add `OwnerPicker.tsx` and `OwnerPicker.module.css`: a `Combobox` with `Combobox.Search` and options, as
  design.md describes.
  - people, with their avatar, name and email
  - "Owner" disabled on a row, and "You" on the signed-in admin
  - No owner always last
  - an empty state of 'No one matches "…"'
  - 300px wide, scrolling after about 6 options

  Verify that the client builds and lints.
- [x] 6.2 Add Reassign to each admin row, as an `Anchor` button in the right column with the aria-label "Reassign
  {store}, {date}". Picking calls `api.reassignReceipts([id], ownerId)`, then shows "Reassigned to {name}" (or "Set
  to no owner"), or "Couldn't reassign the receipts" with the error, and calls `onChanged()`. Disable it while a
  delete or a reassign is running. Verify in the browser as the admin:
  - search by part of an email
  - choose with the arrow keys and Enter
  - Escape changes nothing
  - the row shows the new owner after the reload
  - set a receipt back to No owner
- [x] 6.3 Add Reassign *N* to the footer, before Delete, with `variant="default"`. Picking opens
  `modals.openConfirmModal`, with the title "Reassign *N* receipts to {name}?" and the owner breakdown counted from
  the selected summaries. Confirming sends one request, clears the selection and shows "*N* receipts reassigned to
  {name}". Cancelling keeps the selection. A rejection shows the server's message and reloads. Verify in the
  browser:
  - a mixed selection gives the right breakdown
  - Cancel changes nothing
  - Confirm moves them all
  - at 390px the footer fits three buttons with a 3-digit count, in light and dark mode. Result: it didn't fit with
    full labels, so on phones the labels drop "receipts" and the buttons get less padding, as design.md now says.

## 7. End-to-end check

- [x] 7.1 Work through the No owner backlog on a fresh copy of `data/` as the admin:
  - filter by No owner
  - open a receipt and go back (the filter stays)
  - reassign one from its row
  - select the rest of a month and reassign them in bulk
  - filter by that person to confirm

  Then sign in as that person (restart with them as `Auth__DevUser`) and confirm that they list exactly those
  receipts and see no owners or Reassign. Run `dotnet test`, `npm run build` and `npm run lint` once more.

## Workflow follow-up

- Push and open a PR when the owner asks. After the merge, switch to master, pull, and delete the local branch.
- Archive the change after merging (`/opsx:archive`). That merges the deltas into `openspec/specs/receipt-access`
  and `openspec/specs/user-identity`.
