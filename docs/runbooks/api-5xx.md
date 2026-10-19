# API 5xx

Alert: `template <env>: api 5xx ratio above 1 %`. More than 1 % of requests answered 5xx over 5 minutes.

1. Is it still happening? Open the Cloud Run service `template-api-<env>` in the console, Metrics, and check the request count
   by response code class.
2. Was there a deploy? Revisions tab: a new revision shortly before the alert is the first suspect. If so, roll back
   ([rollback.md](rollback.md)).
3. Read the errors:

   ```bash
   gcloud logging read 'resource.type="cloud_run_revision" AND resource.labels.service_name="template-api-<env>" AND severity>=ERROR' \
     --project <project> --freshness 30m --limit 50 --format='value(timestamp,jsonPayload.message)'
   ```

   Look at the exception type and the route template. Logs carry no personal identifiers or request bodies, so use the trace id
   (`logging.googleapis.com/trace`) to follow one request.
4. Common causes:
   - Database unreachable or out of connections: see [database-cpu.md](database-cpu.md) and the Cloud SQL instance's status.
     `/health` fails too, so check [health-check-failed.md](health-check-failed.md).
   - A secret or the KMS key is not readable (permission change, disabled key version): the API fails at startup or on the first
     request that needs it. Check the Cloud Run revision's startup logs.
   - The schema is older than the code: the migration job failed or was skipped ([migration-failed.md](migration-failed.md)).
   - The OIDC provider is down: login routes fail, other routes keep working for signed-in users.
5. After the fix, confirm the ratio is back under 1 % and the alert closes. If a code fix is needed and a rollback is not safe
   (the release changed the schema), roll forward with a fix.
