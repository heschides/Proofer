<#
.SYNOPSIS
    Triggered WebJob entry point for the SatiDemo migration-history reconciliation.

.DESCRIPTION
    Runs inside sati-demo-api-satilogica, so it reaches SatiDemo through the App Service outbound
    addresses that are already on the SQL allow-list. No temporary workstation firewall rule is
    involved, which is the entire reason this job exists.

    It defaults to the rollback-only dry run. A real run requires the application setting
    SATI_RECONCILIATION_MODE to be exactly "apply", so triggering this job by accident, or leaving
    it triggerable, cannot change migration history. Flip the setting deliberately, run, then set
    it back.

    The job is manual-only. There is no settings.job schedule on purpose: a migration-history
    change is a decision somebody makes, not something that happens on a timer.

.NOTES
    Requires the App Service managed identity to hold DDL rights on SatiDemo. That grant is a
    security setting and is made by a human, not by this job or by any agent.
#>
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$reconciliation = Join-Path $scriptRoot 'Apply-DemoHistoryReconciliation.ps1'
if (-not (Test-Path -LiteralPath $reconciliation)) {
    throw "The reconciliation script was not packaged next to this job: $reconciliation"
}

$sqlServer = $env:SATI_DEMO_SQL_SERVER
if ([string]::IsNullOrWhiteSpace($sqlServer)) {
    $sqlServer = 'sati-demo-satilogica-central.database.windows.net'
}

# Anything other than the exact string "apply" is a dry run, including the setting being absent,
# empty, or misspelled. Fail safe is the default, not the exception.
$mode = $env:SATI_RECONCILIATION_MODE
$isApply = $mode -ceq 'apply'

Write-Output "SatiDemo history reconciliation"
Write-Output "  server : $sqlServer"
Write-Output "  mode   : $(if ($isApply) { 'APPLY (history will change)' } else { 'dry run (transaction rolled back)' })"
if (-not $isApply -and -not [string]::IsNullOrWhiteSpace($mode)) {
    Write-Output "  note   : SATI_RECONCILIATION_MODE is '$mode', which is not the exact string 'apply'."
}

$arguments = @{
    DatabaseName        = 'SatiDemo'
    SqlServer           = $sqlServer
    UseManagedIdentity  = $true
}
if (-not $isApply) { $arguments['WhatIfOnly'] = $true }

# A triggered WebJob reports success or failure by exit code, and `&` on a script that throws does
# not set $LASTEXITCODE. Set it explicitly so a failed reconciliation shows as a failed job rather
# than a green one with an error buried in the log.
try {
    & $reconciliation @arguments
}
catch {
    Write-Output "Reconciliation FAILED: $($_.Exception.Message)"
    exit 1
}

Write-Output 'Reconciliation job completed.'
exit 0
