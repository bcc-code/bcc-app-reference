---
title: "A merged pull request runs the full CI again on main"
status: needs-decision
decision-by: "the team, together with the deploy flow"
effort: M
priority: P2
scope: general
area: deployment
---

# A merged pull request runs the full CI again on main

`ci.yml` runs every job on a push to `main` (the `changes` job sets every filter to true for a non-PR event), even when the
merged tree is the one the pull request's last run already tested. Measured in November 2026: a PR run billed 15 minutes and
a main run 20. In bcc-code/vote-rewrite the same pattern is about 46 of the roughly 63 minutes a merge costs. This is the
largest remaining Actions cost after [ADR 0030](../adrs/0030-fewer-ci-jobs-to-cut-billed-minutes.md).

It is not a simple skip, because `deploy.yml` starts from CI's push run (`workflow_run`) and its `gate` job checks that CI
succeeded for that commit on `main` (`.github/workflows/deploy.yml`, the `gate` job). The `codegen` job also builds the Release
container in CI, and `deploy.yml`'s `build` job builds it again.

Directions, not yet weighed:

- **Merge queue** (`merge_group` trigger): CI runs once on the merged result before it lands; the push to `main` then runs only
  what deploy needs. Requires a ruleset change and `merge_group` in `ci.yml`.
- **Tree-equality skip**: on a push to `main`, find a successful PR run whose head commit has the same tree (`git rev-parse
  HEAD^{tree}`) and skip the heavy jobs. Works with squash merges only when the PR branch was up to date with `main`.
- Either way, `deploy.yml`'s gate must accept the new signal, and the image built in CI could be reused by deploy instead of
  being built twice (standard 06.3 already asks for one build that is promoted).
