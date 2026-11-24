# 0013. Cloud Run behind the platform gateway, in a landing-zone project

- Status: Proposed
- Date: 2026-11-13
- Drafted with AI assistance; a person must review it before it is accepted.
- Adapted from bcc-ateam ADR 0014.

## Context

BCC apps run on Cloud Run in projects created by the platform's landing zone (`bcc-platform-deploy`), and public traffic goes through the shared platform gateway (standards 03, 04, 26). An earlier version of the template had its own workload identity pool, service accounts and state bucket; every real BCC app uses the landing zone's, so the template does too.

## Decision

We will:

- Provision the GCP project of each environment with the landing zone (`provision-project.yml`, project name `<app>`). The landing zone owns the project, the Terraform state, the CI service account `sa-gha-cicd` and its workload identity federation binding to the repository's GitHub environment. Our Terraform references `sa-gha-cicd` and does not create its own federation or keys. Terraform is applied only by the platform's `terraform.yml@main`, with an Azure backend and the `props` the platform passes in.
- Run the API as one `google_cloud_run_v2_service` (`<app>-api-<env>`) with its own runtime service account, built with `dotnet publish /t:PublishContainer` on `noble-chiseled-extra` (ICU and time zone data for Norwegian formatting). The image contains the built frontend and serves it in Static mode ([0004](0004-api-serves-the-frontend-on-one-origin.md)).
- Route the host's default to the API and only `/assets` and `/assets/*` to a private bucket with Cloud CDN, through the `gateway-backend` and `gateway-backend-bucket` modules pinned to a commit SHA (standard 26).
- Probe with TCP on the container port at startup and `/alive` for liveness. `/health` checks the database and answers with the status only, so it may be reachable through the gateway (standard 26.16).
- Set ingress to `INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER` from the first apply, in sandbox and prod. The first deploy therefore comes after the gateway route and `APP_BASE_URL` exist (standard 26.7). A Terraform precondition refuses public ingress in prod. The default `run.app` URL stays enabled, because Pub/Sub push and Cloud Scheduler use it ([0023](0023-pubsub-for-internal-messages-and-bcc-pubsub-for-events.md)).
- Bill sandbox per request and scale it to zero; nothing runs between requests ([0024](0024-wolverine-serverless-with-outbox-and-pubsub-push.md)).

The steps to bring a new environment up are in the [first-deploy runbook](../runbooks/first-deploy.md).

## Consequences

| | |
|---|---|
| Positive | Same platform pieces as other BCC apps; no long-lived keys; TLS, DNS and certificates come from the gateway. Scale to zero costs nothing when idle. |
| Negative | Until ingress is switched, the `run.app` URL bypasses the gateway. Cold starts are visible in sandbox. Infrastructure changes depend on the platform's reusable workflows, and the platform's `terraform.yml` cannot be pinned to a SHA. |
| Follow-up | The route PR in `bcc-platform-gateway` is a manual step per environment. Prod needs the `prod` GitHub environment with reviewers (standard 06.4). |

## Alternatives considered

- **Our own WIF pool and CI service account in this repo's Terraform** — duplicates what the landing zone already creates and binds to the repository; two owners for the same identity.
- **A Dockerfile** — not needed; the SDK container build gives a chiseled image without a file to maintain (standard 04.8).
- **Serve the assets from the API only** — simpler, but loses the CDN and the rollback guarantee of additive bundle uploads (standards 04.12, 26.11).
