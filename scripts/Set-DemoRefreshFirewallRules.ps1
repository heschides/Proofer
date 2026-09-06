<#
.SYNOPSIS
    Previews, adds, or removes the exact Azure SQL firewall rules required by
    Sati's daily Demo refresh Function.

.DESCRIPTION
    THIS SCRIPT IS FOR JOSH TO RUN. A coding assistant may prepare and validate
    it, but must not execute a security-setting change.

    Azure Functions Consumption apps can use any address in their reported
    possible outbound set. Each address is admitted as its own exact-IP rule;
    no range and no general "Allow Azure services" rule is used. Network access
    alone is insufficient: SQL also requires the refresh Function's separately
    granted managed identity.

    With no switch, this script is read-only and previews the required rules.
    Use -Apply to create missing exact-IP rules. Use -Remove only if the refresh
    worker is being retired; it removes rules with the dedicated refresh prefix
    and leaves API and workstation rules untouched.

.EXAMPLE
    .\scripts\Set-DemoRefreshFirewallRules.ps1

.EXAMPLE
    .\scripts\Set-DemoRefreshFirewallRules.ps1 -Apply
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$Remove,
    [string]$ResourceGroup = 'rg-sati-demo',
    [string]$FunctionApp = 'sati-demo-refresh-satilogica',
    [string]$SqlServer = 'sati-demo-satilogica-central',
    [string]$ExpectedSubscriptionId = '253e5008-51c0-434b-80b9-ae3ac94bd66b'
)

$ErrorActionPreference = 'Stop'
$rulePrefix = 'sati-demo-refresh-outbound-'

if ($Apply -and $Remove) {
    throw 'Choose either -Apply or -Remove, not both.'
}

function Invoke-AzureCli([string[]]$Arguments) {
    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Arguments -join ' ')"
    }
    return $output
}

$account = Invoke-AzureCli @('account', 'show', '--output', 'json') |
    Out-String | ConvertFrom-Json
if ($account.id -cne $ExpectedSubscriptionId) {
    throw "Wrong Azure subscription. Expected '$ExpectedSubscriptionId'; found '$($account.id)'."
}

$function = Invoke-AzureCli @(
    'functionapp', 'show', '-g', $ResourceGroup, '-n', $FunctionApp,
    '--query', '{id:id,principalId:identity.principalId,ips:possibleOutboundIpAddresses}',
    '--output', 'json') | Out-String | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($function.principalId)) {
    throw "Function App '$FunctionApp' does not have a managed identity."
}

$ips = @($function.ips -split ',' | ForEach-Object { $_.Trim() } |
    Where-Object { $_ } | Sort-Object -Unique)
if ($ips.Count -eq 0) {
    throw "Function App '$FunctionApp' reported no possible outbound addresses."
}
foreach ($ip in $ips) {
    $parsedIp = [System.Net.IPAddress]::None
    if (-not [System.Net.IPAddress]::TryParse($ip, [ref]$parsedIp) -or
        $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        throw "Function App '$FunctionApp' reported an invalid IPv4 address: '$ip'."
    }
}

$existing = @(Invoke-AzureCli @(
    'sql', 'server', 'firewall-rule', 'list', '-g', $ResourceGroup,
    '-s', $SqlServer, '--output', 'json') | Out-String | ConvertFrom-Json)

if ($Remove) {
    $refreshRules = @($existing | Where-Object { $_.name -like "$rulePrefix*" })
    foreach ($rule in $refreshRules) {
        Invoke-AzureCli @(
            'sql', 'server', 'firewall-rule', 'delete', '-g', $ResourceGroup,
            '-s', $SqlServer, '-n', $rule.name) | Out-Null
    }

    $remaining = @(Invoke-AzureCli @(
        'sql', 'server', 'firewall-rule', 'list', '-g', $ResourceGroup,
        '-s', $SqlServer, '--output', 'json') | Out-String | ConvertFrom-Json)
    if ($remaining | Where-Object { $_.name -like "$rulePrefix*" }) {
        throw 'One or more Demo refresh firewall rules remain after removal.'
    }

    Write-Host "Removed and verified $($refreshRules.Count) Demo refresh rule(s)." -ForegroundColor Green
    return
}

$required = foreach ($ip in $ips) {
    $name = "$rulePrefix$($ip.Replace('.', '-'))"
    $matching = @($existing | Where-Object {
        $_.startIpAddress -eq $ip -and $_.endIpAddress -eq $ip
    })
    [pscustomobject]@{
        Name = $name
        Ip = $ip
        AlreadyAllowed = $matching.Count -gt 0
    }
}

$required | Format-Table Name, Ip, AlreadyAllowed -AutoSize
$missing = @($required | Where-Object { -not $_.AlreadyAllowed })
Write-Host "Function identity: $($function.principalId)" -ForegroundColor DarkGray
Write-Host "Exact addresses: $($required.Count); missing rules: $($missing.Count)." -ForegroundColor Cyan

if (-not $Apply) {
    Write-Host 'Preview only. Rerun with -Apply to add the missing exact-IP rules.' -ForegroundColor Yellow
    return
}

foreach ($rule in $missing) {
    $sameName = $existing | Where-Object { $_.name -ceq $rule.Name } | Select-Object -First 1
    if ($sameName) {
        throw "Rule '$($rule.Name)' already exists with unexpected bounds. Refusing to overwrite it."
    }

    Invoke-AzureCli @(
        'sql', 'server', 'firewall-rule', 'create', '-g', $ResourceGroup,
        '-s', $SqlServer, '-n', $rule.Name,
        '--start-ip-address', $rule.Ip, '--end-ip-address', $rule.Ip) | Out-Null
}

$verified = @(Invoke-AzureCli @(
    'sql', 'server', 'firewall-rule', 'list', '-g', $ResourceGroup,
    '-s', $SqlServer, '--output', 'json') | Out-String | ConvertFrom-Json)
$notAllowed = @($ips | Where-Object {
    $ip = $_
    -not ($verified | Where-Object {
        $_.startIpAddress -eq $ip -and $_.endIpAddress -eq $ip
    })
})
if ($notAllowed.Count -gt 0) {
    throw "Refresh allow-list validation failed for: $($notAllowed -join ', ')."
}

Write-Host "Added $($missing.Count) rule(s); all $($ips.Count) exact Function addresses are allowed." -ForegroundColor Green
