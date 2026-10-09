# Design

## Context

What's already there and can be built on:
- `Receipts.OwnerId` exists, nullable, with its foreign key to `Users` (`AppDbContext`), so this change needs no
  migration.
- `ReceiptService` routes every lookup through `Visible(actor)`. Bulk delete (`DeleteManyAsync`) sets the pattern
  for all-or-nothing requests:
  - an immediate SQLite transaction
  - a `BulkReceiptResult` listing the blocking ids
  - the controller's `BulkProblem`, which puts them in an `ids` extension
- `UsersController` already has one admin-only endpoint (`release-email`, `[Authorize(Policy =
  AccountPolicies.Admin)]`), and `AccountService` is the public face of `ReceiptSplit.Accounts`.
- `ReceiptExtractor` loads the receipt as a tracked entity and saves it. EF writes only the columns it changed,
  and the extractor never touches `OwnerId`.
- In the client, `ReceiptManager` gets the list from `App`. A row can't be selected today while its receipt is
  Processing (`selectable()`). `useRoute` reads and writes query strings for `/` and `/splits` and has `replace`.
  `AccountMenu` draws users as `Avatar name=… color="initials"`.

## Goals / Non-Goals

**Goals:**
- One endpoint does reassigning, for one receipt or many, under the bulk rule in AGENTS.md.
- Members' API surface and page stay the same, apart from the new `ownerId` field and the selection change.
- The person picker and the owner filter work from one user list, loaded once per visit to the manager.

**Non-Goals:**
- Members handing their own receipts to someone.
- Showing or changing the owner on a receipt's own page (`/receipts/{id}`) or the start page.
- Server-side user search or paging. A household has a handful of users.
- Undo for a bulk reassign. It's confirmed beforehand instead.

## Decisions

### One bulk endpoint, also used for a single row
`POST /api/receipts/reassign` takes `{ ids, ownerId }`, and a row's Reassign sends one id. It follows the bulk
delete's name and shape (`POST /api/receipts/delete`).

*Alternative:* add `PUT /api/receipts/{id}/owner` as well. Rejected: it would be a second endpoint, with its own
tests and status codes, that does nothing the bulk one can't.

### `ownerId` must be present; null means no owner
The DTO is `ReceiptReassignDto([Required, MinLength(1), MaxLength(1000)] IReadOnlyList<Guid> Ids, Guid? OwnerId)`,
with `OwnerId` marked `[property: JsonRequired]`.
- A body without `ownerId` fails to deserialize, so `[ApiController]` answers 400 on its own. A client bug that
  leaves the field out can't strip owners from every receipt it names.
- `JsonRequired` is a serializer attribute, not a validation attribute. AGENTS.md's rule against `[property:
  ...]` is about validation attributes, so it doesn't apply here.

Task 3.2 checks that it behaves this way. If System.Text.Json ignored the attribute on a constructor-bound
parameter, the fallback is `RespectRequiredConstructorParameters` on this DTO's path, or a `required` property.

*Alternative:* a sentinel such as `"ownerId": "none"`. Rejected: it isn't a Guid, so it would need its own type and
parsing.

### `ReceiptService.ReassignManyAsync`
`ReassignManyAsync(Actor, IReadOnlyCollection<Guid> ids, Guid? ownerId, CancellationToken)` returns a
`BulkReceiptResult`, and the service decides everything:
- A non-admin actor reaches no receipt for this action, so every id comes back `NotFound`. The controller's Admin
  policy answers 403 before that, but the service doesn't rely on its caller.
  - This mirrors "a receipt you can't reach is not found", and other entry points stay safe.
- An `ownerId` no user has gives the new `ReceiptActionResult.UnknownUser` with no blocking ids. It's checked
  before the receipts, so the 400 isn't hidden by a 404. The foreign key would also stop it, but as an exception
  instead of a clean result.
- Ids that don't exist give `NotFound` listing them.
- Otherwise the method sets `OwnerId` on each receipt and saves, all inside one transaction.
  - The transaction is the same immediate one `DeleteManyAsync` takes, so a receipt deleted between the check and
    the update can't produce a partial result.
- There's no busy check. The model only writes extraction columns, so reassigning while it reads changes nothing
  it relies on, and it overwrites nothing on its save. A test reassigns while `FakeLlamaClient` is blocked, then
  releases it.

The controller maps the results:

| Result | Response |
| --- | --- |
| `Done` | 204 |
| `UnknownUser` | 400 "User not found" problem |
| `NotFound` | 404 `BulkProblem`, "*N* receipts couldn't be found; nothing was reassigned." |

### Summaries carry `ownerId`, and names come from the user list
`ReceiptSummary` and its DTO gain `Guid? OwnerId`. Nothing is joined to `Users` in `ListAsync`. The admin's client
already loads the user list for the picker and the filter, and maps ids to names with it.

Members get the field too. It's always their own id, which tells them nothing new, and one DTO stays simpler than a
member and an admin variant.

*Alternative:* embed `{ id, name, email }` in each summary. Rejected: it repeats the same few people on every row,
and the picker would need the list anyway.

### The user list lives in `AccountService`
`ListUsersAsync` returns public `UserSummary(Guid Id, string? Email, string? Name)`. It sorts by name and then
email, and nulls go last on the client. `GET /api/users` in `UsersController` is admin-only and returns
`UserSummaryDto`. Users stay the Accounts library's concern, and Receipts doesn't learn to list them.

### Client state
- `Route` for the manager becomes `{ page: 'manage'; owner: string | null }`, read from `?owner=`. `'none'` means
  no owner, and anything else is a user id. Changing the filter calls `replace`, so it adds no history entry.
  Existing callers pass `owner: null`. `goBack({ page: 'manage', owner: null })` still steps back to the filtered
  URL, because it goes back through real history.
- `App` passes `account` to `ReceiptManager`. When `account.isAdmin` is set, the manager loads `api.listUsers()` in
  an effect with a `current` flag, as `ReceiptDetail` does, and builds `Map<id, UserSummary>`.
- An `owner` value the list doesn't contain shows Everyone, with the select on Everyone.
  - The URL is left as it is until the filter is changed.
  - While the list loads, the filter applies by id anyway, since `ownerId` doesn't need names.
- `selectable()` goes away, and every row can be selected.
  - `selectedIds` is computed from the filtered list, so hidden, deleted or moved receipts drop out of the footer's
    count and actions.
  - The month checkbox selects that month's filtered receipts.
- Delete is "blocked" while any selected receipt is Processing. It's rendered with `data-disabled` and
  `aria-disabled="true"`, not `disabled`, because a disabled button gets no click to show the toast from. Its
  `onClick` shows the notification and returns.

### The picker
`OwnerPicker.tsx` is a Mantine `Combobox` built from `useCombobox`:
- `Combobox.Target` wraps the trigger, and the dropdown holds `Combobox.Search` and `Combobox.Options`. Arrow
  keys, Enter, Escape and focus come from Combobox, not from hand-written code.
- Filtering is a case-insensitive substring match on name and email.
- Props: `users`, `currentOwnerId` (undefined for the footer, where owners are mixed), `meId`, `onPick(ownerId:
  string | null)` and the trigger element.
- The same component serves a row and the footer. On a row, picking calls the API directly. In the footer, it
  opens `modals.openConfirmModal` with the owner breakdown, which is counted from the selected summaries'
  `ownerId`.

## Visual design

The page belongs to an existing app, so the brand stays fixed:
- Mantine 9
- the `theme.ts` palette (primary `#2f6fed`, or `#6b9bff` in dark mode with dark text)
- the system font stack
- the 1.05rem h2

This feature spends its boldness in one place: owners shown as people, so a list of receipts reads as "whose is
whose" at a glance. Everything else stays as quiet as the existing list.

**Color tokens.** Nothing is added to `theme.ts`.

| Use | Token |
| --- | --- |
| Owner avatar | Mantine `color="initials"`, the same as the header's account menu, so a person keeps their color everywhere |
| No owner avatar | a transparent fill with a 1px dashed `--mantine-color-default-border` outline and an empty-person glyph in `--mantine-color-dimmed` |
| Owner name | `--mantine-color-text` at size `sm` |
| "No owner" text | `--mantine-color-dimmed`, in italic. It's what the admin is hunting for, so it differs in shape rather than color, and both schemes keep their contrast |
| "Current" / "You" marks in the picker | dimmed `xs` text, not badges, so the list doesn't look like a status board |

**Type.** No new faces or sizes:
- row title `fw 500`
- the date and owner line at `sm`
- picker option names at `sm`, with the email at `xs` dimmed beneath
- sentence case throughout, with no all-caps labels

**Row layout (admins).** When `data-with-owner` is set, the row switches from flex to a grid with two columns,
`1fr` and `auto`. Members' rows don't get the attribute, so their layout is untouched.

```
+--------------------------------------------------------+
| [ ] Costco Wholesale              $214.37   [  Open  ] |
|     2026-10-03                 [Completed]  [Reassign] |
|     (AC) Alice Chen                                    |
+--------------------------------------------------------+
| [ ] T&T Supermarket                $48.10   [  Open  ] |
|     2026-09-28               [Needs review] [Reassign] |
|     (  ) No owner                                      |
+--------------------------------------------------------+
```

- The left column is the existing toggle button: checkbox, title, total, date and status.
- The owner line sits outside it, in row 2, starting where the title does, because a button can't contain
  another button.
- The right column spans both rows and stacks two `xs` default buttons of equal width, Open and Reassign,
  centered vertically on the row. Open is a button rendered as a link, so it can still be opened in a new tab.
  Two bare links read as stray text and didn't line up with the row's middle.
- Members' rows keep the plain Open link, because their page doesn't change.
- At 390px the owner name truncates, and the buttons get 8px side padding so the dates still fit.
- Hovering over and checking the row tint the whole grid, as they do today.

**Header (admins).** On a phone the filter wraps below the title. Its `Select` options are "Everyone (40)", "No
owner (28)", then each person, such as "Alice Chen (7)". On wide screens it sits to the right of the title,
because it's a page control rather than a row action.

```
+--------------------------------------------------------+
| <- Back   Manage receipts         Owner [No owner (28)v]|
|           28 of 40 receipts                            |
+--------------------------------------------------------+
```

The count line reads "*N* of *M* receipts" while a filter is on.

**Picker.**

```
            Reassign
+-------------------------------+
| [ Search people             ] |
|-------------------------------|
| (AC) Alice Chen      Current  |  <- disabled on a row
|      alice@example.com        |
| (BK) Ben Kim                  |
|      ben@example.com          |
| (SQ) Sky                You   |
|      sky@example.com          |
|-------------------------------|
| (  ) No owner                 |  <- always shown, last
+-------------------------------+
  No one matches "zed"           <- empty state, No owner still offered
```

- The dropdown is 300px wide and fits inside 390px.
- Its height is capped at about 6 options, then it scrolls. It uses a plain `overflow-y: auto` box, because
  Mantine's `ScrollArea` sizes its content to the widest row, which stops long emails from truncating.
- The search field takes focus on open.
- The dropdown is mounted only while it's open (`keepMounted={false}`). Combobox keeps closed dropdowns in the page
  by default, which would add a hidden search box for every row.

**Footer.** The buttons are `[Clear] ... [Reassign N] [Delete N]`. Reassign is `variant="default"` and Delete is
red, so the action that can't be undone keeps the strong color. At 390px the three buttons don't fit with full
labels and a 3-digit count. Below the `xs` breakpoint the labels drop the word "receipts" ("Reassign 100", "Delete
100"), and the buttons and the gap between them get less padding. Each button's `aria-label` keeps the whole phrase,
and the confirmation names the count in full.

**Copy.**

| Where | Text |
| --- | --- |
| Row and footer buttons | "Reassign", "Reassign *N* receipts" |
| Success toast | "Reassigned to Alice Chen", "*N* receipts reassigned to Alice Chen", "Set to no owner" |
| Failure toast | "Couldn't reassign the receipts", with the server's message |
| Confirm modal title | "Reassign *N* receipts to Alice Chen?" |
| Confirm modal body | "7 have no owner and 3 are Alice's.", or for a single owner "All 10 are Ben's." |
| Confirm modal button | "Reassign *N* receipts" |
| Blocked Delete | "1 selected receipt is being read and can't be deleted yet." |

**Quality floor:**
- the picker and the filter work by keyboard
- focus is visible
- aria-labels "Reassign {store}, {date}" match Open's
- checked in light and dark mode at 1280px and 390px

## Risks / Trade-offs

- [The client filters on a list it loaded a moment ago, and a receipt can change owner in another tab] →
  `onChanged()` reloads the list after every action, and the server applies only what's sent, all or nothing.
- [An admin reassigns many receipts by mistake] → the confirmation states the owners being replaced. Recovering
  means filtering by the new owner and reassigning, but the old mix would have to be remembered.
- [`ownerId` in summaries shows members their own id] → it's already in `GET /api/me`.
- [Rows being read are now selectable, and Delete is blocked by a status that changes under the user] → the list
  polls while receipts are in progress (`App.tsx`), so Delete unblocks itself. The server's 409 stays as the
  backstop.

## Migration Plan

There's no schema change. Deploy as usual, and roll back by deploying the previous image. Owners set in the
meantime stay set, and the previous version reads them correctly.
