# Spec Delta

## ADDED Requirements

### Requirement: Admins can list users
An admin SHALL be able to list every user with their id, email and name, sorted by name and then email. A user
whose email was released is listed with a null email. Members SHALL get 403.

#### Scenario: Admin lists users
- **WHEN** an admin lists users after two members and the admin have signed in
- **THEN** all three are listed with their ids, emails and names, sorted by name

#### Scenario: Released email
- **WHEN** an admin lists users after releasing a user's email
- **THEN** that user is still listed, with a null email

#### Scenario: Member tries to list
- **WHEN** a member lists users
- **THEN** the response is 403
