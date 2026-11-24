---
title: "The template has no audit trail or history of business data"
status: needs-decision
decision-by: "the team, when the approach for relational audit and history is chosen"
effort: L
priority: P2
scope: general
area: privacy
---

# The template has no audit trail or history of business data

The template used to keep a typed `<table>_history` twin per audited table, a `change` table of business steps and an `actor`
table that mapped an alias to the person uid. They were taken out on purpose: a different approach will come later. Until then
a `Note` is a plain EF Core entity with `version` (concurrency token, 409) and `archived_at` (soft removal), and nothing records
who changed a row, when, or what it held before.

The A-team pilot (bcc-ateam ADR 0039) is the earlier design. The template carried a version of it; the commit before the removal has the
code (`git log -- api/BccCode.Template/Changes/`).

## Standards this leaves open

- **18.13** Changes to personal data and admin actions must record the actor and time. Nothing records them now. A note's
  `updated_at` and `archived_at` hold a time but not who.
- **18.14** The audit trail should be append-only and stored apart from tables the app updates. There is no audit trail.
- **18.6 and 18.7** (retention and erasure) were already open ([data inventory](../data-protection/data-inventory.md#anonymization-and-erasure)).
  Whatever replaces the history must be reachable by the erasure procedure.

Standard 12.12 (optimistic concurrency mapped to 409) stays met: `Note.Version` is an explicit integer concurrency token.

## Direction

Choose the mechanism (history tables, an audit table written from the endpoint, or an external sink), then add it with its
tests, the data inventory rows and the erasure steps. An app made from the template that handles personal data needs this
before go-live ([go-live runbook](../runbooks/go-live.md)).
