# 0006. Wolverine.HTTP endpoints in vertical slices

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0006.

## Context

We want one way to write endpoints, with business logic that can be tested without mocks, and all code for a feature in one place.

## Decision

- Endpoints are Wolverine.HTTP endpoints: one static class per operation, in `api/BccCode.Template/<Feature>/<Action><Thing>.cs`, named `<Action><Thing>Endpoint`. No MVC controllers.
- Endpoints follow the compound handler shape `LoadAsync` → `Permission` → `Validate` → `Handle` (standard 09.2, [0022](0022-default-deny-authorization-and-request-hardening.md)). `Permission`, `Validate` and `Handle` are pure and are tested by calling them directly. A handler returns its side effects (storage actions, cascaded messages) instead of calling the database.
- Each endpoint has its own `sealed record` request and response types. No `*Service` or `*Repository` types in slices (standard 10.5).
- Tests and frontend modules mirror the feature folders.

## Consequences

| | |
|---|---|
| Positive | Little ceremony per endpoint; pure functions that are easy to test; a feature can be read in one folder. |
| Negative | Wolverine conventions and code generation must be learned. |
| Follow-up | Wolverine code is pre-generated for production ([0012](0012-pre-generated-wolverine-code-in-production.md)). Architecture tests check the slice shape. |

## Alternatives considered

- **Minimal APIs** — fine for small apps, but no built-in compound handler, and the other BCC apps use Wolverine.
- **MVC controllers** — more ceremony and they encourage service layers.
