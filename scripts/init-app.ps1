#Requires -Version 7.0
<#
.SYNOPSIS
    Turns a copy of the template into a real application: renames BccCode.Template to BccCode.<Name>.

.DESCRIPTION
    Run it once, from a fresh copy of the template (a clean git working tree), with PowerShell 7 on Windows, macOS or Linux.
    It refuses to run when BccCode.Template.slnx is not in the repository root, which is also what makes a second run safe.

    What it does, in this order:
      1. Replaces text in all text files: BccCode.Template -> BccCode.<Name>, BccCode_Template -> BccCode_<Name>,
         Template -> <Name>, template -> <slug>, TEMPLATE -> <SLUG> (hyphens as underscores). That covers
         @bcc-template/frontend, TEMPLATE_APP_URL, the bcc-template-* image and repository names, __Host-template and the
         template-preview markers.
      2. Renames files and folders with the same rules.
      3. Prints the next steps.

    Not replaced: files and folders named like pull_request_template, the Vue tags <template> and </template>, lock files
    (pnpm-lock.yaml, packages.lock.json: regenerate them), and anything under .git, node_modules, bin, obj, dist, .terraform.
    The word "Template" in prose is replaced too, so read the docs afterwards.

.PARAMETER Name
    PascalCase application name, for example Vote. Becomes BccCode.<Name>.* and replaces "Template".

.PARAMETER Slug
    Lower-case identifier for Cloud resources, hosts and file names. Defaults to the lower-cased Name.

.PARAMETER PortBase
    The first port of the application's range for the sole local instance (standard 02.8): every port in the template's range
    16000-16099 (API 16000, Vite 16001, Postgres 16010, ..., Structurizr 16060) moves by the same offset, in all text files (the
    AppHost, the launch settings, .claude/launch.json, appsettings, docs). Pick a base whose 100 ports no other BCC app uses; the
    slot layout is in api/AppHost/Program.cs. Defaults to 16000 (no change), with a warning, because two apps on the same
    developer machine then fight over the ports.

.PARAMETER Force
    Run although the git working tree has uncommitted changes.

.EXAMPLE
    ./scripts/init-app.ps1 -Name Vote -WhatIf

.EXAMPLE
    ./scripts/init-app.ps1 -Name Vote -Slug bcc-vote -PortBase 17000
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Z][A-Za-z0-9]*$')]
    [string]$Name,

    [ValidatePattern('^[a-z][a-z0-9-]*$')]
    [string]$Slug,

    [ValidateRange(1024, 65400)]
    [int]$PortBase = 16000,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$scriptPath = (Resolve-Path $PSCommandPath).Path

# --- Guards ----------------------------------------------------------------------------------------------------------

if (-not (Test-Path (Join-Path $root 'BccCode.Template.slnx'))) {
    throw "BccCode.Template.slnx was not found in '$root'. This is not a fresh copy of the template (or it was initialised already)."
}
if ($Name -eq 'Template') { throw "-Name must not be 'Template'." }
if (-not $Slug) { $Slug = $Name.ToLowerInvariant() }
if ($Slug -eq 'template') { throw "-Slug must not be 'template'." }
if (-not $PSBoundParameters.ContainsKey('PortBase')) {
    Write-Warning "-PortBase was not given: the local ports stay 16000-16099, the template's own range. Another BCC app on this machine will clash with them; pass a base nobody else uses."
}

if (-not $Force -and (Test-Path (Join-Path $root '.git')) -and (Get-Command git -ErrorAction SilentlyContinue)) {
    $dirty = git -C $root status --porcelain 2>$null
    if ($dirty) { throw "The git working tree has uncommitted changes. Commit or stash them, or pass -Force." }
}

# --- Tree walk --------------------------------------------------------------------------------------------------------

$excludedDirs = @('.git', 'node_modules', 'bin', 'obj', 'dist', '.terraform', 'TestResults', '.vs', '.idea', '.playwright', '.vite')
$lockFiles = @('pnpm-lock.yaml', 'packages.lock.json', 'package-lock.json', '.terraform.lock.hcl')

# Returns every file and directory under $root, skipping excluded directories without descending into them.
function Get-TreeItems {
    $stack = [System.Collections.Generic.Stack[string]]::new()
    $stack.Push($root)
    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        foreach ($entry in [System.IO.Directory]::EnumerateFileSystemEntries($dir)) {
            $leaf = [System.IO.Path]::GetFileName($entry)
            if ([System.IO.Directory]::Exists($entry)) {
                if ($excludedDirs -contains $leaf) { continue }
                [pscustomobject]@{ Path = $entry; IsDir = $true }
                $stack.Push($entry)
            }
            else {
                [pscustomobject]@{ Path = $entry; IsDir = $false }
            }
        }
    }
}

function Test-TextFile([string]$path) {
    if ($path -eq $scriptPath) { return $false }
    if ($lockFiles -contains [System.IO.Path]::GetFileName($path)) { return $false }
    $info = [System.IO.FileInfo]::new($path)
    if ($info.Length -gt 5MB) { return $false }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $n = [Math]::Min($bytes.Length, 8000)
    for ($i = 0; $i -lt $n; $i++) { if ($bytes[$i] -eq 0) { return $false } }
    return $true
}

function Get-RelativePath([string]$path) { [System.IO.Path]::GetRelativePath($root, $path).Replace('\', '/') }

# Read keeping the BOM decision, so the file is written back the way it was found.
function Read-TextFile([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [System.Text.UTF8Encoding]::new($false).GetString($bytes, $(if ($hasBom) { 3 } else { 0 }), $bytes.Length - $(if ($hasBom) { 3 } else { 0 }))
    [pscustomobject]@{ Text = $text; Bom = $hasBom }
}

function Write-TextFile([string]$path, [string]$text, [bool]$bom) {
    [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($bom))
}

# --- Text conversion ----------------------------------------------------------------------------------------------------

# Spellings that contain "template" but must stay as they are.
$protected = @('</template', '<template', 'pull_request_template', 'templatefile(', 'ISSUE_TEMPLATE', 'PULL_REQUEST_TEMPLATE')

# $narrow limits the replacement to the project's own names (BccCode.Template...). It is used for third-party reference
# material such as .claude/skills, where the word "template" means something else.
function Convert-Text([string]$text, [switch]$narrow) {
    if ([string]::IsNullOrEmpty($text)) { return $text }
    $placeholders = @{}
    $i = 0
    foreach ($p in $protected) {
        if ($text.Contains($p)) {
            $key = "$([char]1)P$i$([char]1)"
            $placeholders[$key] = $p
            $text = $text.Replace($p, $key)
            $i++
        }
    }
    $text = $text.Replace('BccCode.Template', "BccCode.$Name")
    $text = $text.Replace('BccCode_Template', "BccCode_$Name")
    if (-not $narrow) {
        $text = $text.Replace('Template', $Name)
        $text = $text.Replace('template', $Slug)
        # Environment variables: TEMPLATE_APP_URL -> VOTE_APP_URL (a slug with hyphens becomes underscores).
        $text = $text.Replace('TEMPLATE', $Slug.ToUpperInvariant().Replace('-', '_'))
    }
    foreach ($key in $placeholders.Keys) { $text = $text.Replace($key, $placeholders[$key]) }
    return $text
}

# Moves the template's port range 16000-16099 to $PortBase. A port is a whole number: hashes and longer numbers are left alone.
function Convert-Ports([string]$text) {
    if ($PortBase -eq 16000 -or [string]::IsNullOrEmpty($text)) { return $text }
    return [regex]::Replace($text, '(?<![\w.])160(\d\d)(?![\w])', { param($m) [string]($PortBase + [int]$m.Groups[1].Value) })
}

# --- Plan -----------------------------------------------------------------------------------------------------------------

Write-Host "Initialising '$Name' (slug '$Slug') in $root"

$items = @(Get-TreeItems)
$textFiles = @($items | Where-Object { -not $_.IsDir -and (Test-Path -LiteralPath $_.Path) -and (Test-TextFile $_.Path) } | ForEach-Object { $_.Path })

# 1. Replace text -----------------------------------------------------------------------------------------------------------------
$changedFiles = 0
foreach ($file in $textFiles) {
    if (-not (Test-Path -LiteralPath $file)) { continue }
    $content = Read-TextFile $file
    $source = $content.Text
    $new = Convert-Text $source -narrow:((Get-RelativePath $file) -like '.claude/skills/*')
    $new = Convert-Ports $new
    if ($new -ceq $content.Text) { continue }
    $changedFiles++
    if ($PSCmdlet.ShouldProcess((Get-RelativePath $file), 'Replace Template with the new name')) {
        Write-TextFile $file $new $content.Bom
    }
}

# 2. Rename files and folders, deepest first, so a parent is renamed after its children ------------------------------------------
$renames = @($items | Where-Object { Test-Path -LiteralPath $_.Path } | ForEach-Object {
        $leaf = [System.IO.Path]::GetFileName($_.Path)
        $newLeaf = Convert-Text $leaf
        if ($newLeaf -cne $leaf) { [pscustomobject]@{ Path = $_.Path; NewLeaf = $newLeaf } }
    } | Sort-Object { $_.Path.Length } -Descending)
foreach ($r in $renames) {
    $target = Join-Path ([System.IO.Path]::GetDirectoryName($r.Path)) $r.NewLeaf
    if ($PSCmdlet.ShouldProcess((Get-RelativePath $r.Path), "Rename to $($r.NewLeaf)")) {
        if (Test-Path -LiteralPath $target) { throw "Cannot rename '$(Get-RelativePath $r.Path)': '$($r.NewLeaf)' already exists." }
        Move-Item -LiteralPath $r.Path -Destination $target
    }
}

# --- Report ---------------------------------------------------------------------------------------------------------------------

Write-Host ''
Write-Host "Summary: $changedFiles file(s) with replaced text, $($renames.Count) rename(s)."
if ($PortBase -ne 16000) { Write-Host "Ports: 16000-16099 moved to $PortBase-$($PortBase + 99)." }

$nextSteps = @"

Next steps
  1. Review the result:            git status ; git diff
  2. Restore, with new lock files:  dotnet restore --force-evaluate
  3. Build, then write the OpenAPI document (api/BccCode.$Name.Api/BccCode.$Name.Api.json) and the client:
                                     dotnet build BccCode.$Name.slnx
                                     pnpm --dir frontend install
                                     pnpm --dir frontend run generate
  4. Run the tests and the checks:   dotnet test --solution BccCode.$Name.slnx
                                     dotnet format BccCode.$Name.slnx --verify-no-changes
                                     pnpm --dir frontend run lint ; pnpm --dir frontend run type-check ; pnpm --dir frontend run test
  5. Start it:                       aspire run
  6. Read the ADRs in docs/adrs and the data inventory in docs/data-protection: they still describe the template.
  7. Fill in the placeholders marked TODO: infra/*.tfvars.example, .github/CODEOWNERS, docs/data-protection/data-inventory.md.
  8. Delete scripts/init-app.ps1 when you are done with it.
"@
Write-Host $nextSteps
