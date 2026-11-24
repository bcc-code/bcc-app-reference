# 0025. One feature project, with host-only infrastructure in the API

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0029 (Proposed there).

## Context

An earlier version of the template had one project per persistence variant, each with its own host, plus a shared `BccCode.Template.Web` project for HTTP plumbing. With one persistence stack, the split has no reason left. `BccCode.Template` should be the feature project, but code that only the host uses (sign-in sessions, Data Protection keys, security headers, the Pub/Sub receivers) is not feature code.

Constraints: standard 01.2 keeps one project for all slices and allows another project only for a separate deployable or a hard dependency boundary. Standards 01.3 and 10.3 say everything a feature needs lives in its folder, not in layer projects. Wolverine finds handlers and endpoints in the assembly the host names, and `codegen write` pre-generates them ([0012](0012-pre-generated-wolverine-code-in-production.md)). One storage registration is shared by the host, `db-apply` and the seeder (standard 12.7).

## Decision

We will keep `BccCode.Template` as the one feature project and put infrastructure that only the host uses in `BccCode.Template.Api`.

- **`BccCode.Template`** holds the slices (`Notes`, `Users`, `Admin`), the EF Core storage (`DbContext`, entities, `Change`, migrations) and the shared kernel the slices use (`DomainError`, `TypedProblem`, `Forbidden`, the message markers, claim helpers).
- **`BccCode.Template.Api`** is the host and composition root: `Program.cs`, authentication and sessions, Data Protection, cross-origin check, security headers and CSP, ProblemDetails, SPA hosting and the YARP proxy, the `/internal` receivers and their policies.
- **`BccCode.Template.TestData`** is the seeder: one project that registers the same storage as the API, guarded by `SeedingGate`, and run once (`Seeder:Once`). It is a separate project because it is a separate deployable (a CI step and an AppHost resource).
- The storage registration lives in the feature project and the host calls it, so the schema is complete whichever entry point runs `db-apply`.

## Consequences

| | |
|---|---|
| Positive | Host-only types stay out of the feature project. Handler discovery, `codegen write` and the feature folders are unchanged, and the template has no persistence variants to choose between. |
| Negative | Domain code and slices still share one assembly, so nothing but review and the architecture tests stops a rule class from taking a dependency on Wolverine or a platform client. |
| Follow-up | An architecture test for the domain core (NetArchTest or ArchUnitNET) if domain code starts to pick up infrastructure dependencies. |

## Alternatives considered

- **A `BccCode.Template.Domain` project** — the compiler would enforce the boundary, but each feature would span two projects, which standards 01.3 and 10.3 ask us to avoid.
- **A shared `BccCode.Template.Web` project (the earlier layout)** — it existed to share code between two hosts; with one host it is a layer project with no separate deployable behind it.
