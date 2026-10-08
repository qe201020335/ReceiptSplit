# receipt-access Specification

## Purpose

Decides which receipts each user can see and act on, based on who owns them, and how requests that act on
several receipts at once are accepted or rejected as a whole.

## Requirements

### Requirement: Uploads are owned by the uploader
A receipt uploaded by a signed-in user SHALL be owned by that user. Receipts that existed before owners were
introduced SHALL be kept and have no owner.

#### Scenario: Upload
- **WHEN** a signed-in user uploads a receipt photo
- **THEN** the new receipt is owned by that user

#### Scenario: Existing receipts
- **WHEN** the app starts on a database with receipts from before owners existed
- **THEN** those receipts are kept, unchanged, with no owner

### Requirement: Members see and act on only their own receipts
A member SHALL see only receipts they own when listing receipts. Every per-receipt action SHALL treat a receipt
they don't own, including one without an owner, as missing (404). That covers reading it, its photo, editing,
changing the tax rate, extracting again and deleting.

#### Scenario: Listing
- **WHEN** a member lists receipts
- **THEN** only receipts they own are returned

#### Scenario: Someone else's receipt
- **WHEN** a member reads, edits, re-extracts, deletes or fetches the photo of a receipt owned by another user
- **THEN** the response is 404, the same as for a receipt that doesn't exist, and nothing changes

#### Scenario: Receipt without an owner
- **WHEN** a member opens a receipt that has no owner
- **THEN** the response is 404

### Requirement: Admins see and act on every receipt
An admin SHALL see every receipt when listing, whatever its owner or if it has none. An admin SHALL be able to
take every per-receipt action on any receipt.

#### Scenario: Listing as an admin
- **WHEN** an admin lists receipts
- **THEN** receipts owned by any user and receipts without an owner are all returned

#### Scenario: Acting on another user's receipt
- **WHEN** an admin edits or deletes a receipt owned by another user
- **THEN** the action succeeds as it would on their own receipt

### Requirement: Bulk receipt operations are all or nothing
A request that acts on several receipts SHALL either succeed for every receipt in it or change none. When any
receipt in the request can't be acted on, the request SHALL be rejected without changing anything, and the
response SHALL list the receipts that blocked it.

#### Scenario: One receipt blocks the rest
- **WHEN** a bulk request includes one receipt that can't be acted on
- **THEN** no receipt in the request is changed, and the response names the blocking one

### Requirement: Bulk delete
Deleting several receipts SHALL remove every receipt and photo and answer 204 when all can be deleted. Ids that
don't exist or aren't the user's SHALL answer 404 together, and receipts being read SHALL answer 409, each
listing the ids and deleting nothing. A request SHALL name 1 to 1000 receipts.

#### Scenario: All deletable
- **WHEN** a member deletes several receipts they own, none of them being read
- **THEN** all of them and their photos are deleted, and the response is 204

#### Scenario: Includes someone else's receipt
- **WHEN** a member's bulk delete includes a receipt owned by another user
- **THEN** the response is 404 listing that id, and none of the receipts are deleted

#### Scenario: Includes an unknown id
- **WHEN** a bulk delete includes an id that doesn't exist
- **THEN** the response is 404 listing that id, and none of the receipts are deleted

#### Scenario: Includes a receipt being read
- **WHEN** a bulk delete includes a receipt the model is reading
- **THEN** the response is 409 listing that id, and none of the receipts are deleted

#### Scenario: Empty request
- **WHEN** a bulk delete names no receipts
- **THEN** the response is 400

#### Scenario: Manager shows why nothing was deleted
- **WHEN** a bulk delete from the receipt manager is rejected
- **THEN** the manager says nothing was deleted and why, and reloads the list
