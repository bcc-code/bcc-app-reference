# Architecture decision records

An ADR records one decision: why it was made and what follows from it. The format follows Nygard (standard 23, rule 7).

- File name: `NNNN-short-title-in-kebab-case.md`, numbered in the order they are written. Numbers are never reused.
- Sections: Status, Context, Decision, Consequences, Alternatives considered.
- Status: `Proposed`, then `Accepted` or `Rejected`, later `Superseded by NNNN`. Keep it current: an ADR that stays Proposed while
  the code follows it tells the reader nothing reliable.
- Write the ADR when the decision is made, before or together with the change, not after. Larger changes start as a spec in
  [`../specs/`](../specs/README.md), and the ADR records what was decided.
- Link to the ADR from the code or document that depends on it.

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-one-project-per-sample-variant.md) | One project per sample variant, host and features together | Accepted |
| [0003](0003-marten-or-ef-core.md) | Marten or EF Core: how to choose | Accepted |
| [0004](0004-bff-authentication-with-server-side-sessions.md) | BFF authentication with server-side sessions | Accepted |
| [0005](0005-wolverine-http-and-vertical-slices.md) | Wolverine HTTP endpoints in vertical slices | Accepted |
| [0006](0006-no-slopwatch.md) | No Slopwatch in the template | Accepted |

This list is kept by hand. When you add an ADR, add its row here.
