#!/usr/bin/env bash
# Decides what a preview.yml event does (standard 07.2). Reads EVENT, ACTION, LABEL (the label that changed), LABELLED
# (whether the PR has the preview label now), SAME_REPO, STATE (open or closed), PR, SHA and ACTIVE (whether the sticky
# comment has the :active marker) and prints deploy=, destroy=, first_time=, component= and tag= lines for
# $GITHUB_OUTPUT.
set -euo pipefail

deploy=false
destroy=false
first_time=false

# The number becomes a Terraform workspace and a Postgres schema name; a typo in a manual run would target an empty
# workspace and leave the real preview running.
if ! [[ "${PR:-}" =~ ^[0-9]+$ ]]; then
  echo "PR must be a plain PR number, got '${PR:-}'" >&2
  exit 1
fi

if [ "$EVENT" = workflow_dispatch ]; then
  destroy=true
# Forks get no OIDC token, so they neither deploy nor destroy.
elif [ "${SAME_REPO:-}" = true ]; then
  case "${ACTION:-}" in
    labeled)
      # Any other label must not reseed: that drops the preview's schema.
      if [ "${LABEL:-}" = preview ]; then
        deploy=true
        first_time=true
      fi
      ;;
    unlabeled)
      if [ "${LABEL:-}" = preview ]; then
        destroy=true
      fi
      ;;
    reopened)
      # Closing dropped the schema, so reseed.
      if [ "${LABELLED:-}" = true ]; then
        deploy=true
        first_time=true
      fi
      ;;
    synchronize)
      if [ "${LABELLED:-}" = true ]; then
        deploy=true
      fi
      ;;
    closed)
      if [ "${LABELLED:-}" = true ]; then
        destroy=true
      fi
      ;;
  esac
  # GitHub keeps one pending run per concurrency group, so any later event can replace a queued teardown. A preview the
  # comment still marks active is destroyed whenever the label is gone or the PR is closed.
  if [ "${ACTIVE:-}" = true ] && { [ "${LABELLED:-}" != true ] || [ "${STATE:-}" = closed ]; }; then
    destroy=true
  fi
  # A label added to a closed PR must not bring its preview back.
  if [ "${STATE:-}" = closed ]; then
    deploy=false
    first_time=false
  fi
fi

# pull_request_target has secrets even for forks; it must never deploy PR code.
if [ "$EVENT" = pull_request_target ] && [ "$deploy" = true ]; then
  echo "refusing to deploy from a pull_request_target run" >&2
  exit 1
fi

echo "deploy=$deploy"
echo "destroy=$destroy"
echo "first_time=$first_time"
echo "component=pr$PR"
echo "tag=pr$PR-${SHA:-}"
