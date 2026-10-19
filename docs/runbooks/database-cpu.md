# Database CPU

Alert: `template <env>: database CPU above 80 %`. CPU utilisation of the Cloud SQL instance `template-<env>` was above 80 %
for 10 minutes.

1. Open the instance in the console, Query Insights. Find the queries with the highest total time in the window of the alert.
2. Check connections. The API opens at most 10 connections per instance (`Maximum Pool Size=10`); with many Cloud Run
   instances the total can add up. If connections are the problem, lower `api_max_instances` or the pool size.
3. Is it a new release? A missing index or a query that was added in the last deploy is the usual cause. Compare with the time
   of the last revision. Roll back if it lines up ([rollback.md](rollback.md)).
4. Is it a migration or a backfill? A long-running `template-migrate-<env>` execution or a manual SQL statement can keep CPU
   high. Wait for it or cancel it.
5. If load is simply higher than the tier can serve, raise `db_tier` for the environment in `infra/main.tf` through a pull
   request. Changing the tier restarts the instance, so plan it outside busy hours. Prod runs a regional (high availability)
   instance; sandbox does not.
