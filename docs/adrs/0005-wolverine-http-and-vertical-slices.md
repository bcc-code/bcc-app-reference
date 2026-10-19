# ADR 0005: Wolverine HTTP endpoints in vertical slices

- Status: Accepted
- Date: 2026-10-19

## Context

An endpoint, its input, its decision logic and its persistence change together. Layered folders (controllers, services,
repositories) spread one change over many files. Standards 09, 10 and 11 describe task-oriented endpoints in vertical slices
with pure decision functions.

## Decision

- Endpoints are Wolverine HTTP endpoints. One file or folder per use case under `Features/<Feature>/`, holding the endpoint,
  the request and response types, and the handler logic.
- Endpoints are named after the task (`POST /api/notes/{noteId}/archive`), not after CRUD verbs on a resource.
- The decision itself is a pure function that takes state and input and returns a result or an error, with no I/O. It is
  tested without a database. The endpoint loads the state, calls the function, and persists the outcome.
- Input values are parsed into value types at the boundary (for example `NoteTitle`). A failure becomes a 400 ProblemDetails
  with field errors. All errors leave the API as ProblemDetails.
- Slices do not reference each other. Shared code goes in the shared web project or a small `Shared` folder, and an
  architecture test enforces the rule.
- The OpenAPI document is generated from the endpoints and committed in `api/openapi/openapi.json`. Tests fail when the
  committed file is stale, and the frontend client is generated from it.

## Consequences

| Gain | Cost |
|---|---|
| A change to a use case touches one folder | Some duplication between slices is accepted instead of an early abstraction |
| Decision logic is tested in microseconds | The team must keep endpoints thin on purpose |
| The contract is the OpenAPI file, reviewed in the pull request | Wolverine conventions must be learned |

## Alternatives considered

- Minimal APIs with hand-written handlers: fewer concepts, but no built-in transactional middleware or messaging and more
  repeated wiring.
- MVC controllers and a service layer: familiar, but one change spreads over layers, and controllers attract mixed concerns.
