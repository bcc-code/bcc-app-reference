#!/usr/bin/env pwsh
# Exports every view to diagrams/<view>.svg and records the source hash that CI checks. The model comes from
# workspace.dsl and the layout from workspace.json (saved by Structurizr local); they are merged first.
# Needs Docker. Same result as export.sh.
$ErrorActionPreference = 'Stop'

$dir = $PSScriptRoot
$fromLine = Get-Content (Join-Path $dir 'Dockerfile') | Where-Object { $_ -match ' AS export$' } | Select-Object -First 1
if (-not $fromLine) { throw "No 'FROM ... AS export' line in $dir/Dockerfile" }
$image = ($fromLine -replace '^FROM (.*) AS export$', '$1')

$diagrams = Join-Path $dir 'diagrams'
Remove-Item $diagrams -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $diagrams | Out-Null
New-Item -ItemType Directory (Join-Path $dir '.structurizr') -Force | Out-Null
# The file export reads layout only from JSON, so apply workspace.json's layout to the DSL model first.
docker run --rm -v "${dir}:/usr/local/structurizr" $image `
    merge -workspace /usr/local/structurizr/workspace.dsl -layout /usr/local/structurizr/workspace.json `
    -output /usr/local/structurizr/.structurizr/export.json
if ($LASTEXITCODE -ne 0) { throw "merge failed with exit code $LASTEXITCODE" }
docker run --rm -v "${dir}:/usr/local/structurizr" $image `
    export -format svg -workspace /usr/local/structurizr/.structurizr/export.json -output /usr/local/structurizr/diagrams
if ($LASTEXITCODE -ne 0) { throw "export failed with exit code $LASTEXITCODE" }
# The legend (-key) files repeat what the styles in workspace.dsl say.
Remove-Item (Join-Path $diagrams '*-key.svg'), (Join-Path $dir '.structurizr/export.json')

# The hash covers the export image, the DSL and the layout, with line endings normalised so Windows and Linux agree.
$bytes = [Text.Encoding]::UTF8.GetBytes("$fromLine`n") + [IO.File]::ReadAllBytes((Join-Path $dir 'workspace.dsl')) +
    [IO.File]::ReadAllBytes((Join-Path $dir 'workspace.json'))
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]($bytes | Where-Object { $_ -ne 13 }))).ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $diagrams 'source.sha256'), "$hash`n")
Write-Host "Exported to $diagrams"
