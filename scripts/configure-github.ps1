#Requires -Version 7.0
# Run explicitly after source publication is approved and main has been pushed.
[CmdletBinding()]
param([switch]$Apply)
$ErrorActionPreference = 'Stop'
$repository = 'voltura/voltura-earner'
$settings = @{
    description = 'A lean Windows companion for tracking work, earnings, and overtime.'
    homepage = 'https://voltura.github.io/voltura-earner/'
    has_issues = $true
    has_wiki = $true
    has_projects = $true
    has_discussions = $false
    allow_merge_commit = $true
    allow_squash_merge = $true
    allow_rebase_merge = $true
    delete_branch_on_merge = $false
    web_commit_signoff_required = $false
    security_and_analysis = @{
        secret_scanning = @{ status = 'enabled' }
        secret_scanning_push_protection = @{ status = 'enabled' }
    }
}

if (-not $Apply)
{
    $settings | ConvertTo-Json -Depth 5
    Write-Output 'Preview only. -Apply configures the repository and publishes GitHub Pages from main:/.'

    return
}

function Invoke-RepositoryApi([string]$Method, [string]$Endpoint, $Body)
{
    if ($null -eq $Body)
    {
        gh api --method $Method "repos/$repository$Endpoint" --silent
    }
    else
    {
        $Body | ConvertTo-Json -Depth 5 -Compress | gh api --method $Method "repos/$repository$Endpoint" --input - --silent
    }

    if ($LASTEXITCODE)
    {
        throw "GitHub configuration failed: $Method $Endpoint"
    }
}

gh api "repos/$repository/branches/main" --silent

if ($LASTEXITCODE)
{
    throw 'Push the approved main branch before configuring Pages.'
}

Invoke-RepositoryApi PATCH '' $settings
Invoke-RepositoryApi PUT '/vulnerability-alerts' $null
Invoke-RepositoryApi PUT '/automated-security-fixes' $null
Invoke-RepositoryApi PUT '/private-vulnerability-reporting' $null
Invoke-RepositoryApi PUT '/actions/permissions' @{ enabled = $true; allowed_actions = 'all'; sha_pinning_required = $false }

Invoke-RepositoryApi PUT '/actions/permissions/workflow' @{ default_workflow_permissions = 'read'; can_approve_pull_request_reviews = $false }

gh api "repos/$repository/pages" --silent 2>$null

$pagesMethod = if ($LASTEXITCODE -eq 0)
{
    'PUT'
}
else
{
    'POST'
}

Invoke-RepositoryApi $pagesMethod '/pages' @{ build_type = 'legacy'; source = @{ branch = 'main'; path = '/' } }

Write-Output 'Repository settings applied. Check Pages deployment status before sharing the website.'
