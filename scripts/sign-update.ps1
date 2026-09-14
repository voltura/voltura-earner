param(
    [Parameter(Mandatory)][string]$KeyPath,
    [switch]$BuildPackages,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = (Get-Content (Join-Path $root 'version.json') -Raw | ConvertFrom-Json).version
$publicPath = Join-Path $root 'apps\windows\Features\Updates\update-signing-public.pem'

if (-not (Test-Path -LiteralPath $publicPath))
{
    throw 'Configure the dedicated production public key before building release artifacts.'
}

$passphrase = $null
$credential = $null
$rsa = [Security.Cryptography.RSA]::Create()

try
{
    if ([string]::IsNullOrWhiteSpace($env:VOLTURA_EARNER_UPDATE_SIGNING_PASSPHRASE) -and (Test-Path -LiteralPath ($KeyPath + '.passphrase.xml')))
    {
        $passphrase = Import-Clixml -LiteralPath ($KeyPath + '.passphrase.xml')
    }
    elseif ([string]::IsNullOrWhiteSpace($env:VOLTURA_EARNER_UPDATE_SIGNING_PASSPHRASE))
    {
        $passphrase = Read-Host 'Update signing key passphrase' -AsSecureString
    }
    else
    {
        $passphrase = ConvertTo-SecureString $env:VOLTURA_EARNER_UPDATE_SIGNING_PASSPHRASE -AsPlainText -Force
    }

    $credential = [Net.NetworkCredential]::new('', $passphrase)

    if ([string]::IsNullOrWhiteSpace($credential.Password))
    {
        throw 'A nonempty signing passphrase is required.'
    }

    $rsa.ImportFromEncryptedPem(
        [IO.File]::ReadAllText([IO.Path]::GetFullPath($KeyPath)),
        $credential.Password)

    if ($rsa.ExportSubjectPublicKeyInfoPem().Trim() -ne [IO.File]::ReadAllText($publicPath).Trim())
    {
        throw 'Signing key does not match the embedded public key.'
    }

    if ($BuildPackages)
    {
        & "$PSScriptRoot\package.ps1" -SkipTests:$SkipTests
    }

    $publish = Join-Path $root 'artifacts\publish'
    $assets = @('', '-full') | ForEach-Object {
        $name = "VolturaEarner-Setup-$version-win-x64$_.exe"
        $file = Get-Item -LiteralPath (Join-Path $publish $name)

        [ordered]@{
            name = $name
            size = $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject ([ordered]@{
                    schema = 1
                    version = $version
                    assets = @($assets)
                }) -Depth 5 -Compress))
    $signature = $rsa.SignData(
        $bytes,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pss)

    [IO.File]::WriteAllBytes((Join-Path $publish "VolturaEarner-Update-$version.json"), $bytes)
    [IO.File]::WriteAllBytes((Join-Path $publish "VolturaEarner-Update-$version.sig"), $signature)
}
finally
{
    $rsa.Dispose()

    if ($null -ne $passphrase)
    {
        $passphrase.Dispose()
    }

    $credential = $null
}
