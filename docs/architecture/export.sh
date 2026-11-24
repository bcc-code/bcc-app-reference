#!/usr/bin/env bash
# Exports every view to diagrams/<view>.svg and records the source hash that CI checks. The model comes from
# workspace.dsl and the layout from workspace.json (saved by Structurizr local); they are merged first.
# Needs Docker. Same result as export.ps1.
#   --check  only compare the hash with diagrams/source.sha256 (CI); no Docker needed.
set -euo pipefail

dir="$(cd "$(dirname "$0")" && pwd)"

# The hash covers the export image, the DSL and the layout, with line endings normalised so Windows and Linux agree.
source_hash() {
  { grep ' AS export$' "$dir/Dockerfile"; cat "$dir/workspace.dsl" "$dir/workspace.json"; } | tr -d '\r' | sha256sum | cut -d' ' -f1
}

if [ "${1:-}" = "--check" ]; then
  [ "$(source_hash)" = "$(tr -d '\r\n' < "$dir/diagrams/source.sha256")" ] && exit 0
  echo "The diagrams in docs/architecture/diagrams are older than workspace.dsl, workspace.json or the export image." >&2
  echo "Run docs/architecture/export.sh (or export.ps1) and commit the result." >&2
  exit 1
fi

image=$(sed -n 's/^FROM \(.*\) AS export$/\1/p' "$dir/Dockerfile")
[ -n "$image" ] || { echo "No 'FROM ... AS export' line in $dir/Dockerfile" >&2; exit 1; }

mount="$dir"
# Git Bash on Windows: Docker needs a Windows path, and MSYS must not rewrite the container paths.
if command -v cygpath >/dev/null 2>&1; then mount="$(cygpath -w "$dir")"; export MSYS_NO_PATHCONV=1; fi

rm -rf "$dir/diagrams"
mkdir -p "$dir/diagrams" "$dir/.structurizr"
# The file export reads layout only from JSON, so apply workspace.json's layout to the DSL model first.
docker run --rm -v "$mount:/usr/local/structurizr" "$image" \
  merge -workspace /usr/local/structurizr/workspace.dsl -layout /usr/local/structurizr/workspace.json \
  -output /usr/local/structurizr/.structurizr/export.json
docker run --rm -v "$mount:/usr/local/structurizr" "$image" \
  export -format svg -workspace /usr/local/structurizr/.structurizr/export.json -output /usr/local/structurizr/diagrams
# The legend (-key) files repeat what the styles in workspace.dsl say.
rm -f "$dir"/diagrams/*-key.svg "$dir/.structurizr/export.json"

source_hash > "$dir/diagrams/source.sha256"
echo "Exported to $dir/diagrams"
