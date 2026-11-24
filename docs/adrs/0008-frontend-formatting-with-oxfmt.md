# 0008. Frontend formatting with oxfmt

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0009.

## Context

Formatting debates and whitespace-only diffs waste review time. Generated code must also come out formatted the same way every time, or regenerating the client produces noise.

## Decision

- **oxfmt** formats all frontend source. Its config sits in `frontend/.oxfmtrc.json`; we change it only to match the other BCC apps, not per developer taste.
- Hey API runs oxfmt on the generated client as a post-processing step, so the committed client is formatted the same way on every machine.
- Developers run oxfmt on save. CI runs `oxfmt --check` and fails on unformatted files.

## Consequences

| | |
|---|---|
| Positive | No formatting discussions in review; diffs show only real changes; fast enough to run on save. |
| Negative | oxfmt is younger than Prettier, so some editor integrations are less mature. |
| Follow-up | `format:check` runs in the `frontend` CI job. |

## Alternatives considered

- **Prettier** — mature, but slower, and the BCC standards and other apps use oxfmt.
