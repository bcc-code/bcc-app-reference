# 0023. Google Pub/Sub for internal messages, BCC PubSub for integration events, with a local inbox toggle

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADRs 0026 and 0027.

## Context

An application sends two kinds of messages:

- **Internal messages**: commands and events between our own handlers, such as "send this email" or "follow up on this item in five days". No other service reads them. Their shape changes with our code (standard 13.5).
- **Integration events**: what other services consume, or what we consume from them, with versioned, documented contracts (13.4).

BCC PubSub is the platform's event bus and can carry both, but it accepts only public push URLs, has no dead-letter topics, uses one topic namespace for all clients, and delivers a message without required scopes to any client that subscribes to its type. An internal message there would leave our project, pass through another team's services and come back through the gateway, and be readable by other clients unless every message carries an app-specific scope.

## Decision

We will send internal messages through Google Pub/Sub in our own GCP project and use BCC PubSub only for integration events.

- *Internal messages* implement `IInternalMessage`. In GCP (`Messaging:UsePubSub=true`, set by Terraform) they go to one topic, `<app>-messages-<env>`, one envelope per Pub/Sub message, with a push subscription to `POST /internal/pubsub/{subscription}` (`WolverineFx.Pubsub.Http`), a dead-letter topic and a pull subscription to inspect it (04.15). Nothing pulls. A new message type needs a marker and a handler, no Terraform.
- **Local toggle.** `Messaging:UsePubSub` decides the transport, not the environment. Without it (local runs, integration tests) internal messages are routed to Wolverine's durable local inbox, so there is no emulator and no setup container. Handlers run in a later durability pass, so a chain of N messages takes N passes; the AppHost runs the pass in a short loop ([0024](0024-wolverine-serverless-with-outbox-and-pubsub-push.md)). The local path skips Google Pub/Sub push, which is only exercised in integration tests (they post push bodies) and in sandbox.
- *Integration events* (`IIntegrationEvent`, optional, with a versioned `EventType` such as `no.bcc.<app>.<thing>.v1`) go to BCC PubSub through `BccCode.PubSub.Wolverine` when an application adds them. BCC PubSub pushes to `/_bccpubsub/{topic}` with a BCC Signon token (`pubsub#deliver`). Platform events we consume are translated at the edge into our own internal message (13.14). The template ships the marker and no integration event.
- A handler that needs both cascades one internal message and one integration event. A routing test fails when a cascaded type has no route or its route does not match its marker.
- The push receivers use Wolverine's own envelope mapping, which resolves a `message-type` only against types in Wolverine's handler graph, never one named by the sender (13.3). A test pushes an unknown type and checks that nothing runs.
- **Internal callers.** `/internal/**` is outside the OpenAPI document. Each route requires its own policy on the `GoogleOidc` bearer scheme: Pub/Sub push (`sa-<app>-push-<env>`) and Cloud Scheduler (`sa-<app>-sched-<env>`) each have their own audience and service account, checked in the `email` claim with `email_verified` (14.16, 03.10). No policy accepts the session cookie. `Internal:AllowAll` is refused outside Development and Test. Missing settings stop startup.

## Consequences

| | |
|---|---|
| Positive | Internal work depends only on our project and Google. Internal messages are private by IAM, not by scope convention. Dead letters and caller identity follow the GCP standards. Local runs need no extra container for messaging. |
| Negative | Two transports to configure and explain, and a rule to follow (a routing test checks it). Terraform in this repo for a topic, a dead-letter topic, two subscriptions, a push service account and IAM. Local messaging is slower than GCP and does not use push. |
| Follow-up | Revisit if BCC PubSub gains private topics or dead-letter topics. An application that adds BCC PubSub registers an M2M client in BCC Signon and its subscriptions (a Cloud Scheduler call, because subscriptions expire). |

## Alternatives considered

- **BCC PubSub for everything** — one bus and no Pub/Sub Terraform, but an app-specific scope on every internal message, a public route for internal traffic, a dependency on another team's availability and no dead-letter topics.
- **Cloud Tasks for internal messages** — native delays and retries, but no Wolverine transport, so every message type needs hand-written enqueue and receive code.
- **Pub/Sub emulator locally** — a large image and a setup container; the local inbox is the same code path for retries and scheduling as GCP's outbox and pass.
- **Postgres queues in production** — a queue listener polls, which needs CPU between requests (standard 04.17).
