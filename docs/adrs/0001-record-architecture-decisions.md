# ADR 0001: Record architecture decisions

- Status: Accepted
- Date: 2026-10-19

## Context

Decisions about structure, data and infrastructure are asked about months later by people and by AI agents that were not in
the room. Chat threads and pull request comments do not survive as a reference.

## Decision

Significant decisions are recorded as ADRs in `docs/adrs/`, in the format described in [README.md](README.md). A decision is
significant when changing it later would be expensive, or when a reader of the code would otherwise ask why.

## Consequences

| Gain | Cost |
|---|---|
| New developers and agents find the reasoning next to the code | A short document per decision, and the status must be kept current |
| Reversing a decision is explicit: a new ADR supersedes the old one | The list in the README is maintained by hand |

## Alternatives considered

- Decisions only in pull request descriptions: searchable in GitHub, but not versioned with the code and not found by agents
  that read the repository.
- A wiki: drifts away from the code.
