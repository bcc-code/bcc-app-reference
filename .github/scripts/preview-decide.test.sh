#!/usr/bin/env bash
# Tests for preview-decide.sh, one case per event. Usage: bash .github/scripts/preview-decide.test.sh
set -uo pipefail
script="$(dirname "$0")/preview-decide.sh"
failures=0

# check <name> <expected exit> <expected "deploy destroy first_time"> [VAR=value ...]
check() {
  local name=$1 want_exit=$2 want=$3
  shift 3
  local out code
  out=$(env -i PATH="$PATH" EVENT=pull_request ACTION= LABEL= LABELLED=false SAME_REPO=true STATE=open PR=12 SHA=abc ACTIVE=false \
    "$@" bash "$script" 2>/dev/null)
  code=$?
  if [ "$code" != "$want_exit" ]; then
    echo "FAIL $name: exit $code, want $want_exit"; failures=$((failures + 1)); return
  fi
  [ "$want_exit" = 0 ] || { echo "ok   $name"; return; }
  local got
  got="$(sed -n 's/^deploy=//p' <<<"$out") $(sed -n 's/^destroy=//p' <<<"$out") $(sed -n 's/^first_time=//p' <<<"$out")"
  if [ "$got" != "$want" ]; then
    echo "FAIL $name: got '$got', want '$want'"; failures=$((failures + 1)); return
  fi
  if ! grep -qx 'component=pr12' <<<"$out" || ! grep -qx 'tag=pr12-abc' <<<"$out"; then
    echo "FAIL $name: component or tag missing in: $out"; failures=$((failures + 1)); return
  fi
  echo "ok   $name"
}

check "labeled preview deploys and seeds" 0 "true false true" ACTION=labeled LABEL=preview LABELLED=true
check "labeled other does nothing" 0 "false false false" ACTION=labeled LABEL=bug
check "labeled other while the comment is active destroys" 0 "false true false" ACTION=labeled LABEL=bug ACTIVE=true
check "unlabeled other while the comment is active destroys" 0 "false true false" EVENT=pull_request_target ACTION=unlabeled LABEL=bug ACTIVE=true
check "unlabeled preview destroys" 0 "false true false" EVENT=pull_request_target ACTION=unlabeled LABEL=preview
check "synchronize with label deploys without seeding" 0 "true false false" ACTION=synchronize LABELLED=true
check "synchronize without label but active destroys" 0 "false true false" ACTION=synchronize ACTIVE=true
check "synchronize without label and inactive does nothing" 0 "false false false" ACTION=synchronize ACTIVE=false
check "reopened with label deploys and seeds" 0 "true false true" ACTION=reopened LABELLED=true
check "closed with label destroys" 0 "false true false" EVENT=pull_request_target ACTION=closed LABELLED=true
check "closed without label while the comment is active destroys" 0 "false true false" EVENT=pull_request_target ACTION=closed ACTIVE=true
check "closed without label and inactive does nothing" 0 "false false false" EVENT=pull_request_target ACTION=closed
check "labeled preview on a closed PR does not deploy" 0 "false false false" ACTION=labeled LABEL=preview LABELLED=true STATE=closed
check "labeled preview on a closed PR with an active comment destroys" 0 "false true false" ACTION=labeled LABEL=preview LABELLED=true STATE=closed ACTIVE=true
check "fork does nothing" 0 "false false false" SAME_REPO=false ACTION=labeled LABEL=preview LABELLED=true
check "pull_request_target that would deploy fails" 1 "" EVENT=pull_request_target ACTION=labeled LABEL=preview LABELLED=true
check "workflow_dispatch with a non-numeric PR fails" 1 "" EVENT=workflow_dispatch SAME_REPO=false PR=12a
check "workflow_dispatch with a PR number destroys" 0 "false true false" EVENT=workflow_dispatch SAME_REPO=false SHA=abc

if [ "$failures" -gt 0 ]; then
  echo "$failures failed"; exit 1
fi
echo "all passed"
