[CmdletBinding()]
param(
    [string]$ResourceGroup = 'rg-sati-demo',
    [string]$FunctionApp = 'sati-demo-refresh-satilogica',
    [string]$StorageAccount = 'satidemorefreshst',
    [string]$Location = 'centralus'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'Sati.DemoRefresh'
$seed = Join-Path $PSScriptRoot 'Seed-DemoShowcaseData.ps1'
$staging = Join-Path ([IO.Path]::GetTempPath()) "sati-demo-refresh-$([Guid]::NewGuid().ToString('N'))"
$zip = "$staging.zip"

function Invoke-AzureCli([string[]]$Arguments) {
    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Arguments -join ' ')"
    }
    return $output
}

try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $staging -Recurse
    Copy-Item -LiteralPath $seed -Destination (Join-Path $staging 'RefreshCaseload\Seed-DemoShowcaseData.ps1')
    Copy-Item -LiteralPath $seed -Destination (Join-Path $staging 'ResetDemo\Seed-DemoShowcaseData.ps1')
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -CompressionLevel Optimal

    $storageExists = [int](Invoke-AzureCli @('storage','account','list','-g',$ResourceGroup,'--query',"[?name=='$StorageAccount'] | length(@)",'-o','tsv'))
    if ($storageExists -eq 0) {
        Invoke-AzureCli @('storage','account','create','-g',$ResourceGroup,'-n',$StorageAccount,'-l',$Location,'--sku','Standard_LRS','--kind','StorageV2','--https-only','true','--min-tls-version','TLS1_2') | Out-Null
    }
    $appExists = [int](Invoke-AzureCli @('functionapp','list','-g',$ResourceGroup,'--query',"[?name=='$FunctionApp'] | length(@)",'-o','tsv'))
    if ($appExists -eq 0) {
        Invoke-AzureCli @('functionapp','create','-g',$ResourceGroup,'-n',$FunctionApp,'-s',$StorageAccount,
            '--consumption-plan-location',$Location,'--runtime','powershell','--runtime-version','7.6',
            '--functions-version','4','--os-type','Windows') | Out-Null
    }
    $identity = (Invoke-AzureCli @('functionapp','identity','assign','-g',$ResourceGroup,'-n',$FunctionApp,'-o','json') | Out-String | ConvertFrom-Json)
    Invoke-AzureCli @('functionapp','config','appsettings','set','-g',$ResourceGroup,'-n',$FunctionApp,'--settings',
        'FUNCTIONS_WORKER_RUNTIME=powershell','FUNCTIONS_EXTENSION_VERSION=~4',
        'WEBSITE_TIME_ZONE=Eastern Standard Time','DemoRefreshSchedule=0 15 3 * * *',
        'SATI_DEMO_SQL_SERVER=sati-demo-satilogica-central.database.windows.net') | Out-Null
    Invoke-AzureCli @('functionapp','deployment','source','config-zip','-g',$ResourceGroup,'-n',$FunctionApp,'--src',$zip) | Out-Null

    [pscustomobject]@{
        FunctionApp = $FunctionApp
        PrincipalId = $identity.principalId
        Schedule = '3:15 AM America/New_York daily'
        SqlGrantRequired = $true
    } | Format-List
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
}
