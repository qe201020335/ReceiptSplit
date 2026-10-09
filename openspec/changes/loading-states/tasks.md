# Tasks

Each numbered group is one commit on the branch `loading-states`, and every commit passes `npm run build` and
`npm run lint` in `receiptsplit.client`. The backend is untouched, but `dotnet test` still runs before the last
commit. Browser checks use the following setup:
- a scratch backend on a free port (not 5015), with `Storage__Root` pointing at a scratchpad copy of `data/`. The
  copy is seeded with a few users owning some receipts.
- Vite on a free port (not 5173)
- snap Chromium over CDP, at 1280px and 390px, in light and dark mode

To hold a first load open, the checks delay the API route in Playwright (`page.route('**/api/...')`) and take
screenshots while it waits. Stop everything afterwards.

## 1. Placeholder pieces, the start page and the header

- [x] 1.1 Add `components/Placeholder.tsx` and `Placeholder.module.css` as design.md describes:
  - `Loading`: an `aria-busy` wrapper with a `VisuallyHidden` label
  - `Bar`: a `Skeleton` that doesn't animate under `useReducedMotion()`
  - `ReceiptRowsPlaceholder`: summary-shaped rows with fixed, varied widths and an optional owner line

  Verify that the client builds and lints.
- [x] 1.2 In `ReceiptList.tsx`, replace the "Loading…" line with 5 placeholder rows inside the card. In `App.tsx`,
  draw a 28px circle in the avatar's place while the account loads. Verify in the browser, with
  `/api/receipts` and `/api/me` delayed:
  - the start page shows placeholder rows and the circle, at both widths and in both schemes
  - when the data arrives the rows replace them, and the header doesn't shift (compare the avatar's position)
  - a list reload, such as uploading, doesn't bring the placeholders back
  - with the list route failing, the error shows where the rows were

  Result: an empty rows `Stack` still drew its gap while loading, which made the card 12px taller than when
  loaded. The rows `Stack` now renders only once there are rows. The placeholder also stands in for the footer
  (Manage receipts), so the card is the same height loading and loaded (423px), and the avatar keeps its position.
- [x] 1.3 In AGENTS.md's frontend conventions, add the rule:
  - content that needs data before it can render (lists, cards, text) shows a skeleton of its own shape from
    `Placeholder.tsx` until the first response
  - a control that's already drawn (button, input, dropdown) shows Mantine's `Loader` in its own place while what
    it offers loads, and stays usable for the rest
  - both keep their content through reloads

  Verify by reading it against `Placeholder.tsx` and design.md.

## 2. The receipt manager

- [x] 2.1 In `ReceiptManager.tsx`, treat `account === null` as loading:
  - placeholder rows in the list card and a bar for the count line
  - no empty state, footer or owner filter
  - `shown` empty

  Replace the `filterPending` "Loading…" line with the same rows. Verify in the browser:
  - with `/api/me` delayed, an admin sees placeholder rows, then owners. They never see rows without owners
    first (screenshot taken while it waits).
  - with `/api/users` delayed on `/receipts?owner={id}`, placeholder rows show, then that person's receipts
  - with `/api/users` failing, everyone's receipts show with the alert

  Result:
  - With `/api/me` held, no real row was drawn (a MutationObserver saw no row without an owner), and the
    placeholder rows are 67px, as a member's are. Once the account says admin, they're 91px, as the real rows are.
  - The owner filter appears only once the account is known. On a phone it then wraps below the title, which is the
    trade-off design.md accepts.
- [x] 2.2 While the people first load:
  - draw admin owner lines as a circle and a bar, not "…"
  - in the `OwnerPicker`, show a centered `Loader` inside `Loading` where the people go, above No owner, not
    "Loading people…"
  - in the owner filter `Select`, put a `Loader` in place of the chevron, with the placeholder "Loading people…",
    and keep it enabled

  Verify with `/api/users` delayed:
  - the owner lines show placeholders
  - the open picker shows the spinner, and No owner can still be chosen from it
  - the filter shows its spinner, and Everyone and No owner can be chosen
  - a known-but-missing owner still shows "Unknown user"

  Result: the filter's spinner is wrapped in `Loading`, so it's announced while the filter shows Everyone. design.md
  now says so.

## 3. A receipt and the splits page

- [ ] 3.1 In `ReceiptDetail.tsx`, replace the "Loading…" card with the placeholder card from design.md: the title,
  details and tax bars, and 5 line rows. Verify with `/api/receipts/{id}` delayed:
  - placeholders on the start page and on the receipt's own page, at both widths
  - while a receipt is being read, polling never shows placeholders again. Hold a receipt in Processing by updating
    the copy's status after the backend starts.
  - a failing load shows the error in the card
- [ ] 3.2 In `SplitsPage.tsx`, draw the header's title and summary as bars while loading, and one card of row bars
  where the table goes. Verify with the receipt route delayed: the header keeps its height when the title arrives,
  and nothing scrolls sideways at 390px.

## 4. End-to-end check

- [ ] 4.1 With every API route delayed by a second, open each page:
  - the start page
  - the start page with a receipt
  - a receipt's own page
  - the manager, as an admin and as a member (restart the backend with a member `Auth__DevUser`)
  - the splits page

  Confirm each shows placeholders and then content, in both schemes and at both widths. With
  `emulateMedia({ reducedMotion: 'reduce' })`, confirm no placeholder animates (the computed `animation-name` of
  the skeleton's `::after` is `none`). Confirm a placeholder area has `aria-busy="true"` and its hidden label. Run
  `dotnet test`, `npm run build` and `npm run lint`.

## Workflow follow-up

- Push and open a PR when the owner asks. After the merge, switch to master, pull, and delete the local branch.
- Archive the change before merging, in the PR, as `/opsx:archive`. That creates `openspec/specs/loading-states`
  and updates `openspec/specs/receipt-access`.
