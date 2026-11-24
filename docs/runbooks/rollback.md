# Rollback

How to undo a bad deploy of `<app>-api-<env>`. The deploy is described in [ADR 0014](../adrs/0014-build-once-deploy-after-green-ci.md) and `.github/workflows/deploy.yml`. A deploy builds one image and promotes the same digest from sandbox to prod.

## Roll back traffic

1. In the Cloud Run console, open `<app>-api-<env>` in the environment's project. Under **Revisions**, choose **Manage traffic** and send 100 % to the previous revision. Or from a shell with access to the project:

   ```bash
   gcloud run services update-traffic <app>-api-<env> --region <region> --project <project> --to-revisions <previous-revision>=100
   ```

   The revision's image must still be in Artifact Registry. The cleanup policy (`infra/artifact_registry.tf`) keeps the 20 newest images and deletes the rest once they are 90 days old, so a revision older than both cannot start.

2. Check the environment: `/health` answers `Healthy`, a signed-in user can open the application, and the start page loads with its styles and scripts.
3. Traffic is now pinned to that revision, so a new revision gets 0 % until traffic is sent to the latest one again. The next deploy does that (`deploy.yml` runs `update-traffic --to-latest` after the rollout and fails if the new revision is not serving). Fix forward with a new commit on `main`, or revert the bad commit. To undo the rollback without a deploy, run `gcloud run services update-traffic <app>-api-<env> --region <region> --project <project> --to-latest`. A rolled-back service must not be left on an old revision without a ticket to fix it.

## Why the old frontend still works

The content-hashed bundles under `assets/` in the `<project>-assets` bucket are uploaded without deleting older ones, so the previous revision's `index.html` still finds the bundles it references. Nothing in the bucket is overwritten on deploy. The bucket is versioned and keeps deleted or overwritten objects as noncurrent versions for 30 days, so an object removed by mistake can be restored from its previous generation (`gcloud storage ls --all-versions`, then `gcloud storage cp` the generation back). If the application later adds a file that is overwritten on every deploy, write down here how to restore the previous version.

## When to roll forward instead

The schema step (`db-apply`) runs before the rollout and is not rolled back with the traffic. Schema changes are only additive (new tables and columns, standard 06.12), so the previous revision runs against the newer schema and a traffic rollback is safe. A release that removed or renamed something in the schema, or wrote data in a shape the previous code cannot read, is rolled forward with a fix instead.

## Infrastructure

A bad Terraform change is reverted by reverting the commit and letting the deploy (or `infra.yml`, manual) apply again. Terraform does not own the running image, so applying it does not roll the app back or forward. Terraform runs only from CI, never from a laptop.
