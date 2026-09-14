$ErrorActionPreference = 'Stop'

Push-Location (Split-Path $PSScriptRoot -Parent)

try
{
    dotnet restore VolturaEarner.slnx --locked-mode

    if ($LASTEXITCODE)
    {
        throw 'Locked restore failed.'
    }

    dotnet restore tools/CodeStyle/CodeStyle.csproj --locked-mode

    if ($LASTEXITCODE)
    {
        throw 'Formatter restore failed.'
    }

    & "$PSScriptRoot/format.ps1" -Check

    dotnet build VolturaEarner.slnx -c Release -p:RestoreLockedMode=true

    if ($LASTEXITCODE)
    {
        throw 'Build failed.'
    }

    dotnet test --solution VolturaEarner.slnx -c Release --no-build

    if ($LASTEXITCODE)
    {
        throw 'Tests failed.'
    }
}
finally
{
    Pop-Location
}
