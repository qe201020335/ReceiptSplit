# Spec Delta

## Purpose

Describes what the client shows while the data a page needs is still loading, so pages hold their shape instead of
jumping, never draw a misleading state first, and fall back to an error in place when a load fails.

## ADDED Requirements

### Requirement: First loads show placeholders shaped like the content
While the data a content area needs is loading for the first time, the area SHALL show skeleton placeholders of
the content's shape and size instead of the content, a "Loading…" line, or nothing. Content areas are the receipt
lists, a receipt, the splits page, admins' owner lines and the header's account avatar.

#### Scenario: Start page list
- **WHEN** the start page opens and the receipts haven't loaded yet
- **THEN** the list shows placeholder rows where receipts will be

#### Scenario: A receipt
- **WHEN** a receipt is opened and it hasn't loaded yet
- **THEN** its card shows placeholders for the store name, the details under it and the lines

#### Scenario: Splits page
- **WHEN** the splits page opens and its receipts haven't loaded yet
- **THEN** the header shows placeholders for the title and summary, and the page shows placeholder rows for the
  table

#### Scenario: Header avatar
- **WHEN** the app opens and the signed-in account hasn't loaded yet
- **THEN** the header shows a round placeholder where the avatar will be, so it doesn't shift when the avatar appears

### Requirement: Controls waiting for data show a spinner
A control that is already drawn but waits for the data it offers, such as an input or a dropdown, SHALL show a
spinner in its own place while that data loads for the first time, instead of placeholder shapes. It SHALL stay
usable for whatever doesn't depend on that data.

#### Scenario: Owner picker
- **WHEN** an admin opens the owner picker before the people have loaded
- **THEN** it shows a spinner where people will be, and still offers No owner

#### Scenario: Owner filter
- **WHEN** an admin opens the receipt manager before the people have loaded
- **THEN** the owner filter shows a spinner in place of its arrow and says it's loading people, and Everyone and No
  owner can still be chosen

### Requirement: Reloads keep what's on screen
Once a part of the page has shown its data, reloading that data SHALL keep the current content on screen until the
new data replaces it. That includes polling while receipts are read and reloading after an action. Placeholders
SHALL NOT come back.

#### Scenario: Polling while a receipt is read
- **WHEN** an open receipt is being read and the client polls it
- **THEN** the receipt stays on screen between polls, with no placeholders

#### Scenario: Reloading after an action
- **WHEN** the receipt list reloads after receipts are deleted or reassigned
- **THEN** the rows stay on screen until the new list arrives

### Requirement: A failed first load shows its error in place
When a first load fails, the placeholders SHALL be replaced by the error. The page SHALL NOT keep loading forever or
show an empty state that suggests there is nothing.

#### Scenario: Receipt fails to load
- **WHEN** a receipt's first load fails
- **THEN** its card shows the error where the placeholders were

### Requirement: Placeholders are announced and respect reduced motion
An area showing placeholders or a spinner SHALL be marked busy and include text that screen readers announce as
loading, hidden from view. When the user prefers reduced motion, placeholders SHALL NOT pulse.

#### Scenario: Screen reader
- **WHEN** a screen reader reaches a list showing placeholders
- **THEN** it is told the area is busy and is loading

#### Scenario: Reduced motion
- **WHEN** the system asks for reduced motion and a page shows placeholders
- **THEN** they are drawn without animation
