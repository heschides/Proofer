<#
.SYNOPSIS
    Triggered WebJob entry point for the SatiDemo Users.AgencyId default constraint.

.DESCRIPTION
    Runs inside sati-demo-api-satilogica, so it reaches SatiDemo through the App Service outbound
    addresses already on the SQL allow-list. No temporary workstation firewall rule is involved.

    Separate from demo-history-reconciliation on purpose. This one performs a schema change; that one
    changes migration history and explicitly never does DDL. Keeping them apart keeps each job's
    contract true and each trigger a distinct decision.

    Defaults to the rollback-only dry run. A real run requires the application setting
    SATI_AGENCYID_DEFAULT_MODE to be exactly "apply". Flip it deliberately, run, then set it back.

    Adding a default constraint does not read, modify, or rewrite existing rows.

.NOTES
    Requires ALTER on dbo.Users. The least-privilege grant is
    GRANT ALTER ON OBJECT::dbo.Users TO [sati-demo-api-satilogica];
    which is narrower than db_ddladmin. That grant is a security setting a person makes; no job or
    agent performs it. Without it this job fails on the ALTER and changes nothing.
#>
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$addDefault = Join-Path $scriptRoot 'Add-DemoUsersAgencyIdDefault.ps1'
if (-not (Test-Path -LiteralPath $addDefault)) {
    throw "The default-constraint script was not packaged next to this job: $addDefault"
}

$sqlServer = $env:SATI_DEMO_SQL_SERVER
if ([string]::IsNullOrWhiteSpace($sqlServer)) {
    $sqlServer = 'sati-demo-satilogica-central.database.windows.net'
}

# Anything other than the exact string "apply" is a dry run, including absent, empty, or misspelled.
$mode = $env:SATI_AGENCYID_DEFAULT_MODE
$isApply = $mode -ceq 'apply'

Write-Output 'SatiDemo Users.AgencyId default constraint'
Write-Output "  server : $sqlServer"
Write-Output "  mode   : $(if ($isApply) { 'APPLY (schema will change)' } else { 'dry run (transaction rolled back)' })"
if (-not $isApply -and -not [string]::IsNullOrWhiteSpace($mode)) {
    Write-Output "  note   : SATI_AGENCYID_DEFAULT_MODE is '$mode', which is not the exact string 'apply'."
}

$arguments = @{
    DatabaseName       = 'SatiDemo'
    SqlServer          = $sqlServer
    UseManagedIdentity = $true
}
if (-not $isApply) { $arguments['WhatIfOnly'] = $true }

# A triggered WebJob reports success or failure by exit code, and `&` on a script that throws does
# not set $LASTEXITCODE.
try {
    & $addDefault @arguments
}
catch {
    Write-Output "Default constraint FAILED: $($_.Exception.Message)"
    exit 1
}

Write-Output 'Default constraint job completed.'
exit 0
