<#
.SYNOPSIS
    Adds or removes the temporary exact-IP SQL firewall rule that lets this
    workstation reach SatiDemo for a controlled migration.

.DESCRIPTION
    The SatiDemo SQL firewall admits only sati-demo-api-satilogica's outbound
    addresses. No developer workstation has standing access, by design. A
    controlled migration run from a workstation therefore needs a temporary
    exact-IP rule, which is a security setting.

    THIS SCRIPT IS FOR JOSH TO RUN. The release workflow never adds, alters, or
    deletes a firewall rule - see RELEASE_PLAYBOOK.md section 1.6. An assistant
    may write this script and hand it over; it may not execute it.

    The rule is a hole in the last link of the chain protecting the Demo
    database. Open it for the migration, close it immediately after, and verify
    it is gone. -Remove does the verification for you.

    ASCII only, deliberately. Windows PowerShell 5.1 reads a .ps1 without a BOM
    as ANSI, so a stray em dash or smart quote in a comment corrupts a string
    literal elsewhere in the file and the whole script fails to parse.

.PARAMETER Ip
    The exact public IPv4 address to admit. Required when adding. Deliberately
    not defaulted and never auto-detected into the rule: a wrong guess here
    silently opens the database to somebody else's address.

.PARAMETER Remove
    Delete the rule instead of creating it, then prove it is gone.

.EXAMPLE
    # 1. Open it, for this release only
    .\scripts\Set-DemoWorkstationFirewallRule.ps1 -Ip 66.211.131.66

.EXAMPLE
    # 2. Close it, the moment the migration finishes
    .\scripts\Set-DemoWorkstationFirewallRule.ps1 -Remove
#>
[CmdletBinding()]
param(
    [string]$Ip,
    [switch]$Remove,
    [string]$Server = 'sati-demo-satilogica-central',
    [string]$ResourceGroup = 'rg-sati-demo',
    [string]$RuleName = 'datt-workstation-temp'
)

$ErrorActionPreference = 'Stop'

function Show-AllowList {
    Write-Host ''
    Write-Host 'Current allow-list:' -ForegroundColor Cyan
    az sql server firewall-rule list `
        --server $Server --resource-group $ResourceGroup --output table
}

# Fail closed on identity before touching anything.
$account = az account show --output json 2>$null
if (-not $account) {
    throw "Not signed in to Azure. Run 'az login' first."
}
$subscription = ($account | ConvertFrom-Json).name
Write-Host "Subscription: $subscription" -ForegroundColor DarkGray
Write-Host "Server:       $Server (resource group $ResourceGroup)" -ForegroundColor DarkGray

if ($Remove) {
    Write-Host "Removing firewall rule '$RuleName'..." -ForegroundColor Yellow
    az sql server firewall-rule delete `
        --name $RuleName --server $Server --resource-group $ResourceGroup

    # The verification is the point of the -Remove path. A rule you believe you
    # deleted is worth nothing; a listing that no longer contains it is evidence.
    $remaining = az sql server firewall-rule list `
        --server $Server --resource-group $ResourceGroup --output json | ConvertFrom-Json

    if ($remaining | Where-Object { $_.name -eq $RuleName }) {
        Show-AllowList
        throw "Rule '$RuleName' is STILL PRESENT. Do not leave it open; remove it before finishing."
    }

    Write-Host "Confirmed: '$RuleName' is gone." -ForegroundColor Green
    $unexpected = $remaining | Where-Object { $_.name -notlike 'sati-demo-api-outbound-*' }
    if ($unexpected) {
        Write-Warning 'The allow-list holds entries that are not sati-demo-api-outbound-*:'
        $unexpected | ForEach-Object { Write-Warning "  $($_.name)  $($_.startIpAddress)-$($_.endIpAddress)" }
    }
    else {
        Write-Host 'Allow-list holds only the expected sati-demo-api-outbound-* entries.' -ForegroundColor Green
    }
    Show-AllowList
    return
}

if (-not $Ip) {
    $detected = try { (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 15).Trim() } catch { $null }
    $hint = if ($detected) { " This machine currently appears as $detected." } else { '' }
    throw "-Ip is required when adding the rule.$hint Pass it explicitly so nothing is guessed."
}

if ($Ip -notmatch '^\d{1,3}(\.\d{1,3}){3}$') {
    throw "'$Ip' is not an IPv4 address. Pass a single exact address, not a range or CIDR."
}

$parsedIp = [System.Net.IPAddress]::None
if (-not [System.Net.IPAddress]::TryParse($Ip, [ref]$parsedIp) -or
    $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "'$Ip' is not a valid IPv4 address. Pass a single exact address, not a range or CIDR."
}

# Never silently repoint an existing security rule. If this run's exact rule is
# already open, report that fact; if the name points anywhere else, require the
# operator to inspect and remove it deliberately before continuing.
$existingRules = az sql server firewall-rule list `
    --server $Server --resource-group $ResourceGroup --output json | ConvertFrom-Json
$existingRule = $existingRules | Where-Object { $_.name -eq $RuleName } | Select-Object -First 1
if ($existingRule) {
    if ($existingRule.startIpAddress -eq $Ip -and $existingRule.endIpAddress -eq $Ip) {
        Write-Host "Rule '$RuleName' is already open for exactly $Ip." -ForegroundColor Yellow
        Show-AllowList
        return
    }

    throw "Rule '$RuleName' already exists for $($existingRule.startIpAddress)-$($existingRule.endIpAddress). Refusing to overwrite it."
}

# A single address, not a range. Start and end are deliberately identical.
Write-Host "Adding firewall rule '$RuleName' for $Ip only..." -ForegroundColor Yellow
az sql server firewall-rule create `
    --name $RuleName --server $Server --resource-group $ResourceGroup `
    --start-ip-address $Ip --end-ip-address $Ip

$created = az sql server firewall-rule show `
    --name $RuleName --server $Server --resource-group $ResourceGroup `
    --output json | ConvertFrom-Json
if ($created.startIpAddress -ne $Ip -or $created.endIpAddress -ne $Ip) {
    throw "Rule '$RuleName' was created with unexpected bounds. Remove it immediately with -Remove."
}

Show-AllowList
Write-Host ''
Write-Host 'REMINDER: run this with -Remove as soon as the migration finishes.' -ForegroundColor Yellow
