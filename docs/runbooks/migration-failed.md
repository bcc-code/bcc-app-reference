# Migration failed

Alert: `template <env>: migration job failed`. An execution of the Cloud Run job `template-migrate-<env>` failed. The deploy
runs it with `--wait` before the rollout, so the new revision did not receive traffic. The previous revision keeps serving.

1. Read the execution log:

   ```bash
   gcloud run jobs executions list --job template-migrate-<env> --region <region> --project <project> --limit 5
   gcloud logging read 'resource.type="cloud_run_job" AND resource.labels.job_name="template-migrate-<env>" AND severity>=WARNING' \
     --project <project> --freshness 1h --limit 50 --format='value(timestamp,jsonPayload.message,textPayload)'
   ```

2. Typical causes:
   - Permission denied for schema or table: the migration user lacks the privileges in
     [infra/README.md](../../infra/README.md#database-privileges), or an object is owned by another database user.
   - The Auth Proxy sidecar was not ready or the IAM user does not exist: check the `cloud-sql-proxy` container logs and
     `google_sql_user.migrate` in the Terraform state.
   - A migration contains a statement that cannot run on the existing data (for example a new unique constraint over duplicate
     rows). Fix the migration or the data.
   - Timeout (600 s): a long data migration. Run it in steps, or raise `timeout` in `infra/cloudrun_migrate.tf`.
3. The job does not retry (`max_retries = 0`): a half-applied migration must be looked at. Check what was applied (the migration
   history table of the chosen persistence) before running again.
4. Fix forward: correct the migration in a pull request, merge, and the next deploy runs it. To retry the same image right
   away, run the Deploy workflow again for the same commit, or execute the job:

   ```bash
   gcloud run jobs execute template-migrate-<env> --region <region> --project <project> --wait
   ```

5. Migrations must be backward compatible with the previous release (expand first, contract in a later release), so that a
   traffic rollback stays safe. If a failed release already changed the schema in a way the old revision cannot read, roll
   forward instead ([rollback.md](rollback.md)).
