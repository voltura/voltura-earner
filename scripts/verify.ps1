$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Push-Location $root

try
{
    & "$PSScriptRoot\build.ps1"

    foreach ($file in Get-ChildItem scripts, installer -Filter '*.ps1' -File -Recurse)
    {
        $tokens = $null
        $errors = $null

        [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)

        if ($errors.Count)
        {
            throw ($errors | Out-String)
        }
    }

    Write-Output 'PowerShell syntax checks passed.'
    & "$PSScriptRoot\test-release-signing.ps1"
    & "$PSScriptRoot\test-release-workflow.ps1"
}
finally
{
    Pop-Location
}
