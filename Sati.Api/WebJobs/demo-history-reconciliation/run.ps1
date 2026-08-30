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
    The reconciliation writes only to dbo.__EFMigrationsHistory and reads catalog views. It issues
    no CREATE, ALTER, or DROP, so it needs db_datawriter rather than db_ddladmin, and the App
    Service identity already holds datawriter in order to serve the API. This job most likely
    requires no additional grant. If a permission is missing it fails on the write and changes
    nothing, which is how to establish the answer rather than granting speculatively.

    db_ddladmin becomes necessary when Sati.Migrator applies real schema migrations, not here. Any
    such grant is a security setting made by a person, not by this job or by any agent.
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

# Exactly "apply" runs for real. Exactly "proofs" reports every failing schema proof and writes
# nothing. Anything else — absent, empty, or misspelled — is the rollback-only dry run. Fail safe is
# the default, not the exception.
$mode = $env:SATI_RECONCILIATION_MODE
$isApply = $mode -ceq 'apply'
$isProofs = $mode -ceq 'proofs'

$modeLabel = if ($isApply) { 'APPLY (history will change)' }
             elseif ($isProofs) { 'proofs only (reports every failed proof, writes nothing)' }
             else { 'dry run (transaction rolled back)' }

Write-Output "SatiDemo history reconciliation"
Write-Output "  server : $sqlServer"
Write-Output "  mode   : $modeLabel"
if (-not $isApply -and -not $isProofs -and -not [string]::IsNullOrWhiteSpace($mode)) {
    Write-Output "  note   : SATI_RECONCILIATION_MODE is '$mode', which is neither 'apply' nor 'proofs'."
}

$arguments = @{
    DatabaseName        = 'SatiDemo'
    SqlServer           = $sqlServer
    UseManagedIdentity  = $true
}
if ($isProofs) { $arguments['ProofsOnly'] = $true }
elseif (-not $isApply) { $arguments['WhatIfOnly'] = $true }

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
