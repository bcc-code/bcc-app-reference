# Architecture decision records

This folder records the decisions that shape this codebase: what we decided, why, and what it costs us. Read these before you change something that looks odd. The reason it is that way is often written down here.

## How we write them

- One decision per file, named `NNNN-short-title.md` with the next free number. Start from [0000-template.md](0000-template.md).
- Use the Nygard format: Status, Context, Decision, Consequences. Add "Alternatives considered" when there were real alternatives.
- Keep it short. A reader should understand the decision in two minutes. Link to specs, standards or issues instead of copying them.
- Write the ADR when the decision is made, in the same PR as the first code that follows it, not after.
- Status is one of `Proposed`, `Accepted`, `Rejected`, `Superseded by NNNN` or `Deprecated`. Never edit the Decision of an accepted ADR to say something else; write a new ADR that supersedes it and update the old one's status. `Deprecated` is for a decision that no longer applies and has no successor.
- A deviation from a MUST rule in the [BCC standards](../../.claude/skills/bcc-standards/SKILL.md) needs an ADR that says why.
- If an ADR was drafted with AI, say so under the status and have a person check it before it is accepted.

Larger changes that are hard to reverse, such as a new service, a new data store, an API contract change or an authentication change, start as a spec in `docs/specs/`. The ADR records the outcome.

Most of these ADRs are adapted from the BCC A-team application (bcc-ateam). The set starts at 0001 because the template is not deployed anywhere; see [0001](0001-record-architecture-decisions.md).

## Index

| # | Decision | Status |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Proposed |
| [0002](0002-repository-layout-and-stack.md) | Repository layout and stack | Proposed |
| [0003](0003-aspire-for-local-development.md) | .NET Aspire for local development | Proposed |
| [0004](0004-api-serves-the-frontend-on-one-origin.md) | The API serves the frontend on one origin | Proposed |
| [0005](0005-openapi-contract-and-generated-client.md) | Committed OpenAPI contract and generated client | Proposed |
| [0006](0006-wolverine-http-endpoints-in-vertical-slices.md) | Wolverine.HTTP endpoints in vertical slices | Proposed |
| [0007](0007-frontend-linting-with-oxlint-eslint-and-knip.md) | Frontend linting with oxlint, ESLint and knip | Proposed |
| [0008](0008-frontend-formatting-with-oxfmt.md) | Frontend formatting with oxfmt | Proposed |
| [0009](0009-translations-with-vue-i18n.md) | Translations with vue-i18n | Proposed |
| [0010](0010-frontend-type-checking-and-tests.md) | Frontend type checking and tests | Proposed |
| [0011](0011-bcc-component-library-and-tailwind.md) | BCC component library and Tailwind for UI | Proposed |
| [0012](0012-pre-generated-wolverine-code-in-production.md) | Pre-generated Wolverine code in production | Proposed |
| [0013](0013-cloud-run-behind-the-platform-gateway.md) | Cloud Run behind the platform gateway, in a landing-zone project | Proposed |
| [0014](0014-build-once-deploy-after-green-ci.md) | Build once and deploy after green CI, Terraform first | Proposed |
| [0015](0015-shared-platform-postgres-with-iam-auth.md) | Shared platform Postgres with IAM authentication | Proposed |
| [0016](0016-schema-applied-by-a-deploy-step.md) | Schema applied by a deploy step | Proposed |
| [0017](0017-integration-and-e2e-tests.md) | Integration tests with Testcontainers, E2E tests with Playwright on the AppHost | Proposed |
| [0018](0018-c4-diagrams-with-structurizr.md) | C4 diagrams as Structurizr DSL, exported to SVG by a script, checked by hash in CI | Proposed |
| [0019](0019-bff-sign-in-with-bcc-signon.md) | BFF sign-in with BCC Signon, with PAR | Proposed |
| [0020](0020-server-side-sessions-with-back-channel-logout.md) | Server-side sessions in EF Core with back-channel logout | Proposed |
| [0021](0021-data-protection-keys-in-postgres-with-kms.md) | Data Protection keys in Postgres, wrapped with Cloud KMS by in-house code | Proposed |
| [0022](0022-default-deny-authorization-and-request-hardening.md) | Default-deny authorization, cross-origin checks and no CORS | Proposed |
| [0023](0023-pubsub-for-internal-messages-and-bcc-pubsub-for-events.md) | Google Pub/Sub for internal messages, BCC PubSub for integration events, with a local inbox toggle | Proposed |
| [0024](0024-wolverine-serverless-with-outbox-and-pubsub-push.md) | Wolverine ServerlessWithOutbox with Pub/Sub push | Proposed |
| [0025](0025-one-feature-project-with-a-host-only-api.md) | One feature project, with host-only infrastructure in the API | Proposed |
| [0026](0026-telemetry-export-through-the-bcc-monitoring-collector.md) | Telemetry export through the BCC monitoring collector, best effort under request-based billing | Proposed |
| [0027](0027-pr-previews-in-a-preview-project.md) | PR previews run in a preview project, one Postgres schema per PR | Proposed |
| [0029](0029-timeprovider-as-the-clock.md) | TimeProvider as the clock | Proposed |
| [0030](0030-fewer-ci-jobs-to-cut-billed-minutes.md) | Fewer CI jobs and no second run on main, to cut billed Actions minutes | Proposed |
| [0031](0031-entra-id-sign-in-next-to-bcc-signon.md) | Microsoft Entra ID sign-in next to BCC Signon, with app-issued person ids and stored admin grants | Proposed |

This list is kept by hand. When you add an ADR, add its row here.
