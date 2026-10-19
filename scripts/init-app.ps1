#Requires -Version 7.0
<#
.SYNOPSIS
    Turns a copy of the template into a real application: picks one persistence variant, removes the other and renames
    BccCode.Template to BccCode.<Name>.

.DESCRIPTION
    Run it once, from a fresh copy of the template (a clean git working tree), with PowerShell 7 on Windows, macOS or Linux.
    It refuses to run when BccCode.Template.slnx is not in the repository root, which is also what makes a second run safe.

    What it does, in this order:
      1. Deletes the projects of the persistence variant you did not choose
         (api/BccCode.Template.Api.<Other> and api/tests/BccCode.Template.Api.<Other>.Tests).
      2. Edits marked blocks (see "Markers" below), mainly in the Aspire AppHost, so that only the chosen API remains.
      3. Removes lines that still reference the deleted projects from .slnx, .csproj, .props and .targets files.
      4. Records the choice in docs/adrs/0003-marten-or-ef-core.md.
      5. Replaces text in all text files (in .github/ the name of the removed variant is first mapped to the chosen one): BccCode.Template.Api.<Chosen> -> BccCode.<Name>.Api (tests: BccCode.<Name>.Api.Tests),
         BccCode.Template -> BccCode.<Name>, Template -> <Name>, template -> <slug>.
      6. Renames files and folders with the same rules.
      7. Lists what it could not decide by itself, and prints the next steps.

    Markers. The script cannot know how the AppHost chooses between the two APIs, so the code carries markers in comments
    (any comment syntax: //, #, <!-- -->). Each marker sits alone on a line:
      template:only-marten:begin / template:only-marten:end      kept (without the marker lines) for Marten, removed for EF Core
      template:only-efcore:begin / template:only-efcore:end      kept for EF Core, removed for Marten
      template:remove-on-init:begin / template:remove-on-init:end   always removed, including the markers (the Template:Persistence selector)
      // template:init-marten <code>                              uncommented to <code> for Marten, deleted for EF Core
      // template:init-efcore <code>                              uncommented to <code> for EF Core, deleted for Marten
      template:init-note                                          a line that contains it is deleted (comments that describe the script)
    Example, in the AppHost: the switch sits between remove-on-init markers, and the line that replaces it is a comment
    with the init-marten and init-efcore markers, so that only one stays:
      // template:remove-on-init:begin
      var api = persistence switch { ... };
      // template:remove-on-init:end
      // template:init-marten var api = AddVariant<Projects.BccCode_Template_Api_Marten>(...);
      // template:init-efcore var api = AddVariant<Projects.BccCode_Template_Api_EfCore>(...);
    Code inside a kept only-<variant> block must not use anything that is declared in a remove-on-init block, because that
    block is gone afterwards. If the AppHost has no markers, the script says so and the AppHost must be edited by hand.

    Not replaced: files and folders named like pull_request_template, the Vue tags <template> and </template>, lock files
    (pnpm-lock.yaml, packages.lock.json: regenerate them), and anything under .git, node_modules, bin, obj, dist, .terraform.
    The word "Template" in prose is replaced too, so read the docs afterwards.

.PARAMETER Name
    PascalCase application name, for example Vote. Becomes BccCode.<Name>.* and replaces "Template".

.PARAMETER Persistence
    marten or efcore.

.PARAMETER Slug
    Lower-case identifier for Cloud resources, hosts and file names. Defaults to the lower-cased Name.

.PARAMETER Force
    Run although the git working tree has uncommitted changes.

.EXAMPLE
    ./scripts/init-app.ps1 -Name Vote -Persistence marten -WhatIf

.EXAMPLE
    ./scripts/init-app.ps1 -Name Vote -Persistence efcore -Slug bcc-vote
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Z][A-Za-z0-9]*$')]
    [string]$Name,

    [Parameter(Mandatory)]
    [ValidateSet('marten', 'efcore')]
    [string]$Persistence,

    [ValidatePattern('^[a-z][a-z0-9-]*$')]
    [string]$Slug,

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

$chosen = if ($Persistence -eq 'marten') { 'Marten' } else { 'EfCore' }
$other = if ($Persistence -eq 'marten') { 'EfCore' } else { 'Marten' }

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
$protected = @('</template', '<template', 'pull_request_template', 'templatefile(', 'ISSUE_TEMPLATE')

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
    $text = $text.Replace("BccCode.Template.Api.$chosen.Tests", "BccCode.$Name.Api.Tests")
    $text = $text.Replace("BccCode.Template.Api.$chosen", "BccCode.$Name.Api")
    $text = $text.Replace("BccCode_Template_Api_$chosen", "BccCode_$($Name)_Api")
    $text = $text.Replace("template-api-$($chosen.ToLowerInvariant())", "$Slug-api")
    $text = $text.Replace('BccCode.Template', "BccCode.$Name")
    if (-not $narrow) {
        $text = $text.Replace('Template', $Name)
        $text = $text.Replace('template', $Slug)
    }
    foreach ($key in $placeholders.Keys) { $text = $text.Replace($key, $placeholders[$key]) }
    return $text
}

# --- Plan -----------------------------------------------------------------------------------------------------------------

Write-Host "Initialising '$Name' (slug '$Slug') with $chosen persistence in $root"

$items = @(Get-TreeItems)
# A marker is a comment line (//, #, <!-- or /*) that contains it. Prose that merely mentions a marker does not match.
$markerPattern = '(?m)^\s*(//|#|<!--|/\*).*template:((only-(marten|efcore)|remove-on-init):(begin|end)|init-note|init-(marten|efcore)\s)'

# 1. Delete the other variant -----------------------------------------------------------------------------------------------
$deleteDirs = @(
    (Join-Path $root "api/BccCode.Template.Api.$other"),
    (Join-Path $root "api/tests/BccCode.Template.Api.$other.Tests")
)
foreach ($dir in $deleteDirs) {
    if (-not (Test-Path $dir)) {
        Write-Warning "Not found, nothing to delete: $(Get-RelativePath $dir)"
        continue
    }
    if ($PSCmdlet.ShouldProcess((Get-RelativePath $dir), 'Delete project of the unused persistence variant')) {
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
}
# Items inside the deleted directories no longer exist under -WhatIf either, so leave them out of the rest of the plan.
$deletedPrefixes = $deleteDirs | ForEach-Object { $_ + [System.IO.Path]::DirectorySeparatorChar }
$items = @($items | Where-Object {
        $p = $_.Path
        -not ($deletedPrefixes | Where-Object { $p.StartsWith($_, [System.StringComparison]::Ordinal) -or $p + [System.IO.Path]::DirectorySeparatorChar -eq $_ }) })
$textFiles = @($items | Where-Object { -not $_.IsDir -and (Test-Path -LiteralPath $_.Path) -and (Test-TextFile $_.Path) } | ForEach-Object { $_.Path })

# 2. Marker blocks ---------------------------------------------------------------------------------------------------------
$markerFilesFound = 0
$appHostMarkers = 0
foreach ($file in $textFiles) {
    $content = Read-TextFile $file
    if ($content.Text -notmatch $markerPattern) { continue }
    $markerFilesFound++
    $lines = [regex]::Split($content.Text, '(?<=\n)')
    $out = [System.Collections.Generic.List[string]]::new()
    $skipping = $null   # the kind of block being dropped, or $null
    $keeping = $null    # the kind of block whose content is kept
    $changed = $false
    foreach ($line in $lines) {
        if ($line -match '^\s*(//|#|<!--|/\*).*template:init-note') { $changed = $true; continue }
        if ($line -match '^(?<indent>\s*)(//|#)\s*template:init-(?<variant>marten|efcore)\s(?<code>[^\r\n]*)(?<eol>\r?\n?)$') {
            $changed = $true
            if (-not $skipping -and $Matches['variant'] -eq $Persistence) { $out.Add($Matches['indent'] + $Matches['code'] + $Matches['eol']) }
            continue
        }
        if ($line -match '^\s*(//|#|<!--|/\*).*template:(?<kind>only-marten|only-efcore|remove-on-init):(?<edge>begin|end)') {
            $kind = $Matches['kind']; $edge = $Matches['edge']
            $changed = $true
            if ((Get-RelativePath $file) -like 'api/AppHost/*') { $appHostMarkers++ }
            if ($edge -eq 'begin') {
                if ($skipping -or $keeping) { throw "Nested template markers in $(Get-RelativePath $file)" }
                if ($kind -eq 'remove-on-init' -or $kind -ne "only-$($Persistence)") { $skipping = $kind } else { $keeping = $kind }
            }
            else {
                if ($skipping -eq $kind) { $skipping = $null }
                elseif ($keeping -eq $kind) { $keeping = $null }
                else { throw "Unbalanced template marker '$kind:end' in $(Get-RelativePath $file)" }
            }
            continue
        }
        if (-not $skipping) { $out.Add($line) }
    }
    if ($skipping -or $keeping) { throw "Missing 'end' for template marker in $(Get-RelativePath $file)" }
    if ($changed -and $PSCmdlet.ShouldProcess((Get-RelativePath $file), 'Resolve template markers')) {
        Write-TextFile $file ($out -join '') $content.Bom
    }
}
$appHostDir = Join-Path $root 'api/AppHost'
$appHostProblem = $null
if (-not (Test-Path $appHostDir)) { $appHostProblem = 'api/AppHost does not exist.' }
elseif ($appHostMarkers -eq 0) { $appHostProblem = 'api/AppHost has no template markers, so the Template:Persistence switch was not removed.' }

# 3. References to the deleted projects --------------------------------------------------------------------------------------
# A condition such as  '$(MSBuildProjectName)' == 'A' or '$(MSBuildProjectName)' == 'B'  loses the removed name; a whole
# project entry (<Project Path=...>, <ProjectReference ...>) loses its line. Anything else is left for the final scan.
$projectFiles = @($textFiles | Where-Object { $_ -match '\.(slnx?|csproj|props|targets)$' })
$otherName = [regex]::Escape("BccCode.Template.Api.$other")
$orCondition = "\s+or\s+'[^']*'\s*==\s*'$otherName'"
$firstCondition = "'[^']*'\s*==\s*'$otherName'\s+or\s+"
$entryLine = "^\s*<(Project|ProjectReference)\b[^>]*$otherName"
foreach ($file in $projectFiles) {
    if (-not (Test-Path -LiteralPath $file)) { continue }
    $content = Read-TextFile $file
    if ($content.Text -notmatch $otherName) { continue }
    $text = [regex]::Replace($content.Text, $orCondition, '')
    $text = [regex]::Replace($text, $firstCondition, '')
    $kept = [regex]::Split($text, '(?<=\n)') | Where-Object { $_ -notmatch $entryLine }
    $text = $kept -join ''
    if ($PSCmdlet.ShouldProcess((Get-RelativePath $file), "Remove references to BccCode.Template.Api.$other")) {
        Write-TextFile $file $text $content.Bom
    }
}

# 4. Record the choice in ADR 0003 ----------------------------------------------------------------------------------------------
$adr = Join-Path $root 'docs/adrs/0003-marten-or-ef-core.md'
if (Test-Path $adr) {
    $content = Read-TextFile $adr
    $choiceText = if ($Persistence -eq 'marten') { 'Marten' } else { 'EF Core' }
    $date = (Get-Date).ToString('yyyy-MM-dd')
    $pattern = '<!-- template:persistence-choice -->\r?\n[^\r\n]*'
    if ($content.Text -match $pattern) {
        $new = [regex]::Replace($content.Text, $pattern, "**$choiceText** was chosen on $date, when $Name was created. The other variant was removed.")
        if ($PSCmdlet.ShouldProcess((Get-RelativePath $adr), "Record the choice of $choiceText")) {
            Write-TextFile $adr $new $content.Bom
        }
    }
    else {
        Write-Warning 'docs/adrs/0003-marten-or-ef-core.md has no choice marker; record the choice by hand.'
    }
}

# 5. Replace text -----------------------------------------------------------------------------------------------------------------
$changedFiles = 0
foreach ($file in $textFiles) {
    if (-not (Test-Path -LiteralPath $file)) { continue }
    $content = Read-TextFile $file
    $source = $content.Text
    # Workflows name one API project, written as the Marten one in the template. For EF Core it must name the EF Core one.
    if ((Get-RelativePath $file) -like '.github/*') { $source = $source.Replace("BccCode.Template.Api.$other", "BccCode.Template.Api.$chosen") }
    $new = Convert-Text $source -narrow:((Get-RelativePath $file) -like '.claude/skills/*')
    if ($new -ceq $content.Text) { continue }
    $changedFiles++
    if ($PSCmdlet.ShouldProcess((Get-RelativePath $file), 'Replace Template with the new name')) {
        Write-TextFile $file $new $content.Bom
    }
}

# 6. Rename files and folders, deepest first, so a parent is renamed after its children ------------------------------------------
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
Write-Host "Summary: $changedFiles file(s) with replaced text, $($renames.Count) rename(s), $markerFilesFound file(s) with markers."

# Names of the variant that was not chosen that survived in text (docs, comments). They need a human.
$leftovers = [System.Collections.Generic.List[string]]::new()
if (-not $WhatIfPreference) {
    foreach ($file in @(Get-TreeItems | Where-Object { -not $_.IsDir } | ForEach-Object { $_.Path })) {
        if ((Get-RelativePath $file) -like '.claude/skills/*') { continue }
        if (-not (Test-TextFile $file)) { continue }
        $text = (Read-TextFile $file).Text
        if ($text -cmatch 'Api[._](Marten|EfCore)\b') {
            $leftovers.Add((Get-RelativePath $file))
        }
    }
}

if ($appHostProblem) {
    Write-Warning "$appHostProblem Edit api/AppHost by hand so that it starts only the chosen API. The convention for markers is in the header of this script."
}
if ($leftovers.Count -gt 0) {
    Write-Warning "Text that still names a project variant (check these files):`n  $($leftovers -join "`n  ")"
}

$nextSteps = @"

Next steps
  1. Review the result:            git status ; git diff
  2. Restore, with new lock files:  dotnet restore --force-evaluate
  3. Build and regenerate the contract (Debug build writes api/openapi/openapi.json):
                                     dotnet build BccCode.$Name.slnx
                                     pnpm --dir frontend install
                                     pnpm --dir frontend run generate:api
  4. Run the tests and the checks:   dotnet test --solution BccCode.$Name.slnx
                                     dotnet format BccCode.$Name.slnx --verify-no-changes
                                     pnpm --dir frontend run lint ; pnpm --dir frontend run type-check ; pnpm --dir frontend run test
  5. Start it:                       aspire run
  6. Read the ADRs 0002 and 0003 in docs/adrs and the data inventory in docs/privacy: they still describe the template.
  7. Fill in the placeholders marked TODO: infra/*.tfvars.example, .github/CODEOWNERS, docs/privacy/data-inventory.md.
  8. Remove unused package versions of the variant you did not choose from Directory.Packages.props.
  9. Delete scripts/init-app.ps1 when you are done with it.
"@
Write-Host $nextSteps
