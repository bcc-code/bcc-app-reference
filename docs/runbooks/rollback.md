# Rollback

A deploy builds one image and promotes the same digest from sandbox to prod. Hashed frontend assets are uploaded additively and
never deleted from the bucket, so an older revision still finds the bundles its `index.html` references.

1. In Cloud Run, open `template-api-<env>`. Under Revisions, choose Manage traffic and send 100 % of the traffic to the previous
   revision. Or:

   ```bash
   gcloud run services update-traffic template-api-<env> --region <region> --project <project> --to-revisions <previous-revision>=100
   ```

2. Check the environment: `/health` answers, and a signed-in user can open the application.
3. The database schema is only extended by a release (expand first, contract in a later release), so the previous revision runs
   against the newer schema. If a release removed or renamed something, do not roll back: roll forward with a fix.
4. Fix the problem on a branch and deploy again. The next deploy sends traffic to the new revision, so a rolled-back service
   must not be left on an old revision without a ticket to fix it.

## What is overwritten on deploy

Nothing outside the image and the hashed bundles. If the application later adds a file that is overwritten on every deploy (for
example a script loader that others embed), write down here how to restore the previous version, because the bucket has no
object versioning.

## Rolling forward instead

Roll forward when a migration of the bad release removed or renamed something, when data was written in a new format, or when a
fix is smaller than the rollback.
