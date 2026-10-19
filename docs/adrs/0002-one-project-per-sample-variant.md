# ADR 0002: One project per sample variant, host and features together

- Status: Accepted
- Date: 2026-10-19

## Context

Standard 01, rule 2 asks for a host project that holds the composition root and a separate feature project that holds the
domain and the slices. The template carries two complete variants of the same sample (Marten in
`BccCode.Template.Api.Marten`, EF Core in `BccCode.Template.Api.EfCore`) that must stay small enough to read and to keep
identical in behaviour. A host and a feature project per variant would give four projects plus the shared web project for one
small sample.

## Decision

Each variant is one project that contains the composition root (`Program.cs`, configuration) and the features (`Features/`,
one folder per slice). HTTP plumbing that does not depend on persistence lives in `BccCode.Template.Web` and is shared by both
variants. This deviates from standard 01, rule 2.

Split the project into a host and a feature project when one of these happens:

- A second host needs the features (for example a worker that handles messages from the same domain).
- The feature code grows past what one person can hold in their head, or build times in the project become noticeable.
- You want the compiler to enforce that features do not reference host configuration.

Until then, `BccCode.Template.ArchitectureTests` enforce the rules that the split would give for free: slices do not reference
each other, and only the composition root registers infrastructure.

## Consequences

| Gain | Cost |
|---|---|
| Two parallel samples that are easy to compare | A feature can reach host internals; only the architecture tests prevent it |
| Fewer projects to build, test and rename | The split later is a mechanical but real refactoring |
| `scripts/init-app.ps1` has one project to rename per variant | |

After `scripts/init-app.ps1` the project is called `BccCode.<Name>.Api`.

## Alternatives considered

- Follow standard 01 literally: correct for a real application, too heavy for a sample whose point is the contrast between
  two persistence choices.
- One shared feature project with persistence behind interfaces: hides exactly the difference the template is meant to show
  and adds an abstraction that neither variant needs.
