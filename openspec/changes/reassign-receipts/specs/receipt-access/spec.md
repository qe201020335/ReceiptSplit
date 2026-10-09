# Spec Delta

## ADDED Requirements

### Requirement: Receipt summaries name their owner
Each receipt in the receipt list SHALL carry its owner's user id, or null when it has no owner.

#### Scenario: Owned receipt
- **WHEN** a user lists receipts after uploading one
- **THEN** that receipt's summary carries their user id as its owner

#### Scenario: Receipt without an owner
- **WHEN** an admin lists receipts that include one without an owner
- **THEN** that receipt's summary has a null owner

### Requirement: Admins reassign receipts
An admin SHALL be able to give one or more receipts a new owner, or set them to no owner, in one request. The
request SHALL answer 204 once every receipt has the new owner. Reassigning SHALL be allowed in every status,
including while the model reads the receipt, and SHALL change nothing but the owner.

#### Scenario: To another user
- **WHEN** an admin reassigns a member's receipt to another user
- **THEN** the response is 204, the new owner lists and opens it, and the previous owner gets 404 for it

#### Scenario: A receipt without an owner
- **WHEN** an admin reassigns a receipt without an owner to a member
- **THEN** the member lists and opens it

#### Scenario: Back to no owner
- **WHEN** an admin reassigns a member's receipt to no owner
- **THEN** the member no longer lists it and gets 404 for it, and admins still list it with a null owner

#### Scenario: Receipt being read
- **WHEN** an admin reassigns a receipt while the model reads it, and the extraction then finishes
- **THEN** the receipt keeps the new owner and has the extraction's result

#### Scenario: Several receipts with different owners
- **WHEN** an admin reassigns several receipts, some owned by different users and some without an owner, to one
  user
- **THEN** all of them are owned by that user

### Requirement: Reassigning is refused as a whole
A reassign request SHALL change nothing when it can't be done for every receipt in it. Ids that don't exist SHALL
answer 404 listing them. A new owner that isn't a user SHALL answer 400. Members SHALL get 403 whatever the ids.
A request SHALL name 1 to 1000 receipts and SHALL state the new owner, which may be null for no owner.

#### Scenario: Includes an unknown id
- **WHEN** an admin reassigns several receipts and one id doesn't exist
- **THEN** the response is 404 listing that id, and no receipt changes owner

#### Scenario: Unknown user
- **WHEN** an admin reassigns receipts to an id no user has
- **THEN** the response is 400, and no receipt changes owner

#### Scenario: Member tries to reassign
- **WHEN** a member reassigns their own receipt or someone else's
- **THEN** the response is 403, and nothing changes

#### Scenario: Empty request
- **WHEN** a reassign request names no receipts
- **THEN** the response is 400

#### Scenario: New owner left out
- **WHEN** a reassign request doesn't state the new owner
- **THEN** the response is 400, and no receipt is set to no owner

### Requirement: The receipt manager shows owners to admins
For an admin, each receipt in the receipt manager SHALL show its owner as an initials avatar and name, falling back
to the email, or as "No owner". For a member, the manager SHALL show no owners and look as it did before owners
were shown.

#### Scenario: Admin
- **WHEN** an admin opens the receipt manager
- **THEN** every receipt shows its owner's avatar and name, or "No owner"

#### Scenario: Member
- **WHEN** a member opens the receipt manager
- **THEN** no receipt shows an owner, and there is no Reassign button or owner filter

### Requirement: Admins reassign a receipt from its row
For an admin, each receipt in the manager SHALL have a Reassign button. It opens a picker that searches people by
name or email and always offers No owner. The current owner is marked and can't be chosen. Choosing applies the
change at once and says who the receipt was reassigned to. A failure says the receipt wasn't reassigned and why.

#### Scenario: Searching
- **WHEN** an admin opens a receipt's Reassign picker and types part of a person's email
- **THEN** the picker lists the people whose name or email contains it, ignoring case, along with No owner

#### Scenario: Choosing someone
- **WHEN** an admin chooses a person in a receipt's picker
- **THEN** the receipt is reassigned to them without a confirmation, a notification says "Reassigned to" their
  name, and the row shows them as the owner

#### Scenario: Keyboard
- **WHEN** an admin opens the picker and uses the arrow keys and Enter
- **THEN** they can choose a person without the mouse, and Escape closes the picker without changing anything

### Requirement: Admins reassign selected receipts after confirming
For an admin, the selection footer SHALL offer Reassign with the number of selected receipts. It opens the same
picker. Choosing someone SHALL ask for confirmation, saying how many receipts each current owner, or no owner,
has among them. Only after it's confirmed are they all reassigned in one all-or-nothing request.

#### Scenario: Confirmation lists what changes
- **WHEN** an admin selects 7 receipts without an owner and 3 of Alice's, and chooses Carol in the footer's picker
- **THEN** a confirmation asks to reassign 10 receipts to Carol and says 7 have no owner and 3 are Alice's

#### Scenario: Cancelled
- **WHEN** the admin cancels that confirmation
- **THEN** nothing is reassigned, and the selection stays

#### Scenario: Rejected
- **WHEN** a confirmed bulk reassign is rejected
- **THEN** the manager says nothing was reassigned and why, and reloads the list

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

### Requirement: Bulk actions act only on receipts the manager shows
The selection footer's actions SHALL act only on selected receipts that are listed under the current filter.
Receipts that a filter hides, that were deleted, or whose owner changed out of the filter SHALL NOT be acted on or
counted.

#### Scenario: Filter hides a selected receipt
- **WHEN** an admin selects receipts under Everyone and then filters by No owner
- **THEN** the footer counts and acts on only the selected receipts without an owner

### Requirement: Receipts being read can be selected, but not deleted
Every receipt in the manager SHALL be selectable, including one the model is reading. While any selected receipt
is being read, Delete SHALL look disabled. Clicking it SHALL show a notification saying how many selected
receipts are being read and can't be deleted yet, and SHALL NOT send a request.

#### Scenario: Selecting a receipt being read
- **WHEN** a user selects a receipt the model is reading along with others
- **THEN** it is selected, and Delete looks disabled

#### Scenario: Clicking the disabled Delete
- **WHEN** the user clicks Delete with that selection
- **THEN** a notification says 1 selected receipt is being read and can't be deleted yet, and nothing is deleted

#### Scenario: Reading finishes
- **WHEN** the list reloads after that receipt is read
- **THEN** Delete is enabled again for the same selection
