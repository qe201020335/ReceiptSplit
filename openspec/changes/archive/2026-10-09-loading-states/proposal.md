# Proposal

## Why

The client signals loading in three ways, and it misses some places:
- some components show a dimmed "Loading…" line
- some show nothing, and the page shifts when data arrives
- the receipt manager briefly draws a member's page for an admin, then redraws it with owners and the filter

Since the manager now depends on who's signed in, that shift is visible on every visit. A `/receipts?owner=` link
also shows everyone's receipts for a moment.

## What Changes

- While a content area's first data loads, it shows skeleton placeholders shaped like the content (Mantine
  `Skeleton`), not a "Loading…" line or nothing. That covers:
  - the start page's receipt list
  - the receipt manager
  - a receipt, on the start page and on its own page
  - the splits page
  - the header's account avatar
  - an admin's owner lines while the people load
- Controls that are already drawn but wait for what they offer show a spinner (Mantine `Loader`) in their own
  place and stay usable for the rest:
  - the owner picker's people
  - the owner filter while the people load
- The receipt manager waits for the signed-in account before drawing rows. Admins never see a member's layout
  first, and a `?owner=` filter never shows everyone's receipts in the meantime.
- Only first loads show placeholders. Polling while receipts are read, and reloads after an action, keep the
  current content on screen.
- A failed load replaces the placeholders with the error, as the "Loading…" line was replaced before.
- Placeholders tell screen readers the area is loading (`aria-busy`, with hidden "Loading…" text). Their pulse
  stops when reduced motion is preferred.
- AGENTS.md's frontend conventions gain the rule: content that needs data before it can render shows a skeleton
  of its own shape, and a control that's already drawn shows a spinner in its own place, until the data arrives.

## Capabilities

### New Capabilities

- `loading-states`: how the client behaves while the data a page needs is loading. That covers placeholders for
  content and spinners in controls on first load, keeping content through reloads, errors in their place, and
  accessibility.

### Modified Capabilities

- `receipt-access`: the receipt manager shows placeholders until it knows whether the signed-in user is an admin,
  and while an admin's person filter waits for the people.

## Impact

- **Client:**
  - `App.tsx`, the header avatar
  - `ReceiptList.tsx`
  - `ReceiptManager.tsx`
  - `ReceiptDetail.tsx`
  - `SplitsPage.tsx`
  - `OwnerPicker.tsx`
  - a small shared placeholder component and its CSS
- **No backend or API change.**
- **Docs:** AGENTS.md's frontend conventions.
- **Out of scope:** feedback for actions (button `loading`, the upload form's photo spinner) stays as it is.
