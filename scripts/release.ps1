#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$KeyPath = $env:EARNER_KEYPATH,
    [string]$Version,
    [switch]$PrepareOnly,
    [switch]$NoTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repository = 'voltura/voltura-earner'
$script:releasePhase = 'Starting release checks'

function Write-ReleaseHeader([string]$Message)
{
    $script:releasePhase = $Message

    Write-Host ''
    Write-Host ('=' * 72) -ForegroundColor DarkCyan
    Write-Host ('  ' + $Message) -ForegroundColor Cyan
    Write-Host ('=' * 72) -ForegroundColor DarkCyan
}

function Write-ReleaseStatus([string]$Message, [ConsoleColor]$Color = [ConsoleColor]::Gray)
{
    Write-Host ('  ' + $Message) -ForegroundColor $Color
}

function Write-ReleaseSuccess([string]$Message)
{
    $displayMessage = if ($Message.Length -gt 59)
    {
        $Message.Substring(0, 56) + '...'
    }
    else
    {
        $Message.PadRight(59)
    }

    Write-Host ''
    Write-Host ('+' + ('-' * 70) + '+') -ForegroundColor Green
    Write-Host ('|  SUCCESS: ' + $displayMessage + '|') -ForegroundColor Green
    Write-Host ('+' + ('-' * 70) + '+') -ForegroundColor Green
}

trap
{
    $phase = if ($script:releasePhase.Length -gt 44)
    {
        $script:releasePhase.Substring(0, 41) + '...'
    }
    else
    {
        $script:releasePhase.PadRight(44)
    }
    $errorMessage = [string]$_.Exception.Message

    if ($errorMessage.Length -gt 67)
    {
        $errorMessage = $errorMessage.Substring(0, 64) + '...'
    }
    else
    {
        $errorMessage = $errorMessage.PadRight(67)
    }

    Write-Host ''
    Write-Host ('!' + ('-' * 70) + '!') -ForegroundColor Red
    Write-Host ('|  RELEASE FAILED during: ' + $phase + '|') -ForegroundColor Red
    Write-Host ('|  ' + $errorMessage + '|') -ForegroundColor Red
    Write-Host ('!' + ('-' * 70) + '!') -ForegroundColor Red
    throw $_.Exception
}

function ConvertTo-ReleaseVersion([string]$Value)
{
    if ($Value -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')
    {
        throw "Invalid release version '$Value'. Use a stable x.y.z version, for example 1.1.0."
    }

    return [version]$Value
}

function Test-ReleaseNotes([string]$Path)
{
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf))
    {
        return $false
    }

    $body = (Get-Content -LiteralPath $Path -Raw) -replace '(?m)^\s*#.*$', ''
    $body = $body -replace '(?s)<!--.*?-->', ''

    return $body -match '[\p{L}\p{N}]'
}

function Assert-ReleaseTag([string]$Revision)
{
    $tags = @(git -C $root ls-remote --tags origin "refs/tags/v$Version" "refs/tags/v$Version^{}")

    if ($LASTEXITCODE)
    {
        throw 'Could not check the release tag.'
    }

    if ($tags.Count)
    {
        $peeled = @($tags | Where-Object { $_.EndsWith('^{}') })
        $entry = if ($peeled.Count)
        {
            $peeled[0]
        }
        else
        {
            $tags[0]
        }

        if (($entry -split '\s+')[0] -ne $Revision)
        {
            throw "Tag v$Version points to a different commit. Choose a new version."
        }
    }
}

Write-ReleaseHeader 'Preparing release'
Write-ReleaseStatus "Repository: $repository"

if ([string]::IsNullOrWhiteSpace($KeyPath))
{
    throw 'Supply -KeyPath or set EARNER_KEYPATH, then open a new terminal.'
}

if (-not (Test-Path -LiteralPath $KeyPath -PathType Leaf))
{
    throw 'The signing key file does not exist. Check -KeyPath or EARNER_KEYPATH.'
}

$KeyPath = (Resolve-Path -LiteralPath $KeyPath).Path
# Failed lookups must never be mistaken for an unreleased version.

Write-ReleaseStatus 'Checking signing key and existing GitHub releases...' DarkGray

$releaseTags = @(gh api "repos/$repository/releases" --paginate --jq '.[].tag_name')

if ($LASTEXITCODE)
{
    throw 'Could not query GitHub releases. Check gh authentication and connectivity.'
}

$latest = [version]'0.0.0'

foreach ($tag in $releaseTags)
{
    if ($tag -cmatch '^v(\d+\.\d+\.\d+)$')
    {
        $publishedVersion = ConvertTo-ReleaseVersion $Matches[1]

        if ($publishedVersion -gt $latest)
        {
            $latest = $publishedVersion
        }
    }
}

$versionPath = Join-Path $root 'version.json'
$metadata = Get-Content -LiteralPath $versionPath -Raw | ConvertFrom-Json

if (-not $PSBoundParameters.ContainsKey('Version'))
{
    $Version = $metadata.version

    if ((ConvertTo-ReleaseVersion $Version) -le $latest)
    {
        $Version = Read-Host "Version to release (newer than $latest)"
    }
}

if ((ConvertTo-ReleaseVersion $Version) -le $latest)
{
    throw "Version $Version must be newer than the latest release $latest."
}

Write-ReleaseStatus "Releasing version $Version (latest published: $latest)" White

$branch = git -C $root branch --show-current

if ($LASTEXITCODE -or [string]::IsNullOrWhiteSpace($branch))
{
    throw 'Release from a checked-out branch, not a detached HEAD.'
}

$revision = git -C $root rev-parse --verify HEAD 2>$null

if ($LASTEXITCODE -and -not $PrepareOnly)
{
    throw 'Could not read the release commit.'
}

if ($revision)
{
    Assert-ReleaseTag $revision
}

$notes = Join-Path $root "docs/releases/$Version.md"

if (-not (Test-ReleaseNotes $notes))
{
    $editor = Get-Command notepad++.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source

    if (-not $editor)
    {
        $editor = @("$env:ProgramFiles\Notepad++\notepad++.exe", "${env:ProgramFiles(x86)}\Notepad++\notepad++.exe", "$env:LOCALAPPDATA\Programs\Notepad++\notepad++.exe") |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }

    if (-not $editor)
    {
        throw 'Notepad++ is required to write missing release notes. Install it or create the notes file first.'
    }

    if (-not (Test-Path -LiteralPath $notes) -or [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $notes -Raw)))
    {
        New-Item -ItemType Directory -Path (Split-Path $notes -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($notes, "# Voltura Earner $Version`n`n")
    }

    Write-ReleaseStatus 'Write and save the release notes, then close the separate Notepad++ window to continue.' Yellow
    Start-Process -FilePath $editor -ArgumentList @('-multiInst', '-nosession', ('"' + $notes + '"')) -Wait

    if (-not (Test-ReleaseNotes $notes))
    {
        throw 'Release notes require saved content beyond the heading. Nothing staged, committed, or pushed.'
    }
}

if ($metadata.version -ne $Version)
{
    $metadata.version = $Version

    [IO.File]::WriteAllText($versionPath, (($metadata | ConvertTo-Json -Compress) + "`n"))
}

foreach ($path in @($versionPath, $notes))
{
    $content = [IO.File]::ReadAllText($path)
    $normalized = $content -replace '\r\n|\r|\n', "`r`n"

    if ($content -cne $normalized)
    {
        [IO.File]::WriteAllText($path, $normalized)
    }
}

Write-ReleaseHeader 'Building, testing, packaging, and signing'
Write-ReleaseStatus 'The detailed tool output below is retained for diagnostics.' DarkGray

# Validate the key, build/test once, and sign before committing or pushing.

& "$PSScriptRoot\sign-update.ps1" -KeyPath $KeyPath -BuildPackages -SkipTests:$NoTests

$assets = @(
    "VolturaEarner-Setup-$Version-win-x64.exe",
    "VolturaEarner-Setup-$Version-win-x64-full.exe",
    "VolturaEarner-$Version-win-x64.zip",
    "VolturaEarner-Update-$Version.json",
    "VolturaEarner-Update-$Version.sig"
) | ForEach-Object { (Get-Item -LiteralPath (Join-Path $root "artifacts/publish/$_")).FullName }

if ($PrepareOnly)
{
    Write-ReleaseSuccess "Version $Version prepared locally"
    Write-ReleaseStatus 'Files retained; nothing staged, committed, pushed, or published.' Yellow

    return
}

Write-ReleaseHeader 'Committing and publishing'
Write-ReleaseStatus "Committing release $Version on branch $branch..." White

git -C $root add -A

if ($LASTEXITCODE)
{
    throw 'Could not stage release changes.'
}

git -C $root diff --cached --quiet

if ($LASTEXITCODE -eq 1)
{
    git -C $root commit -m "Release $Version"

    if ($LASTEXITCODE)
    {
        throw 'Release commit failed. Fix the error and rerun the same command.'
    }
}
elseif ($LASTEXITCODE)
{
    throw 'Could not inspect staged changes.'
}

$revision = git -C $root rev-parse HEAD

if ($LASTEXITCODE)
{
    throw 'Could not read the release commit.'
}

$changes = @(git -C $root status --porcelain)

if ($LASTEXITCODE -or $changes.Count)
{
    throw 'Files changed during release. Review the changes and rerun before publishing.'
}

Assert-ReleaseTag $revision
git -C $root push origin "HEAD:refs/heads/$branch"

if ($LASTEXITCODE)
{
    throw 'Release push failed. Resolve the Git error and rerun; no release was published.'
}

gh api "repos/$repository/commits/$revision" --silent

if ($LASTEXITCODE)
{
    throw 'Could not verify the pushed release commit.'
}

gh release create "v$Version" @assets --repo $repository --target $revision --title "Voltura Earner $Version" --notes-file $notes

if ($LASTEXITCODE)
{
    throw 'Release publication failed. Inspect the GitHub release before retrying; the commit remains pushed.'
}

Write-ReleaseSuccess "Voltura Earner $Version released successfully"
Write-ReleaseStatus "GitHub: https://github.com/$repository/releases/tag/v$Version" Green
Write-ReleaseStatus 'Commit pushed and all release assets published.' Green
