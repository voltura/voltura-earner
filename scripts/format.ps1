param([switch]$Check)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

& "$root/tools/format-powershell.ps1" -Check:$Check

foreach ($project in @("$root/VolturaEarner.slnx", "$root/tools/CodeStyle/CodeStyle.csproj"))
{
    if ($Check)
    {
        dotnet format style $project --diagnostics IDE0011 --verify-no-changes --no-restore
    }
    else
    {
        dotnet format style $project --diagnostics IDE0011 --no-restore
    }

    if ($LASTEXITCODE)
    {
        throw 'C# brace formatting failed. Run scripts/format.ps1 to fix it.'
    }

    if ($Check)
    {
        dotnet format whitespace $project --verify-no-changes --no-restore
    }
    else
    {
        dotnet format whitespace $project --no-restore
    }

    if ($LASTEXITCODE)
    {
        throw 'C# formatting failed. Run scripts/format.ps1 to fix it.'
    }
}

dotnet run --project "$root/tools/CodeStyle/CodeStyle.csproj" -- --self-test

if ($LASTEXITCODE)
{
    throw 'C# formatter regression checks failed.'
}

if ($Check)
{
    dotnet run --project "$root/tools/CodeStyle/CodeStyle.csproj" --no-build -- $root --check
}
else
{
    dotnet run --project "$root/tools/CodeStyle/CodeStyle.csproj" --no-build -- $root
}

if ($LASTEXITCODE)
{
    throw 'C# formatting check failed. Run scripts/format.ps1 to fix it.'
}
