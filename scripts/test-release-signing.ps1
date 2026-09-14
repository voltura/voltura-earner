$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Earner-signing-test-' + [Guid]::NewGuid().ToString('N'))
$savedPassphrase = $env:VOLTURA_EARNER_UPDATE_SIGNING_PASSPHRASE
$testPassword = ' test signing passphrase '
$rsa = [Security.Cryptography.RSA]::Create(2048)
$otherKey = [Security.Cryptography.RSA]::Create(2048)
$signingTestState = @{ PromptCount = 0; PromptPassword = $testPassword }
$releaseTestState = @{ Revision = ('a' * 40); Tags = @(); Dirty = $false; CommitExists = $true; Published = $false }

function git
{
    $global:LASTEXITCODE = 0

    if ($args -contains 'status')
    {
        if ($releaseTestState.Dirty)
        {
            ' M scripts/release.ps1'
        }
    }
    elseif ($args -contains 'branch')
    {
        'master'
    }
    elseif ($args -contains 'add') { }
    elseif ($args -contains 'diff')
    {
        if ($releaseTestState.Dirty)
        {
            $global:LASTEXITCODE = 1
        }
    }
    elseif ($args -contains 'commit')
    {
        $releaseTestState.Dirty = $false
    }
    elseif ($args -contains 'push') { }
    elseif ($args -contains 'rev-parse')
    {
        $releaseTestState.Revision
    }
    elseif ($args -contains 'ls-remote')
    {
        $releaseTestState.Tags
    }
    else
    {
        throw 'Unexpected git command in release test.'
    }
}

function gh
{
    $global:LASTEXITCODE = 0

    if ($args[0] -eq 'api')
    {
        if ($args[1] -like '*/releases')
        {
            return
        }

        if (-not $releaseTestState.CommitExists)
        {
            $global:LASTEXITCODE = 1
        }
    }
    elseif ($args[0] -eq 'release' -and $args[1] -eq 'create')
    {
        $targetIndex = [Array]::IndexOf($args, '--target')

        if ($targetIndex -lt 0 -or $args[$targetIndex + 1] -ne $releaseTestState.Revision -or
            $args -contains '--verify-tag' -or $args[2] -ne 'v1.0.0' -or
            -not (Test-Path -LiteralPath (Join-Path $testRoot 'artifacts/publish/VolturaEarner-Update-1.0.0.sig')))
        {
            throw 'Publication must use the verified commit and signed assets with automatic tagging.'
        }

        $releaseTestState.Published = $true
    }
    else
    {
        throw 'Unexpected GitHub command in release test.'
    }
}

function Read-Host([string]$Prompt, [switch]$AsSecureString)
{
    if (-not $AsSecureString -or (Test-Path -LiteralPath (Join-Path $testRoot 'packaged')))
    {
        throw 'The secure prompt must run before packaging.'
    }

    $signingTestState.PromptCount++
    ConvertTo-SecureString $signingTestState.PromptPassword -AsPlainText -Force
}

function Invoke-SigningCase([string]$Name, [AllowNull()][string]$Value, [bool]$ExpectPrompt, [bool]$ExpectSuccess, [bool]$Publish = $false, [bool]$NoTests = $false)
{
    $env:VOLTURA_EARNER_UPDATE_SIGNING_PASSPHRASE = $Value
    $signingTestState.PromptCount = 0
    $releaseTestState.Published = $false
    $marker = Join-Path $testRoot 'packaged'

    Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue

    $failure = $null

    try
    {
        & (Join-Path $testRoot 'scripts/release.ps1') -KeyPath (Join-Path $testRoot 'test.private.pem') -PrepareOnly:(-not $Publish) -NoTests:$NoTests | Out-Null
    }
    catch
    {
        $failure = $_
    }

    if (($null -eq $failure) -ne $ExpectSuccess -or
        (Test-Path -LiteralPath $marker) -ne $ExpectSuccess -or
        $signingTestState.PromptCount -ne [int]$ExpectPrompt -or
        $releaseTestState.Published -ne ($Publish -and $ExpectSuccess))
    {
        throw "Signing regression failed: $Name"
    }

    if ($ExpectSuccess)
    {
        $publishDirectory = Join-Path $testRoot 'artifacts/publish'
        $bytes = [IO.File]::ReadAllBytes((Join-Path $publishDirectory 'VolturaEarner-Update-1.0.0.json'))
        $signature = [IO.File]::ReadAllBytes((Join-Path $publishDirectory 'VolturaEarner-Update-1.0.0.sig'))

        if (-not $rsa.VerifyData($bytes, $signature,
                [Security.Cryptography.HashAlgorithmName]::SHA256,
                [Security.Cryptography.RSASignaturePadding]::Pss))
        {
            throw "Signature verification failed: $Name"
        }
    }

    Write-Output "Passed: $Name"
}

try
{
    $scripts = Join-Path $testRoot 'scripts'
    $publicDirectory = Join-Path $testRoot 'apps/windows/Features/Updates'
    $notesDirectory = Join-Path $testRoot 'docs/releases'

    New-Item -ItemType Directory -Path $scripts, $publicDirectory, $notesDirectory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $notesDirectory '1.0.0.md'), 'Test release')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'release.ps1'), (Join-Path $PSScriptRoot 'sign-update.ps1') -Destination $scripts
    [IO.File]::WriteAllText((Join-Path $testRoot 'version.json'), '{"version":"1.0.0"}')

    $publicPath = Join-Path $publicDirectory 'update-signing-public.pem'

    [IO.File]::WriteAllText($publicPath, $rsa.ExportSubjectPublicKeyInfoPem())

    $parameters = [Security.Cryptography.PbeParameters]::new(
        [Security.Cryptography.PbeEncryptionAlgorithm]::Aes256Cbc,
        [Security.Cryptography.HashAlgorithmName]::SHA256, 1000)

    [IO.File]::WriteAllText((Join-Path $testRoot 'test.private.pem'),
        $rsa.ExportEncryptedPkcs8PrivateKeyPem($testPassword, $parameters))
    [IO.File]::WriteAllText((Join-Path $scripts 'package.ps1'), @'
param([switch]$SkipTests)
if ([bool]$SkipTests -ne $NoTests) { throw 'Release test selection was not forwarded to packaging.' }
$root = Split-Path $PSScriptRoot -Parent
[IO.File]::WriteAllText((Join-Path $root 'packaged'), 'packaged')
$publishDirectory = Join-Path $root 'artifacts/publish'
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
foreach ($suffix in @('', '-full'))
{
    [IO.File]::WriteAllText((Join-Path $publishDirectory "VolturaEarner-Setup-1.0.0-win-x64$suffix.exe"), 'test payload')
}
[IO.File]::WriteAllText((Join-Path $publishDirectory 'VolturaEarner-1.0.0-win-x64.zip'), 'test portable')
'@)

    Invoke-SigningCase 'Environment passphrase with surrounding spaces' $testPassword $false $true
    Invoke-SigningCase 'NoTests reaches packaging and still signs for publication' $testPassword $false $true $true $true
    Invoke-SigningCase 'NoTests works with PrepareOnly' $testPassword $false $true $false $true
    Invoke-SigningCase 'Missing environment prompts before packaging' $null $true $true
    Invoke-SigningCase 'Empty environment prompts before packaging' '' $true $true
    Invoke-SigningCase 'Whitespace environment prompts before packaging' '   ' $true $true
    Invoke-SigningCase 'Wrong environment passphrase stops before packaging' 'incorrect' $false $false

    $signingTestState.PromptPassword = 'incorrect'

    Invoke-SigningCase 'Wrong prompted passphrase stops before packaging' $null $true $false

    $signingTestState.PromptPassword = '   '

    Invoke-SigningCase 'Blank prompted passphrase stops before packaging' $null $true $false

    $signingTestState.PromptPassword = $testPassword

    [IO.File]::WriteAllText($publicPath, $otherKey.ExportSubjectPublicKeyInfoPem())
    Invoke-SigningCase 'Mismatched signing key stops before packaging' $testPassword $false $false
    [IO.File]::WriteAllText($publicPath, $rsa.ExportSubjectPublicKeyInfoPem())
    Invoke-SigningCase 'Missing tag is created by publication at the verified commit' $testPassword $false $true $true

    $releaseTestState.Tags = @("$($releaseTestState.Revision)`trefs/tags/v1.0.0")

    Invoke-SigningCase 'Matching lightweight tag is reused' $testPassword $false $true $true

    $releaseTestState.Tags = @("$('b' * 40)`trefs/tags/v1.0.0", "$($releaseTestState.Revision)`trefs/tags/v1.0.0^{}")

    Invoke-SigningCase 'Matching annotated tag is reused' $testPassword $false $true $true

    $releaseTestState.Tags = @("$('b' * 40)`trefs/tags/v1.0.0")

    Invoke-SigningCase 'Conflicting tag stops before packaging' $testPassword $false $false $true

    $releaseTestState.Tags = @()
    $releaseTestState.Dirty = $true

    Invoke-SigningCase 'Uncommitted changes are committed for publication' $testPassword $false $true $true

    $releaseTestState.Dirty = $false
}
finally
{
    $env:VOLTURA_EARNER_UPDATE_SIGNING_PASSPHRASE = $savedPassphrase

    $rsa.Dispose()
    $otherKey.Dispose()

    $resolved = [IO.Path]::GetFullPath($testRoot)

    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::GetTempPath().TrimEnd('\', '/') -or
        [IO.Path]::GetFileName($resolved) -notlike 'Earner-signing-test-*')
    {
        throw 'Unexpected signing test directory.'
    }

    if (Test-Path -LiteralPath $resolved)
    {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
