# API latency

Alert: `template <env>: api latency p95 above 1 s`. The 95th percentile of request latency was above 1000 ms over 5 minutes.

1. Cold starts? In sandbox the service scales to zero, so the first requests after an idle period are slow. Check the instance
   count in the Cloud Run metrics. If the p95 is high only after idle periods, it is expected in sandbox. In prod one instance is
   kept warm (`api_min_instances` in `infra/main.tf`), so cold starts there mean a scale-out under load.
2. Is the database the cause? Open the Cloud SQL instance `template-<env>` and check CPU, connections and Query Insights for the
   slowest queries. See [database-cpu.md](database-cpu.md).
3. Is one route slow? In Cloud Run request logs, group by route and look at the latency of the slowest requests. If one
   endpoint stands out, check its query or its loop over data.
4. Is it load? Compare the request count with normal. If the service sits at `api_max_instances`, raise it in `infra/main.tf`
   through a pull request. Terraform owns the scaling bounds.
5. A recent deploy? Compare with the time of the last revision and roll back if it lines up ([rollback.md](rollback.md)).
