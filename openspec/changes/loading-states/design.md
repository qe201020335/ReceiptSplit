# Design

## Context

How each part of the client loads today:

| Part | Data | While it loads |
| --- | --- | --- |
| `ReceiptList` (start page) | the list `App` loads and polls | dimmed "Loading…" line |
| `ReceiptManager` | the same list | dimmed "Loading…" line |
| `ReceiptManager` | the account (`useAccount`) | nothing: `admin` is false until it arrives, so a member's rows are drawn first |
| `ReceiptManager` | the people (`api.listUsers`) | owner lines show "…", and a person's filter shows "Loading…" (#25) |
| `ReceiptDetail` | one receipt, polled while it's read | a card with "Loading…" until the first response; reloads keep the old receipt |
| `SplitsPage` | its receipts | "Loading…" below a header with no title |
| `App` header | the account | nothing; the avatar appears later |
| `OwnerPicker` | the people | "Loading people…" text |
| Owner filter `Select` | the people | only Everyone and No owner until they arrive, with no sign more are coming |

Every one of these already tells a first load (state still `null`) apart from a reload (state kept while the next
request runs), and shows the error in place when a load fails. This change only replaces what's drawn for the
`null` case.

Mantine's `Skeleton` pulses through a CSS animation that ignores `prefers-reduced-motion`. In light mode it's drawn
in `gray-3`, and in dark mode in `dark-4` (#39414f in this theme), on the card colors.

## Goals / Non-Goals

**Goals:**
- One small set of placeholder pieces, so each page's skeleton is a few lines and they all look alike.
- Placeholders sized like the content, so the page barely moves when the data arrives.
- The manager never draws a state it will contradict a moment later.

**Non-Goals:**
- Spinners for content. Content areas get skeletons; see "Skeletons for content, spinners in controls".
- A delay before placeholders appear. Skeletons were chosen over a delay.
- Loading feedback for actions: button `loading`, the upload form's photo spinner.
- Suspense or a data-fetching library. The effects with a `current` flag stay as they are.

## Decisions

### Skeletons for content, spinners in controls
- **Content areas get skeletons.** These are lists, cards and text, where the data decides the shape, so a
  placeholder of that shape keeps the page still.
- **Controls get a spinner.** A button, input or dropdown is drawn the same whatever the data, and only what it
  offers is waiting. Fake shapes inside it would add nothing, so it shows Mantine's `Loader` in its own place and
  stays usable for whatever doesn't depend on the data.
- **The header avatar is the exception:** a button whose content is the data, the initials. It keeps a skeleton
  circle. A spinner there would spin in the header of every view on every load, and a still grey circle that
  becomes the avatar draws the eye less.

### A shared `Placeholder.tsx`
A new `components/Placeholder.tsx` (+ `.module.css`) exports two pieces:
- `Loading({ label, children })`: the wrapper. It's a `div` with `aria-busy="true"`. Inside, Mantine's
  `VisuallyHidden` holds `label` (for example "Loading receipts"), followed by the skeleton children or a spinner.
  Every placeholder area and both spinners use it, so each one is announced the same way. That includes the filter
  `Select`'s spinner in its `rightSection`: the "Loading people…" placeholder text only shows while the filter has
  no value, and it usually shows Everyone.
- `Bar(props)`: Mantine's `Skeleton` with `animate` turned off when `useReducedMotion()` (from `@mantine/hooks`)
  is true. It takes the same props as `Skeleton`: `height`, `width`, `circle`, `radius`.

Two shapes repeat across pages, so they're built once:
- `ReceiptRowsPlaceholder({ rows, withOwner })`: rows shaped like a receipt summary. Each row has a title bar with
  an amount bar to its right, then a date bar with a badge bar, and optionally an owner line (a 22px circle and a
  bar). `ReceiptList` and `ReceiptManager` both use it.
- The header avatar is a 28px `Bar circle`, the size of `AccountMenu`'s avatar.

*Alternative:* write `Skeleton` inline everywhere. Rejected: every page would need its own reduced-motion check and
`aria-busy` wrapper, and they would drift apart.

### What each part draws
- **ReceiptList:** inside its card, 5 placeholder rows in place of the list.
- **ReceiptManager:**
  - The count line under the title becomes a 90px bar.
  - The list card holds a month heading bar, then 6 rows, inside the same `Card` the real list uses.
  - Owner lines are included once the account says admin. While the account itself loads, the rows have none.
- **ReceiptDetail:** inside the same card:
  - a title bar (about 40% wide, the h2's height)
  - a details bar (70%) and a tax bar (25%)
  - then 5 line rows, each a name bar with an amount bar on the right
- **SplitsPage:**
  - The header's title and summary become bars beside Back, so the header keeps its height. On a phone they wrap
    below Back, as the loaded ones do, with a second summary bar for several receipts.
  - The body is one card with 6 row bars where the table goes.
- **App header:** the avatar circle until the account arrives. The header no longer renders nothing in its place.
- **Owner lines (admin):** while the people first load, a 22px circle and a 90px bar replace "…" and the gray
  placeholder avatar. A known-but-missing owner still reads "Unknown user", as #25 made it.
- **OwnerPicker:** while the people first load, a centered `Loader` (size `sm`) where the people go, with a hidden
  "Loading people" label. No owner stays below it and can be chosen. A failed load keeps its message.
- **Owner filter `Select`:** while the people first load, a `Loader` (size `xs`) in `rightSection` in place of the
  chevron, and the placeholder "Loading people…". The `Select` stays enabled, so Everyone and No owner can be
  chosen. While a person's filter is pending, it shows that placeholder with no value, as #25 does.

Placeholder widths vary a little from row to row, using a fixed list rather than random numbers, so the list
doesn't look like a grid of identical bars and renders the same every time.

### The manager waits for the account
`ReceiptManager` treats `account === null` as loading. `useAccount` is only null before `/api/me` answers, or when
the sign-in can't be used, and in that case the sign-in problem page replaces the app. While the account loads:
- the list area shows the placeholder rows
- there's no empty state and no footer
- `shown` is empty, so nothing can be selected or acted on

The owner filter `Select` only appears once the account says admin. Until then the header has no filter, which a
member's header never has.

The person-filter wait from #25 (`filterPending`) draws the same placeholder rows instead of its "Loading…" line.

### Reloads
Each component shows placeholders only while its state is `null`, which is only before the first response.
Polling and reloads keep the old state, as they do now. A reload that fails while content is on screen keeps
whatever each component does today, an alert or a toast. Placeholders never come back.

## Risks / Trade-offs

- [The owner filter appears a moment after the header, for admins] → It can't be drawn before the account says who
  is signed in. The `Select` sits at the right of the header, or wraps below on a phone, so the title doesn't move.
  On a phone the header grows by one row.
- [Skeletons flash briefly on a fast LAN] → This was accepted when skeletons were chosen over a delay. They're
  sized like the content, so a brief flash doesn't move the page.
- [Placeholder rows don't match the real count] → A long list grows past them and a short one shrinks. That's
  ordinary, and better than an empty card.
