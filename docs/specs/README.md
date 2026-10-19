# Specs

Design specs and RFCs, written before the implementation of a significant change (standard 23, rule 6). A spec holds:

- context and the problem,
- constraints and non-goals,
- options considered,
- a decision log (what was decided, when, by whom),
- open questions, each with an owner.

The flow is RFC, decision meeting, ADR. When a decision is made, record it in [`../adrs/`](../adrs/README.md) and mark the spec as
superseded by that ADR, or keep the spec current. A spec that describes a design the code no longer follows misleads people and
agents (rule 9).

Specs are explanation and design, in the Diátaxis sense. They are not how-to guides. Name files `YYYY-MM-DD-short-title.md` or
number them, and state the status at the top: `Draft`, `Accepted`, `Superseded by ADR NNNN`.

The template ships without specs. Plans and specs that an AI agent writes for itself (`docs/superpowers/`) may be kept, but
they do not replace ADRs.
