# 0001. Record architecture decisions

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.

## Context

Decisions about structure, libraries and conventions get made in meetings, chats and pull requests and are then forgotten. Later readers, people and coding agents alike, see the result without the reason and either copy a pattern blindly or remove it without knowing what it protected.

This repository is a template. Applications made from it inherit its decisions, so the reasons have to travel with the code.

## Decision

We will record decisions as architecture decision records in `docs/adrs/`, in the Nygard format (Status, Context, Decision, Consequences), as described in the [README](README.md). An ADR is written when the decision is made and kept up to date through its status.

The set was renumbered from 0001 when the template was aligned with the BCC A-team application ([bcc-ateam](https://github.com/bcc-code/bcc-ateam), whose ADRs most of these are adapted from). The template is not deployed anywhere, so no earlier number is referenced from outside this repository. An application made from the template keeps these numbers and continues from the next free one.

## Consequences

| | |
|---|---|
| Positive | The reason for a decision lives next to the code and shows up in review. Agents can read it before changing things. |
| Negative | Writing an ADR takes some minutes per decision, and statuses must be maintained. |
| Follow-up | Deviations from MUST rules in the BCC standards are recorded here. An application made from the template reviews the set and sets the status of the ones it does not follow to `Deprecated` or `Superseded`. |
