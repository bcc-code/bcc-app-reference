# API latency

Alert: `bcc-template <env>: api latency p95 above 1 s`. The 95th percentile of request latency was above 1000 ms over 5 minutes.

1. Cold starts? In sandbox the service scales to zero, so the first requests after an idle period are slow. Check the instance
   count in the Cloud Run metrics. If the p95 is high only after idle periods, it is expected in sandbox. In prod one instance is
   kept warm (`api_scaling_by_environment` in `infra/cloudrun_api.tf`), so cold starts there mean a scale-out under load.
2. Is the database the cause? The instance is the platform's shared one. Look at Query Insights for the slowest queries from this app's database user, and ask the
   platform team about CPU and connections on the instance.
3. Is one route slow? In Cloud Run request logs, group by route and look at the latency of the slowest requests. If one
   endpoint stands out, check its query or its loop over data.
4. Is it load? Compare the request count with normal. If the service sits at its maximum instances, raise it in `api_scaling_by_environment` (`infra/cloudrun_api.tf`)
   through a pull request. Terraform owns the scaling bounds.
5. A recent deploy? Compare with the time of the last revision and roll back if it lines up ([rollback.md](rollback.md)).
