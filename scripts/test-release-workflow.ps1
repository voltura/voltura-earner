$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Earner-release-test-' + [Guid]::NewGuid().ToString('N'))
$savedKey = $env:EARNER_KEYPATH
$releaseScript = Join-Path $PSScriptRoot 'release.ps1'

function Assert([bool]$Condition, [string]$Message)
{
    if (-not $Condition)
    {
        throw $Message
    }
}

# All external effects are simulated; fixtures and artifacts stay in a temporary directory.

function git
{
    $global:LASTEXITCODE = 0
    $operation = $args[2]

    $state.Calls.Add("git:$operation")

    if ($state.Fail -eq $operation)
    {
        $global:LASTEXITCODE = 1; return
    }

    switch ($operation)
    {
        branch
        {
            if (-not $state.Detached)
            {
                'master'
            }
        }
        'rev-parse'
        {
            $state.Revision
        }
        'ls-remote'
        {
            $state.Tags
        }
        add
        {
            Assert ($args[3] -eq '-A') 'All edits must be staged.'
        }
        diff
        {
            if ($state.Dirty)
            {
                $global:LASTEXITCODE = 1
            }
        }
        commit
        {
            Assert ($args[4] -eq "Release $($state.Target)") 'Wrong commit message.'

            $state.Dirty = $false
            $state.Revision = 'b' * 40
        }
        status
        {
            if ($state.Dirty)
            {
                ' M file.txt'
            }
        }
        push
        {
            Assert ($args[3] -eq 'origin' -and $args[4] -eq 'HEAD:refs/heads/master') 'Wrong push target.'
        }
        default
        {
            throw "Unexpected git operation: $args"
        }
    }
}

function gh
{
    $global:LASTEXITCODE = 0

    if ($args[0] -eq 'api')
    {
        if ($args[1] -like '*/releases')
        {
            $state.Calls.Add('releases')

            if ($state.Fail -eq 'lookup')
            {
                $global:LASTEXITCODE = 1; return
            }

            $state.Releases
        }
        else
        {
            $state.Calls.Add('verify-push')

            if ($state.Fail -eq 'verify-push')
            {
                $global:LASTEXITCODE = 1
            }
        }

        return
    }

    Assert ($args[0] -eq 'release' -and $args[1] -eq 'create') 'Unexpected GitHub operation.'
    $state.Calls.Add('publish')

    if ($state.Fail -eq 'publish')
    {
        $global:LASTEXITCODE = 1; return
    }

    Assert ($args[2] -eq "v$($state.Target)") 'Wrong release version.'

    $targetIndex = [Array]::IndexOf($args, '--target')

    Assert ($args[$targetIndex + 1] -eq $state.Revision) 'Publication must target the final commit.'

    $files = @($args | Where-Object { $_ -like "$($state.Root)*" -and $_ -notlike '*.md' })

    Assert ($files.Count -eq 5) 'Exactly five release assets must be uploaded.'
    Assert (-not ($files | Where-Object { $_ -like '*stale*' })) 'Stale files must not be uploaded.'
}

function Read-Host([string]$Prompt)
{
    $state.Calls.Add('prompt')

    return $state.Target
}

function Get-Command
{
    Assert ($args[0] -eq 'notepad++.exe') 'Unexpected command discovery.'

    if ($state.Editor -eq 'path')
    {
        [pscustomobject]@{ Source = 'C:\test editor\notepad++.exe' }
    }
}

function Test-Path
{
    param([string]$LiteralPath, [string]$PathType)
    if ($LiteralPath -like '*Notepad++\notepad++.exe')
    {
        return $state.Editor -eq 'installed'
    }

    $parameters = @{ LiteralPath = $LiteralPath }

    if ($PathType)
    {
        $parameters.PathType = $PathType
    }

    Microsoft.PowerShell.Management\Test-Path @parameters
}

function Start-Process
{
    param([string]$FilePath, [string[]]$ArgumentList, [switch]$Wait)
    $state.Calls.Add('editor')
    Assert ($Wait -and $ArgumentList -contains '-multiInst' -and $ArgumentList -contains '-nosession') 'Editor must use a separate session and wait.'

    $path = $ArgumentList[-1].Trim('"')

    Assert ([IO.File]::Exists($path)) 'Notes must exist before opening the editor.'

    if ($state.SaveNotes)
    {
        [IO.File]::WriteAllText($path, "# Voltura Earner $($state.Target)`n`n- Saved release notes. Ä中文`n")
    }
}

function Invoke-WorkflowCase([string]$Name, [hashtable]$Options = @{}, [hashtable]$Parameters = @{}, [string]$Failure = '')
{
    $caseRoot = Join-Path $testRoot ([Guid]::NewGuid().ToString('N') + ' with spaces')
    $state = @{
        Root = $caseRoot; Target = '1.1.0'; Current = '1.1.0'; Releases = @('v1.0.9')
        Revision = ('a' * 40); Tags = @(); Dirty = $true; Fail = ''; Detached = $false
        Editor = 'path'; SaveNotes = $true; Notes = '- Existing release notes.'
        Calls = [Collections.Generic.List[string]]::new(); Key = 'environment'
    }

    foreach ($key in $Options.Keys)
    {
        $state[$key] = $Options[$key]
    }

    New-Item -ItemType Directory -Path "$caseRoot/scripts", "$caseRoot/docs/releases" -Force | Out-Null
    Copy-Item -LiteralPath $releaseScript -Destination "$caseRoot/scripts/release.ps1"
    [IO.File]::WriteAllText("$caseRoot/version.json", ('{"version":"' + $state.Current + '"}'))

    if ($null -ne $state.Notes)
    {
        [IO.File]::WriteAllText("$caseRoot/docs/releases/$($state.Target).md", $state.Notes)
    }

    [IO.File]::WriteAllText("$caseRoot/key.pem", 'simulated key')

    $env:EARNER_KEYPATH = "$caseRoot/key.pem"

    if ($state.Key -eq 'missing')
    {
        $env:EARNER_KEYPATH = ''
    }

    if ($state.Key -eq 'invalid')
    {
        $env:EARNER_KEYPATH = "$caseRoot/absent.pem"
    }

    if ($state.Key -eq 'override')
    {
        $env:EARNER_KEYPATH = "$caseRoot/absent.pem"; $Parameters.KeyPath = "$caseRoot/key.pem"
    }

    [IO.File]::WriteAllText("$caseRoot/scripts/sign-update.ps1", @'
param([string]$KeyPath, [switch]$BuildPackages, [switch]$SkipTests)
$state.Calls.Add('sign-build')
Assert ($KeyPath -eq "$($state.Root)/key.pem" -or $KeyPath -eq (Join-Path $state.Root 'key.pem')) 'Wrong key path.'
Assert ([bool]$BuildPackages) 'Packages must be built.'
Assert ([bool]$SkipTests -eq [bool]$Parameters.NoTests) 'Wrong test selection passed to signing.'
if ($state.Fail -eq 'sign-build') { throw 'Simulated signing/package failure.' }
$version = (Get-Content "$PSScriptRoot/../version.json" -Raw | ConvertFrom-Json).version
Assert ($version -eq $state.Target) 'Build must use selected version.'
$publish = "$PSScriptRoot/../artifacts/publish"
New-Item -ItemType Directory -Path $publish -Force | Out-Null
foreach ($name in @("VolturaEarner-Setup-$version-win-x64.exe", "VolturaEarner-Setup-$version-win-x64-full.exe", "VolturaEarner-$version-win-x64.zip", "VolturaEarner-Update-$version.json", "VolturaEarner-Update-$version.sig", "stale-$version.txt"))
{
    [IO.File]::WriteAllText((Join-Path $publish $name), 'simulated asset')
}
'@)

    $caught = $null

    try
    {
        & "$caseRoot/scripts/release.ps1" @Parameters | Out-Null
    }
    catch
    {
        $caught = $_
    }

    if ($Failure)
    {
        Assert ($null -ne $caught -and $caught.ToString() -like "*$Failure*") "$Name expected '$Failure', received '$caught'."

        if ($state.Fail -ne 'publish')
        {
            Assert (-not $state.Calls.Contains('publish')) "$Name published after failure."
        }

        if ($state.Fail -notin @('commit', 'push', 'verify-push', 'publish'))
        {
            Assert (-not $state.Calls.Contains('git:add')) "$Name staged files after an early failure."
        }
    }
    else
    {
        Assert ($null -eq $caught) "$Name failed: $caught"
        Assert ($state.Calls.Contains('sign-build')) "$Name did not build."

        if ($Parameters.PrepareOnly)
        {
            Assert (-not $state.Calls.Contains('git:add') -and -not $state.Calls.Contains('git:push') -and -not $state.Calls.Contains('publish')) 'PrepareOnly mutated Git or published.'
        }
        else
        {
            Assert ($state.Calls.Contains('publish')) "$Name did not publish."
            Assert ($state.Calls.IndexOf('sign-build') -lt $state.Calls.IndexOf('git:add')) 'Build/sign must precede staging.'
            Assert ($state.Calls.IndexOf('git:push') -lt $state.Calls.IndexOf('publish')) 'Push must precede publication.'
        }

        Assert ($state.Calls.Contains('prompt') -eq (-not $Parameters.ContainsKey('Version') -and [version]$state.Current -le [version]'1.0.9')) 'Unexpected version prompt behavior.'
        Assert ($state.Calls.Contains('editor') -eq ($state.Notes -notmatch '[\p{L}\p{N}]')) 'Unexpected editor behavior.'
    }

    Write-Output "Passed: $Name"
}

try
{
    Invoke-WorkflowCase 'Environment key, pending version, all edits, default publication'
    Invoke-WorkflowCase 'Explicit key overrides environment' @{ Key = 'override' }

    Invoke-WorkflowCase 'Missing environment key' @{ Key = 'missing' } @{} 'Supply -KeyPath'
    Invoke-WorkflowCase 'Invalid environment key' @{ Key = 'invalid' } @{} 'does not exist'
    Invoke-WorkflowCase 'Prompt for version' @{ Current = '1.0.9' }

    Invoke-WorkflowCase 'Explicit version' @{ Current = '1.0.9' } @{ Version = '1.1.0' }

    Invoke-WorkflowCase 'Already published version' @{} @{ Version = '1.0.9' } 'must be newer'
    Invoke-WorkflowCase 'Invalid stable version' @{} @{ Version = '../1.1.0' } 'Invalid release version'
    Invoke-WorkflowCase 'Prerelease version rejected' @{} @{ Version = '1.1.0-beta' } 'Invalid release version'
    Invoke-WorkflowCase 'No prior releases' @{ Releases = @() }

    Invoke-WorkflowCase 'Lookup failure' @{ Fail = 'lookup' } @{} 'Could not query'
    Invoke-WorkflowCase 'Detached HEAD' @{ Detached = $true } @{} 'detached HEAD'
    Invoke-WorkflowCase 'Missing notes, PATH editor, wait and save' @{ Notes = $null }

    Invoke-WorkflowCase 'Empty notes, installed editor' @{ Notes = ''; Editor = 'installed' }

    Invoke-WorkflowCase 'Missing editor' @{ Notes = $null; Editor = 'missing' } @{} 'Notepad++ is required'
    Invoke-WorkflowCase 'Editor closes without saving content' @{ Notes = $null; SaveNotes = $false } @{} 'saved content beyond'
    Invoke-WorkflowCase 'Heading-only notes are insufficient' @{ Notes = '# Voltura Earner 1.1.0'; SaveNotes = $false } @{} 'saved content beyond'
    Invoke-WorkflowCase 'Prepare only' @{} @{ PrepareOnly = $true }

    Invoke-WorkflowCase 'NoTests publishes' @{} @{ NoTests = $true }

    Invoke-WorkflowCase 'NoTests prepares locally' @{} @{ NoTests = $true; PrepareOnly = $true }

    Invoke-WorkflowCase 'Clean retry does not need another commit' @{ Dirty = $false }

    Invoke-WorkflowCase 'Conflicting tag' @{ Tags = @("$('c' * 40)`trefs/tags/v1.1.0") } @{} 'different commit'
    Invoke-WorkflowCase 'Matching lightweight tag' @{ Dirty = $false; Tags = @("$('a' * 40)`trefs/tags/v1.1.0") }

    Invoke-WorkflowCase 'Matching annotated tag' @{ Dirty = $false; Tags = @("$('c' * 40)`trefs/tags/v1.1.0", "$('a' * 40)`trefs/tags/v1.1.0^{}") }

    Invoke-WorkflowCase 'Signing/package failure' @{ Fail = 'sign-build' } @{} 'Simulated signing/package failure'
    Invoke-WorkflowCase 'Commit failure' @{ Fail = 'commit' } @{} 'Release commit failed'
    Invoke-WorkflowCase 'Push failure' @{ Fail = 'push' } @{} 'Release push failed'
    Invoke-WorkflowCase 'Pushed commit verification failure' @{ Fail = 'verify-push' } @{} 'verify the pushed'
    Invoke-WorkflowCase 'Publication failure' @{ Fail = 'publish' } @{} 'publication failed'
    Invoke-WorkflowCase 'Removed Publish switch' @{} @{ Publish = $true } 'parameter name'
}
finally
{
    $env:EARNER_KEYPATH = $savedKey
    $resolved = [IO.Path]::GetFullPath($testRoot)

    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::GetTempPath().TrimEnd('\', '/') -or
        [IO.Path]::GetFileName($resolved) -notlike 'Earner-release-test-*')
    {
        throw 'Unexpected test directory.'
    }

    if ([IO.Directory]::Exists($resolved))
    {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

# Successful failure-case tests must not leave a simulated native command failure behind.

$global:LASTEXITCODE = 0
