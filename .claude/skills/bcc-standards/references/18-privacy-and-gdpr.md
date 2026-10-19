# 18 Privacy and GDPR

The engineering practices that support data protection in our repos: knowing what personal data an app processes, keeping as little as possible, deleting it on time, keeping it out of logs and client-readable stores, and recording who did what. This document covers how the code and repo are set up. It does not decide legal basis, retention periods or whether a processing activity is lawful; those decisions belong to BCC's privacy function, and engineers bring the facts to them. See GDPR.

## Rules

### Inventory and review

1. **Every repo that processes personal data MUST keep a data inventory in `docs/privacy/` listing each category of personal data, its purpose, where it is stored, its retention and deletion path, and who can access it.** It describes categories, never real people. vote-rewrite's `docs/privacy/data-inventory.md` and orginn's `docs/gdpr/` are the models.
2. **The inventory MUST be verified with BCC's privacy/legal function and say so; an AI-drafted inventory MUST be marked as unverified until then.** vote-rewrite marks its inventory as an AI-assisted draft for review; orginn opens with a legal caveat. Neither is a compliance claim until reviewed.
3. **Features that add or change personal data processing MUST include a privacy section in the design spec or PR description: what data, why, where stored, retention, who sees it.** The inventory is updated in the same PR.
4. **Legal basis and retention periods MUST be agreed with BCC's privacy function, not chosen by the team alone.** Record the decision (or the open question) in the inventory and link it from the ADR that implements it.

### Minimisation and retention

5. **Collect and keep only what a feature uses: request only needed OIDC scopes, slim session claims, and keep names and other identifiers out of read models, exports and client-readable stores where an id is enough.** See [14 Authentication and Sessions](14-authentication-and-sessions.md) rule 3. vote-rewrite keeps names out of anonymous rounds entirely.
6. **Retention MUST be implemented as a scheduled purge job with tests, not as a manual task.** The job deletes or anonymises data past its retention, records that it ran (an event or audit row), and is idempotent so a failed run can repeat. vote-rewrite ADR 0013 describes a daily purge with per-record retention, tested in `PurgeTests` and `RetentionPolicyTests`.
7. **Data subject deletion and export paths MUST exist where the inventory says they apply, and destructive operations MUST be guarded.** bcc-privacy's clear-user-data endpoint is admin-only, returns 404 when deletion is disabled by config, and requires a typed confirmation that matches the target. Production deletion flags MUST be off unless deliberately enabled (bcc-privacy tracks this as tech debt).

### Leak prevention

8. **Where a feature promises that some data is hidden (anonymous modes, restricted fields), a test MUST assert that the data never appears in projections, exports or client-readable stores.** vote-rewrite's `AnonymousLeakTests` checks every Firestore document of an anonymous round, and the E2E `LeakProbes` crawl Firestore and the XLSX export.
9. **Logs MUST NOT contain personal identifiers (names, e-mail, phone, person ids) or message bodies; use trace ids and internal request ids for correlation.** See Don't Log Personal Identifiers. Where an identifier is logged on purpose (for example an admin id on a dead-letter operation), the inventory MUST say so.
10. **Poison or rejected messages MUST be logged without their body.** vote-rewrite logs message id, type, subscription, reason and body length only.
11. **Capability URLs (approval links, display tokens, signed links) MUST never be logged.** Anyone with log access could use them.
12. **Error and request logging MUST NOT log full URLs with query strings.** Query strings carry search terms and identifiers. Log the route template and method.

### Audit trail

13. **Changes to personal data and admin actions MUST record the actor and time, including the real user behind an impersonation.** vote-rewrite and bcc-privacy put the actor in event data/metadata; orginn writes an audit table from domain events and keeps `RealUserId` for impersonation. See Audit Logging.
14. **The audit trail SHOULD be append-only and stored separately from application logs and from tables the app updates.** Event-sourced apps can use the event store with a retention-aware design; relational apps need an insert-only table or an external sink.

### Test data

15. **Seed and test data MUST be generated, never copied from production.** vote-rewrite generates Core API data with a deterministic script (fixed seeds); orginn's `Backend/Orginn.DatabaseManager/Seeders` generate organisations, members and payments; bcc-privacy runs the Core API emulator with NDJSON fixtures and a seeder. See Realistic Test Data.

## Example

`docs/privacy/` (data inventory, marked as an unverified draft until the privacy function has reviewed it). The sample stores the OIDC `sub` as owner and never logs it. Source repo for retention and leak tests: vote-rewrite `docs/privacy/data-inventory.md`, `docs/adrs/0013-retention-and-purge.md`, `AnonymousLeakTests`.
