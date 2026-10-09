# Spec Delta

## MODIFIED Requirements

### Requirement: The receipt manager shows owners to admins
For an admin, each receipt in the receipt manager SHALL show its owner as an initials avatar and name, falling back
to the email, or as "No owner". For a member, the manager SHALL show no owners, no Reassign button and no owner
filter. Until the manager knows whether the signed-in user is an admin, it SHALL show placeholders, not a member's
rows.

#### Scenario: Admin
- **WHEN** an admin opens the receipt manager
- **THEN** every receipt shows its owner's avatar and name, or "No owner"

#### Scenario: Member
- **WHEN** a member opens the receipt manager
- **THEN** no receipt shows an owner, and there is no Reassign button or owner filter

#### Scenario: Account still loading
- **WHEN** an admin opens the receipt manager before their account has loaded
- **THEN** the manager shows placeholder rows, and never rows without owners first

### Requirement: Admins filter the receipt manager by owner
For an admin, the manager SHALL offer an owner filter: Everyone, No owner, then each person, each with how many
receipts it matches. The filter SHALL be kept in the page's URL, and changing it SHALL NOT add a history entry. An
owner value that names no one SHALL show everyone. Members SHALL get no filter, and any owner value is ignored.

#### Scenario: No owner
- **WHEN** an admin chooses No owner in the filter
- **THEN** only receipts without an owner are listed, and the URL is `/receipts?owner=none`

#### Scenario: Back from a receipt
- **WHEN** an admin filtering by a person opens a receipt and goes back
- **THEN** the manager still filters by that person

#### Scenario: Unknown owner in the URL
- **WHEN** an admin opens `/receipts?owner=` followed by an id no user has
- **THEN** every receipt is listed, with the filter on Everyone

#### Scenario: A person's filter while the people load
- **WHEN** an admin opens `/receipts?owner=` followed by a user's id before the people have loaded
- **THEN** the manager shows placeholder rows, not every receipt or none, until it can show that person's receipts

#### Scenario: The people can't be loaded
- **WHEN** an admin opens `/receipts?owner=` followed by a user's id and the people fail to load
- **THEN** every receipt is listed with the filter on Everyone, and the manager says the people couldn't be loaded
