param(
    [string]$BaseAddress = "https://sati-demo-api-satilogica.azurewebsites.net/"
)

$ErrorActionPreference = "Stop"
$expectedHost = "sati-demo-api-satilogica.azurewebsites.net"
$baseUri = [Uri]::new($BaseAddress, [UriKind]::Absolute)

if ($baseUri.Scheme -cne "https" -or $baseUri.Host -cne $expectedHost) {
    throw "This seed is hard-limited to the hosted Sati Demo API at https://$expectedHost/."
}

$username = [Environment]::GetEnvironmentVariable("SATI_DEMO_USERNAME")
$password = [Environment]::GetEnvironmentVariable("SATI_DEMO_PASSWORD")
if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($password)) {
    $credentialPath = Join-Path $env:LOCALAPPDATA "SatiLogica\Sati\Credentials\demo-agency-admin.xml"
    if (-not (Test-Path -LiteralPath $credentialPath)) {
        throw "Set SATI_DEMO_USERNAME and SATI_DEMO_PASSWORD, or create the Windows-protected Demo agency Admin credential at '$credentialPath'."
    }

    $credential = Import-Clixml -LiteralPath $credentialPath
    if ($credential -isnot [PSCredential]) {
        throw "The Demo agency Admin credential file did not contain a Windows-protected PSCredential."
    }
    $username = $credential.UserName
    $password = $credential.GetNetworkCredential().Password
}

function Get-DemoUri([string]$relativePath) {
    return [Uri]::new($baseUri, $relativePath)
}

$loginBody = @{ username = $username; password = $password } | ConvertTo-Json -Compress
$login = Invoke-RestMethod `
    -Uri (Get-DemoUri "api/v1/auth/login") `
    -Method Post `
    -ContentType "application/json" `
    -Body $loginBody `
    -TimeoutSec 90

if ([string]::IsNullOrWhiteSpace($login.accessToken) -or $login.user.role -cne "Admin") {
    throw "The Demo SSN seed requires the designated synthetic agency Admin account."
}

$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$people = Invoke-RestMethod `
    -Uri (Get-DemoUri "api/v1/admin/people") `
    -Headers $headers `
    -TimeoutSec 90

if ($people.Count -lt 1 -or $people.Count -gt 9999) {
    throw "The Demo Person count '$($people.Count)' is outside the seed's safe range."
}

$result = Invoke-RestMethod `
    -Uri (Get-DemoUri "api/v1/admin/demo/seed-ssns") `
    -Method Post `
    -Headers $headers `
    -ContentType "application/json" `
    -Body "{}" `
    -TimeoutSec 300

if ($result.count -ne $people.Count) {
    throw "The Demo API seeded '$($result.count)' SSNs for '$($people.Count)' agency People."
}

[pscustomobject]@{
    Environment = "Demo"
    ApiHost = $baseUri.Host
    People = $people.Count
    EncryptedSsnsSaved = $result.count
} | Format-List
