# Durability pass failing

Alerts:

- `bcc-template <env>: durability pass failed`: the pass could not run 5 or more times in 10 minutes.
- `bcc-template <env>: Cloud Scheduler job failed`: the job `template-durability-pass-<env>` failed 3 or more times in
  30 minutes.

How it works: [ADR 0024](../adrs/0024-wolverine-serverless-with-outbox-and-pubsub-push.md). A message is stored in the outbox in the
handler's transaction and sent to Pub/Sub right after the commit. When that send fails, the row stays. Cloud Scheduler calls
`POST /internal/durability/pass` every minute (locally an AppHost loop does); the pass sends rows older than 60 s and deletes expired envelopes and dead
letters. While the pass fails, messages whose first send failed are delayed, not lost.

1. Look at the job and its recent attempts:

   ```bash
   gcloud scheduler jobs describe template-durability-pass-<env> --location <region> --project <project>
   gcloud logging read 'resource.type="cloud_scheduler_job" AND resource.labels.job_id="template-durability-pass-<env>" AND severity>=ERROR' \
     --project <project> --freshness 1h --limit 20 --format='value(timestamp,jsonPayload.status,jsonPayload.url)'
   ```

2. Typical causes:
   - **401 or 403**: the token was refused. Check `Internal__SchedulerAudience` and `Internal__SchedulerServiceAccount` on
     the service against the job's OIDC settings (`infra/scheduler.tf`), and that the Cloud Scheduler service agent has
     `roles/iam.serviceAccountTokenCreator` on the scheduler account.
   - **500**: a step of the pass failed; the exception is in the API log. Usually the database (see
     the [health check runbook](health-check-failed.md)) or Pub/Sub publishing (check that the API
     account still has `roles/pubsub.publisher` on the topic).
   - **Deadline exceeded**: the pass took longer than 60 s, usually because the outbox grew while publishing failed. It
     catches up over the next runs once the cause is fixed.
   - **Lock not taken**: another pass holds the PostgreSQL advisory lock. Normal now and then; the lock is transaction-scoped, so it is safe
     behind a transaction-mode pooler ([ADR 0015](../adrs/0015-shared-platform-postgres-with-iam-auth.md)); persistent means a pass that never finishes.

3. See how much is waiting in the outbox (as a member of the database admin group, through the Auth Proxy):

   ```sql
   select count(*), min("timestamp") from <app schema>.wolverine_outgoing_envelopes;
   ```

4. Run one pass by hand once the cause is fixed:

   ```bash
   gcloud scheduler jobs run template-durability-pass-<env> --location <region> --project <project>
   ```

5. If the job is paused or deleted, re-apply Terraform for the environment.
