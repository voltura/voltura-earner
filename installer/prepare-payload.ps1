param([Parameter(Mandatory = $true)][string]$Payload)
$ErrorActionPreference = 'Stop'

Set-StrictMode -Version Latest

# Direct assignment unwraps ConvertFrom-Json's array consistently in Windows
# PowerShell 5.1 and PowerShell 7. An array subexpression around the pipeline
# instead retains an extra array wrapper in Windows PowerShell 5.1.

$manifestPath = Join-Path $Payload 'payload.json'
$entries = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest = @($entries)

foreach ($entry in $manifest)
{
    if (-not $entry.path -or $entry.path -eq 'Uninstall.exe')
    {
        throw 'Invalid original payload manifest.'
    }
}

$uninstaller = Get-Item -LiteralPath (Join-Path $Payload 'Uninstall.exe')
$manifest += @{

    path = 'Uninstall.exe'
    size = $uninstaller.Length
    sha256 = (Get-FileHash -LiteralPath $uninstaller.FullName -Algorithm SHA256).Hash
}

[IO.File]::WriteAllText($manifestPath, (ConvertTo-Json -InputObject $manifest -Depth 4))
