# 0005. Committed OpenAPI contract and generated client

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0005.

## Context

Hand-written frontend types and fetch calls drift from the API without anyone noticing. Contract changes should show up in review as a diff. An earlier version of the template wrote the document as a side effect of a Debug build, which made every build a file write and hid the step from people who run only the frontend scripts.

## Decision

- The API generates its OpenAPI document (`Microsoft.AspNetCore.OpenApi`, written by `jasper openapi`, run from `pnpm --dir frontend run generate`) to `api/BccCode.Template.Api/BccCode.Template.Api.json`. The file is committed. No build writes it.
- The document is byte-stable: servers are cleared, paths, responses and tags are sorted ordinally and line endings are LF. Tags and operation ids come from one convention (tag from the feature folder, operation id from the endpoint class name without `Endpoint`), not set by hand.
- The frontend client is generated from that file with Hey API (`pnpm --dir frontend run openapi-ts`) into `frontend/src/client/` and committed. Our own API is never called with hand-written `fetch`.
- Server state goes through TanStack Query, with `queryOptions` factories per feature that call the generated SDK. We do not use Hey API's TanStack Query plugin, matching the other BCC apps.
- `/internal/**` routes are not in the document ([0023](0023-pubsub-for-internal-messages-and-bcc-pubsub-for-events.md)).

## Consequences

| | |
|---|---|
| Positive | Typed client that matches the API; contract changes visible in PRs; method names stay stable when files move. |
| Negative | Two generated artefacts to keep committed and current; a backend change needs a `generate` run before the frontend sees it. |
| Follow-up | CI fails when the committed document or client differs from what the code produces (standard 08.5). |

## Alternatives considered

- **Hey API's TanStack Query plugin** — generates query hooks, but the other BCC apps write `queryOptions` by hand, which keeps query keys under our control.
- **NSwag or Kiota** — the BCC standards and existing apps use Hey API.
- **Writing the document in every Debug build** — what the template did before; it hides the step and needs MSBuild properties that only exist for this.
