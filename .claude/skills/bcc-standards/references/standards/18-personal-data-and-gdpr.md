# 18 Personal Data and GDPR

This standard covers the engineering practices that support data protection in our repos: knowing what personal data an app processes, keeping as little as possible, deleting it on time, keeping it out of logs and client-readable stores, and recording who did what. It covers how the code and repo are set up. It does not decide legal basis, retention periods or whether a processing activity is lawful. Those decisions belong to BCC's data protection function, and engineers bring the facts to them.

## Background

Data protection is built in from the start of a project, not added as a checklist item at the end. Personal data must be deletable and exportable, and retrofitting that later is painful, so the design accounts for it from day one. Personal data handling is explicit and auditable: it is obvious what data the app stores and why. Store as little as possible, since data that is not collected cannot leak, and have a clear retention policy that the code enforces. A good outcome is a self-service flow where users export their data and request deletion without contacting support.

## Rules

### Inventory and review

1. **Every repo that processes personal data MUST keep a data inventory in `docs/data-protection/` listing each category of personal data, its purpose, where it is stored, its retention and deletion path, and who can access it.** It describes categories, never real people. A good inventory has one section per store, with retention and deletion path, and a list of open questions for the data protection function.
2. **The inventory MUST be verified with BCC's data protection/legal function and say so; an AI-drafted inventory MUST be marked as unverified until then.** Mark the status line as a draft for review, and open with a legal caveat. The inventory is not a compliance claim until reviewed.
3. **Features that add or change personal data processing MUST include a data protection section in the design spec or PR description: what data, why, where stored, retention, who sees it.** A PR template with this prompt makes it routine. The inventory is updated in the same PR.
4. **Legal basis and retention periods MUST be agreed with BCC's data protection function, not chosen by the team alone.** Record the decision (or the open question) in the inventory and link it from the ADR that implements it.

### Minimisation and retention

5. **Collect and keep only what a feature uses: request only needed OIDC scopes, slim session claims, and keep names and other identifiers out of read models, exports and client-readable stores where an id is enough.** See [14 Authentication and Sessions](14-authentication-and-sessions.md) rule 3. Where a feature promises to hide identities, keep names out of it entirely.
6. **Retention MUST be enforced automatically, either by a scheduled purge job with tests or by a platform TTL or lifecycle rule, not as a manual task.** A purge job deletes or anonymises data past its retention, records that it ran (an event or audit row), and is idempotent so a failed run can repeat. A platform rule (for example a bucket lifecycle rule or a database TTL) is defined in code and listed in the inventory as the deletion path. A daily purge with per-record retention, covered by integration tests of the purge and unit tests of the retention policy, is a suitable shape.
7. **Data subject deletion and export paths MUST exist where the inventory says they apply, and MUST work in production. Bulk or admin wipe tools MUST be guarded.** Data-subject deletion is the right to erasure and is never switched off in production. Bulk or admin wipe tools are admin-only, require a typed confirmation that matches the target, and MAY be disabled by config (returning 404 when disabled).

### Leak prevention

8. **Where a feature promises that some data is hidden (for example identities in a restricted view, or fields hidden from some roles), a test MUST assert that the data never appears in projections, exports or client-readable stores.** For example, an integration test checks every document of a record with hidden fields, and end-to-end probes crawl the client-readable store and the export file.
9. **Logs MUST NOT contain direct personal identifiers (names, e-mail, phone, national identity numbers) or message bodies; use trace ids and internal request ids for correlation.** See [22 Observability and Operations](22-observability-and-operations.md). A pseudonymous internal id (one that cannot be turned into a person without another system) MAY be logged where the data inventory lists it as a logged field, because support often traces a problem through it. Do not log a hash of a direct identifier as a substitute.
10. **Poison or rejected messages MUST be logged without their body.** Log message id, type, subscription, reason and body length only.
11. **Capability URLs (approval links, display tokens, signed links) MUST never be logged.** Anyone with log access could use them.
12. **Error and request logging MUST NOT log full URLs with query strings.** Query strings carry search terms and identifiers. Log the route template and method.

### Audit trail

13. **Changes to personal data and admin actions MUST record the actor and time, including the real user behind an impersonation.** Event-sourced apps put the actor in event data or metadata. Relational apps write an audit table from domain events and keep a `RealUserId` column for impersonation.
14. **The audit trail SHOULD be append-only and stored separately from application logs and from tables the app updates.** An audit record in the app's own store can be changed or purged by the app. Event-sourced apps can use the event store with a retention-aware design; relational apps need an insert-only table or an external sink.

### Test data

15. **Seed and test data MUST be generated, never copied from production.** Use a deterministic script with fixed seeds, seeders that generate organisations and records, or an emulator with fixtures and a seeder. See [19 Testing Strategy](19-testing-strategy.md).

## Examples

### Data inventory (rules 1, 2, 4)
Keep the inventory under `docs/data-protection/`. Its status line marks it as a draft that a human must verify. It has one section per store with retention and deletion path, and a list of open questions for the data protection team. A longer analysis can add a baseline, a target state, a migration plan and a legal caveat at the top.

### Retention purge (rule 6)
Document the decision in an ADR, put the code in a `Retention` folder, and cover it with an integration test for the purge and a unit test for the retention policy. The purge runs in one transaction and appends an event recording what was deleted.

### Guarded admin wipe tool (rule 7)
```csharp
/// <summary>Returns a 404 result when data deletion is disabled, otherwise null. 404 (not 403) keeps the surface invisible.</summary>
public static IResult? NotEnabledResult(AppOptions options) =>
    options.AllowDataDeletion ? null : TypedResults.NotFound();
```
The admin clear-user-data endpoint calls this first and also requires the confirmation to match the target id. The self-service erasure path for a data subject does not use this switch.

### Logging without bodies (rule 10)
```csharp
public const string PoisonMessage = "Poison Pub/Sub message {MessageId} ({MessageType}) on {Subscription}: {Reason}. Body length {BodyLength}";
```

### Leak tests (rule 8)
An integration test loads every stored document for a record with hidden fields and asserts that no hidden identifier or name appears. An end-to-end probe does the same against the client-readable store and the export file.

### Audit including impersonation (rule 13)
An `AuditLogger` is called from endpoint `After` steps and stores the effective user, the `RealUserId` of the person impersonating, the action and the time.

## Related
- [22 Observability and Operations](22-observability-and-operations.md) — logging rules and correlation ids
- [14 Authentication and Sessions](14-authentication-and-sessions.md) — slim claims and scopes
- [19 Testing Strategy](19-testing-strategy.md) — leak tests and generated test data
- [23 Documentation and Decisions](23-documentation-and-decisions.md) — ADRs and the inventory
- [Security principles](../principles/security.md)
