param([switch]$Check)

$ErrorActionPreference = 'Stop'

Import-Module PSScriptAnalyzer -RequiredVersion 1.25.0 -ErrorAction Stop

$root = Split-Path $PSScriptRoot -Parent
$settings = @{
    IncludeRules = @('PSPlaceOpenBrace', 'PSPlaceCloseBrace', 'PSUseConsistentIndentation', 'PSUseConsistentWhitespace')
    Rules = @{
        PSPlaceOpenBrace = @{ Enable = $true; OnSameLine = $false; NewLineAfter = $true; IgnoreOneLineBlock = $false }
        PSPlaceCloseBrace = @{ Enable = $true; NewLineAfter = $true; IgnoreOneLineBlock = $false }
        PSUseConsistentIndentation = @{ Enable = $true; Kind = 'space'; IndentationSize = 4 }
        PSUseConsistentWhitespace = @{ Enable = $true; CheckInnerBrace = $true; CheckOpenBrace = $true; CheckOpenParen = $true; CheckOperator = $true; CheckSeparator = $true }
    }
}

function Format-PowerShellSource([string]$Source)
{
    $formatted = Invoke-Formatter -ScriptDefinition ($Source -replace '\r?\n', "`r`n") -Settings $settings
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($formatted, [ref]$tokens, [ref]$errors)

    if ($errors.Count)
    {
        throw 'PowerShell formatting produced invalid syntax.'
    }

    $lines = [Collections.Generic.List[string]]::new()

    $lines.AddRange([string[]]($formatted -split '\r?\n'))

    $insert = [Collections.Generic.HashSet[int]]::new()
    $remove = [Collections.Generic.HashSet[int]]::new()
    $groups = $ast.FindAll({ param($node) $node -is [Management.Automation.Language.StatementBlockAst] -or $node -is [Management.Automation.Language.NamedBlockAst] }, $true)

    foreach ($group in $groups)
    {
        $previous = $null

        foreach ($statement in $group.Statements)
        {
            $start = $statement.Extent.StartLineNumber - 1
            $assignment = $statement -is [Management.Automation.Language.AssignmentStatementAst]
            $previousAssignment = $previous -is [Management.Automation.Language.AssignmentStatementAst]
            $control = $statement -is [Management.Automation.Language.IfStatementAst] -or $statement -is [Management.Automation.Language.LoopStatementAst] -or $statement -is [Management.Automation.Language.TryStatementAst] -or $statement -is [Management.Automation.Language.SwitchStatementAst] -or $statement -is [Management.Automation.Language.FunctionDefinitionAst]

            if ($previous)
            {
                $end = $previous.Extent.EndLineNumber - 1

                if ($assignment -and $previousAssignment)
                {
                    for ($i = $end + 1; $i -lt $start -and [string]::IsNullOrWhiteSpace($lines[$i]); $i++)
                    {
                        [void]$remove.Add($i)
                    }
                }
                elseif (($control -or ($assignment -ne $previousAssignment) -or $previous.Extent.Text.TrimEnd().EndsWith('}') -or $statement -is [Management.Automation.Language.ReturnStatementAst]) -and $start -gt $end -and -not [string]::IsNullOrWhiteSpace($lines[$start - 1]))
                {
                    [void]$insert.Add($start)
                }
            }
            elseif ($statement -is [Management.Automation.Language.ReturnStatementAst])
            {
                for ($i = $start - 1; $i -ge 0 -and [string]::IsNullOrWhiteSpace($lines[$i]); $i--)
                {
                    [void]$remove.Add($i)
                }
            }

            $previous = $statement
        }
    }

    $output = [Collections.Generic.List[string]]::new()
    $literalLines = [Collections.Generic.HashSet[int]]::new()

    foreach ($token in $tokens)
    {
        if ($token.Kind -notin @('NewLine', 'EndOfInput') -and $token.Extent.StartLineNumber -lt $token.Extent.EndLineNumber)
        {
            for ($line = $token.Extent.StartLineNumber; $line -le $token.Extent.EndLineNumber; $line++)
            {
                [void]$literalLines.Add($line - 1)
            }
        }
    }

    for ($i = 0; $i -lt $lines.Count; $i++)
    {
        if ($insert.Contains($i))
        {
            $output.Add('')
        }

        if (-not $remove.Contains($i))
        {
            if ($literalLines.Contains($i))
            {
                $output.Add($lines[$i])
            }
            else
            {
                $output.Add($lines[$i].TrimEnd())
            }
        }
    }

    $formatted = $output -join "`r`n"
    $null = [Management.Automation.Language.Parser]::ParseInput($formatted, [ref]$tokens, [ref]$errors)
    $significant = @($tokens | Where-Object Kind -ne NewLine)

    # Only adjacent brace tokens without comments represent a genuinely empty block.

    for ($i = $significant.Count - 2; $i -ge 1; $i--)
    {
        if ($significant[$i].Kind -eq 'LCurly' -and $significant[$i + 1].Kind -eq 'RCurly')
        {
            $start = $significant[$i - 1].Extent.EndOffset
            $end = $significant[$i + 1].Extent.EndOffset

            if ([string]::IsNullOrWhiteSpace($formatted.Substring($start, $significant[$i].Extent.StartOffset - $start)))
            {
                $formatted = $formatted.Remove($start, $end - $start).Insert($start, ' { }')
            }
        }
    }

    # Formatting must preserve all non-whitespace tokens, including strings and comments.

    $before = $null
    $after = $null
    $null = [Management.Automation.Language.Parser]::ParseInput($Source, [ref]$before, [ref]$errors)
    $null = [Management.Automation.Language.Parser]::ParseInput($formatted, [ref]$after, [ref]$errors)
    $originalTokens = @($before | Where-Object Kind -notin @('NewLine', 'EndOfInput'))
    $formattedTokens = @($after | Where-Object Kind -notin @('NewLine', 'EndOfInput'))

    if ($originalTokens.Count -eq $formattedTokens.Count)
    {
        for ($i = $originalTokens.Count - 1; $i -ge 0; $i--)
        {
            $old = $originalTokens[$i]
            $new = $formattedTokens[$i]

            if ($old.Kind -eq $new.Kind -and $old.Text -cne $new.Text -and ($old.Text -replace '\r\n', "`n") -ceq ($new.Text -replace '\r\n', "`n"))
            {
                $formatted = $formatted.Remove($new.Extent.StartOffset, $new.Extent.EndOffset - $new.Extent.StartOffset).Insert($new.Extent.StartOffset, $old.Text)
            }
        }
    }

    $null = [Management.Automation.Language.Parser]::ParseInput($formatted, [ref]$after, [ref]$errors)
    $oldTokens = @($before | Where-Object Kind -notin @('NewLine', 'EndOfInput') | ForEach-Object { "$($_.Kind):$($_.Text)" })
    $newTokens = @($after | Where-Object Kind -notin @('NewLine', 'EndOfInput') | ForEach-Object { "$($_.Kind):$($_.Text)" })

    if ($errors.Count -or (Compare-Object $oldTokens $newTokens -SyncWindow 0))
    {
        throw 'Formatter changed PowerShell tokens.'
    }

    return $formatted
}

$example = 'for ($i = 0; $i -lt $lines.Count; $i++) { if ($insert.Contains($i)) { $output.Add('''') }; if (-not $remove.Contains($i)) { $output.Add($lines[$i]) } }'
$result = Format-PowerShellSource $example

if ($result -match 'if\s*\([^\r\n]+\)\s*\{[^\r\n]+\}' -or (Format-PowerShellSource $result) -cne $result)
{
    throw 'PowerShell block formatting regression.'
}

$changed = 0

foreach ($folder in @('scripts', 'installer', 'tools'))
{
    foreach ($file in Get-ChildItem (Join-Path $root $folder) -Filter '*.ps1' -Recurse -File)
    {
        $source = [IO.File]::ReadAllText($file.FullName)
        $formatted = Format-PowerShellSource $source

        if ($source -cne $formatted)
        {
            $changed++

            if (-not $Check)
            {
                [IO.File]::WriteAllText($file.FullName, $formatted)
            }

            Write-Output $file.FullName
        }
    }
}

if ($Check -and $changed)
{
    throw "$changed PowerShell files need formatting. Run scripts/format.ps1."
}

Write-Output "PowerShell formatting checked: $changed files changed or requiring formatting."
