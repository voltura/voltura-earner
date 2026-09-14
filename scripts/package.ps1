param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Push-Location $root

function Reset-BuildDirectory([string]$directory)
{
    $resolved = [IO.Path]::GetFullPath($directory)
    $allowedParent = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))

    if ([IO.Path]::GetDirectoryName($resolved) -ne $allowedParent -or
        [IO.Path]::GetFileName($resolved) -notin @('payload-standard', 'payload-full', 'portable'))
    {
        throw 'Unexpected build directory.'
    }

    if (Test-Path -LiteralPath $resolved)
    {
        if ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)
        {
            throw 'Linked build directories are not supported.'
        }

        if (Get-ChildItem -LiteralPath $resolved -Recurse -Force -Attributes ReparsePoint)
        {
            throw 'Linked build files are not supported.'
        }

        Remove-Item -LiteralPath $resolved -Recurse -Force
    }

    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}

try
{
    $version = (Get-Content version.json -Raw | ConvertFrom-Json).version
    $nsis = Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe'

    if (-not (Test-Path -LiteralPath $nsis))
    {
        throw 'NSIS is required.'
    }

    if (-not $SkipTests)
    {
        & "$PSScriptRoot\build.ps1"
    }

    & "$PSScriptRoot\build-installer-art.ps1"

    $publish = Join-Path $root 'artifacts\publish'

    New-Item -ItemType Directory -Path $publish -Force | Out-Null

    foreach ($variant in @('standard', 'full'))
    {
        $payload = Join-Path $root "artifacts\payload-$variant"

        Reset-BuildDirectory $payload
        dotnet publish apps/windows/VolturaEarner.csproj `
            -c Release `
            -r win-x64 --self-contained ($variant -eq 'full').ToString().ToLowerInvariant() `
            -o $payload `
            -p:DebugType=None `
            -p:DebugSymbols=false

        if ($LASTEXITCODE)
        {
            throw 'Publish failed.'
        }

        if ($variant -eq 'full')
        {
            $nugetRoot = if ($env:NUGET_PACKAGES)
            {
                $env:NUGET_PACKAGES
            }
            else
            {
                Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'
            }
            $runtimeConfig = Get-Content -LiteralPath (Join-Path $payload 'VolturaEarner.runtimeconfig.json') -Raw |
                ConvertFrom-Json

            foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks)
            {
                $runtimePackage = Join-Path $nugetRoot ($framework.name.ToLowerInvariant() + '.runtime.win-x64\' + $framework.version)
                $noticeFiles = @(Get-ChildItem -LiteralPath $runtimePackage -File |
                        Where-Object { $_.Name -like 'LICENSE*' -or
                            $_.Name -like '*NOTICE*' })

                if (-not ($noticeFiles |
                            Where-Object Name -like 'LICENSE*'))
                {
                    throw ('Runtime license is missing: ' + $runtimePackage)
                }

                $noticeTarget = Join-Path $payload ('ThirdPartyNotices\' + $framework.name)

                New-Item -ItemType Directory -Path $noticeTarget -Force | Out-Null
                $noticeFiles | Copy-Item -Destination $noticeTarget
            }
        }

        Copy-Item installer/maintain.ps1 (Join-Path $payload 'maintain.ps1')

        $entries = @(Get-ChildItem -LiteralPath $payload -File -Recurse |
                Where-Object { $_.Name -notin @(
                        'payload.json',
                        'portable.marker',
                        'installation.marker',
                        'package.variant',
                        'Maintenance.exe',
                        'Uninstall.exe') } |
                ForEach-Object {
                    @{
                        path = $_.FullName.Substring($payload.Length + 1)
                        size = $_.Length
                        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
                    }
                })

        [IO.File]::WriteAllText(
            (Join-Path $payload 'payload.json'),
            (ConvertTo-Json -InputObject $entries -Depth 4))

        $suffix = if ($variant -eq 'full')
        {
            '-full'
        }
        else
        {
            ''
        }
        $output = Join-Path $publish "VolturaEarner-Setup-$version-win-x64$suffix.exe"

        & $nsis /WX "/DVERSION=$version" "/DVARIANT=$variant" "/DPAYLOAD=$payload" "/DOUTPUT=$output" installer/VolturaEarner.nsi

        if ($LASTEXITCODE)
        {
            throw 'NSIS compilation failed.'
        }
    }

    $portable = Join-Path $root 'artifacts\portable'

    Reset-BuildDirectory $portable
    Get-ChildItem artifacts/payload-full |
        Where-Object { $_.Name -notin @('payload.json', 'maintain.ps1') } |
        Copy-Item -Destination $portable -Recurse
    [IO.File]::WriteAllText((Join-Path $portable 'portable.marker'), 'VolturaEarner.Portable.v1')
    Compress-Archive `
        -Path "$portable\*" `
        -DestinationPath (Join-Path $publish "VolturaEarner-$version-win-x64.zip") `
        -Force
    Get-FileHash -LiteralPath @(
        (Join-Path $publish "VolturaEarner-Setup-$version-win-x64.exe"),
        (Join-Path $publish "VolturaEarner-Setup-$version-win-x64-full.exe"),
        (Join-Path $publish "VolturaEarner-$version-win-x64.zip")
    ) -Algorithm SHA256 | Format-Table -AutoSize
}
finally
{
    Pop-Location
}
