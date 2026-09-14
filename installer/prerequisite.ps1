$ErrorActionPreference = 'Stop'
$runtimeExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

function Test-DesktopRuntime
{
    if (-not (Test-Path -LiteralPath $runtimeExe -PathType Leaf))
    {
        return $false
    }

    $runtimes = & $runtimeExe --list-runtimes

    return $LASTEXITCODE -eq 0 -and [bool]($runtimes -match '^Microsoft\.WindowsDesktop\.App 10\.0\.\d+ ')
}

if (Test-DesktopRuntime)
{
    exit 0
}

$download = Join-Path ([IO.Path]::GetTempPath()) ('VolturaEarner-runtime-' + [Guid]::NewGuid().ToString('N') + '.exe')

try
{
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    Invoke-WebRequest `
        -UseBasicParsing `
        -Uri 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe' `
        -OutFile $download `
        -TimeoutSec 300

    $signature = Get-AuthenticodeSignature -LiteralPath $download

    if ($signature.Status -ne 'Valid' -or
        -not $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)')
    {
        throw 'Invalid Microsoft runtime signature.'
    }

    $process = Start-Process `
        -FilePath $download `
        -ArgumentList '/install /quiet /norestart' `
        -Verb RunAs `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    $result = $process.ExitCode

    $process.Dispose()

    if ($result -notin @(0, 3010, 1641))
    {
        throw ('Runtime installation failed: ' + $result)
    }

    if (-not (Test-DesktopRuntime))
    {
        throw 'The required desktop runtime is still unavailable.'
    }

    if ($result -in @(3010, 1641))
    {
        exit 3010
    }

    exit 0
}
catch
{
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
finally
{
    if (Test-Path -LiteralPath $download)
    {
        Remove-Item -LiteralPath $download -Force
    }
}
