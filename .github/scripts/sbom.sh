#!/usr/bin/env bash
# CycloneDX SBOMs (standard 17.11): the API project with its project references (what the image ships), and the
# frontend pnpm workspace. Usage: sbom.sh <output dir> <name suffix, e.g. the commit SHA>
set -euo pipefail
mkdir -p "$1"
out=$(realpath "$1")
suffix=$2

dotnet dotnet-cyclonedx api/BccCode.Template.Api/BccCode.Template.Api.csproj --recursive \
  --output-format Json --output "$out" --filename "dotnet-$suffix.cdx.json"
# Production dependencies only, from the lock file, so it matches what the build bundles.
pnpm --dir frontend sbom --sbom-format cyclonedx --sbom-type application --prod --lockfile-only \
  --out "$out/frontend-$suffix.cdx.json"
