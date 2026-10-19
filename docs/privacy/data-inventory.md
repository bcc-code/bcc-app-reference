# Template: data inventory for the privacy team

Status: draft for review. This inventory was drafted with AI assistance from the design of the template's Notes sample. It must
be verified by a human against the code and the deployed environment, and by BCC's privacy function, before it is used. It is
a factual inventory, not legal advice and not the data protection assessment. It describes categories of data and never real
people (standard 18, rules 1 and 2).

When an application is made from this template, replace the Notes sample with the application's own data, keep this structure,
and update the inventory in the same pull request as every change to personal data processing.

## 1. Purpose and legal basis

The Notes sample lets a signed-in user keep personal notes. Purpose: to demonstrate the structure of the template. The
legal basis, the roles (controller, processor) and the information given to users are **not decided**. They are for BCC's
privacy function to agree (standard 18, rule 4). Open question 1 below.

## 2. Categories of data

| Category | Detail | Source | Where stored | Visible to |
|---|---|---|---|---|
| User identifier | The OIDC `sub` claim. Used as the owner of each note. A stable pseudonymous identifier, personal data | OIDC provider at login | Database (owner column or event data), session store | The API only. Never returned to the client |
| Display name | The `name` claim, shown in the UI | OIDC provider at login | Session only. Not copied into notes or events | The signed-in user |
| Roles | Role claims, for example the admin role | OIDC provider at login | Session only | The signed-in user (the roles shown by `GET /api/me`) |
| Note content | Title and body, free text written by the user. May contain personal data about the user or others, and the template cannot limit it | The user | Database | The owner only. Other users get 404 for a note they do not own, so its existence is not revealed |
| Note timestamps | Created, edited and archived times, from `TimeProvider` | The API | Database | The owner |
| Session data | Session id and the slim claims above, server-side (ADR 0004) | The API | Database or server-side session store | The API only |
| Data Protection keys | Keys that protect cookies and antiforgery tokens. Not personal data themselves | The API | Database, encrypted with a Cloud KMS key | The API only |
| Aggregate statistics | `activeNotes` and `archivedNotes` counts for `GET /api/admin/stats`. No identifiers | Computed from notes | Not stored, computed per request | Administrators |

Special-category data is not collected by design. Free text in notes can still contain it. The user interface should tell
users not to store such data in notes (open question 3).

The template has no email address, phone number, address or other contact data: the API requests only the OIDC scopes it needs
and does not keep the `email` claim.

## 3. Where data lives

- **Database:** Cloud SQL for PostgreSQL, one instance per environment, in the region of the project. Access for the application
  is through IAM database users (no passwords). Backups follow the Cloud SQL configuration (`infra/database.tf`).
- **Marten variant:** notes are an event stream (`NoteCreated`, `NoteEdited`, `NoteArchived`) with a projection. The events hold
  the owner id and the content as written.
- **EF Core variant:** one row per note with the owner id and the content.
- **Frontend:** the browser holds the session cookie and what the page shows. The SPA keeps no personal data in local storage.
- **Logs:** structured JSON in Cloud Logging.
- **Previews and local development:** generated test data only, never production data (standard 18, rule 15, standard 07).

## 4. Retention and deletion

| Data | Retention | Deletion path |
|---|---|---|
| Notes | Not decided (open question 2). Archiving hides a note from the user but does not delete it | None in the sample: there is no scheduled purge and no erasure endpoint |
| Sessions | Until logout, expiry or back-channel logout | Session expiry |
| Backups | Cloud SQL backup retention | Expire with the backup retention |

This is a gap against standard 18, rules 6 and 7: retention must be a scheduled purge job with tests, and a deletion path must
exist where the inventory says it applies. For the Marten variant, deleting a person's data from an event stream needs a stated
approach (delete the stream, or encrypt content per user and discard the key); events are not edited in place. The decision
belongs in an ADR once retention is agreed.

## 5. Logging rule

Logs must not contain personal identifiers (the `sub`, the display name, e-mail), note titles or bodies, or full URLs with
query strings (standard 18, rules 9 and 12). Log the route template, method, status, trace id and internal request id. Use
trace ids for correlation. The owner id is never logged. No identifier is logged on purpose in the template, so there are no
exceptions to list here.

## 6. Audit trail

Writes record the time. The Marten variant stores the stream events, which include the acting owner id. The EF Core variant
keeps timestamps on the row but no separate actor history. There are no administrator actions that change personal data in the
sample (the admin endpoint returns counts only), so no admin audit is needed for it yet (standard 18, rule 13).

## 7. Leak prevention

The ownership rule (a note of another user answers 404) is covered by tests that use two personas, `user` and `user2`
(standard 18, rule 8). The admin statistics endpoint returns counts only.

## 8. Open questions for the privacy team

1. Legal basis and roles for processing note content and the owner identifier.
2. Retention period for active and archived notes, and whether archiving should lead to deletion after a period.
3. Whether a notice about not storing special-category data in notes is enough, or the feature needs further limits.
4. Erasure on request: delete the notes of one user, and for the Marten variant how to treat the events.
5. Backup retention and its relation to deletion requests.
