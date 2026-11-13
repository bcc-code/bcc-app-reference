---
title: "Dead letters can only be seen and replayed with database and console access"
status: needs-decision
decision-by: "the team, when the first app made from the template needs it"
effort: M
priority: P2
scope: api
area: permissions
---

# Dead letters can only be seen and replayed with database and console access

Standard 13.11 requires dead letters to be visible and replayable from an admin-only UI or endpoint. [ADR 0024](../adrs/0024-wolverine-serverless-with-outbox-and-pubsub-push.md) puts failed messages in two places:

- Wolverine's dead letter table in Postgres, after the retries in the failure policy (kept 7 days).
- The Pub/Sub dead-letter topic, which only catches pushes that failed before a handler ran.

Both raise an alert (`infra/monitoring.tf`). To look at them and act, an operator follows [the dead-letter runbook](../runbooks/pubsub-dead-letters.md), which needs database access and the GCP console.

A fix needs the admin policy and a decision on where the endpoints live:

- Map Wolverine's dead letter endpoints (`MapDeadLettersEndpoints`) under `/api/admin/**`, which the admin policy covers by convention, with a test that a normal user gets 404 (see the `permission-tests` skill). Check that replay works under `ServerlessWithOutbox`: the durability pass may have no step that moves `replayable` rows back to the inbox.
- Shorten the runbook to use the endpoints.
- Keep the endpoints out of the shared `openapi.json` contract, or add them to it for both variants.
