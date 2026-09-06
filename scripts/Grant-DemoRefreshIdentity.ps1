[CmdletBinding()]
param(
    [string]$IdentityPrincipalId = 'f60bde02-b148-4a4c-a9ae-fd64f934c4fb',
    [string]$IdentityClientId = '6396645c-79a9-4c02-811e-e60e498df110',
    [string]$IdentityName = 'sati-demo-refresh-satilogica',
    [string]$SqlServer = 'sati-demo-satilogica-central.database.windows.net',
    [string]$Database = 'SatiDemo'
)

$ErrorActionPreference = 'Stop'
if ($Database -cne 'SatiDemo') { throw 'This grant is restricted to SatiDemo.' }
if ($IdentityName -cne 'sati-demo-refresh-satilogica') {
    throw 'This grant is restricted to the named Sati Demo refresh identity.'
}

$principalId = [Guid]::Parse($IdentityPrincipalId)
$clientId = [Guid]::Parse($IdentityClientId)
$servicePrincipal = az ad sp show --id $principalId --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $servicePrincipal.id -cne $principalId.ToString() -or
    $servicePrincipal.appId -cne $clientId.ToString() -or
    $servicePrincipal.servicePrincipalType -cne 'ManagedIdentity') {
    throw 'The supplied principal and client IDs do not match the managed identity in Azure.'
}

# Microsoft Entra applications, including managed identities, authenticate to
# Azure SQL by their application (client) ID. The directory object/principal ID
# identifies the service principal for Azure resource management, but it is not
# the SID SQL matches against the token's appid claim.
$sid = '0x' + (($clientId.ToByteArray() | ForEach-Object { $_.ToString('X2') }) -join '')
$token = az account get-access-token --resource 'https://database.windows.net/' --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
    throw 'Could not obtain the signed-in Azure SQL access token.'
}

$connection = [System.Data.SqlClient.SqlConnection]::new(
    "Server=$SqlServer;Database=$Database;Encrypt=true;TrustServerCertificate=false;Connect Timeout=30;")
$connection.AccessToken = $token
$connection.Open()
try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 60
    $command.CommandText = @"
IF DB_NAME() <> N'SatiDemo' OR NOT EXISTS
   (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND EnvironmentName=N'Demo')
    THROW 51000, 'Refusing to grant access outside the validated Demo database.', 1;

IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name=N'$IdentityName' AND sid<>$sid)
BEGIN
    IF IS_ROLEMEMBER(N'sati_demo_refresh', N'$IdentityName') = 1
        ALTER ROLE [sati_demo_refresh] DROP MEMBER [$IdentityName];
    DROP USER [$IdentityName];
END;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name=N'$IdentityName')
    CREATE USER [$IdentityName] WITH SID=$sid, TYPE=E;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name=N'sati_demo_refresh' AND type='R')
    CREATE ROLE [sati_demo_refresh];

GRANT SELECT, INSERT, UPDATE ON SCHEMA::dbo TO [sati_demo_refresh];
IF IS_ROLEMEMBER(N'sati_demo_refresh', N'$IdentityName') <> 1
    ALTER ROLE [sati_demo_refresh] ADD MEMBER [$IdentityName];
"@
    [void]$command.ExecuteNonQuery()

    $verify = $connection.CreateCommand()
    $verify.CommandText = @"
SELECT COUNT(*)
FROM sys.database_role_members membership
JOIN sys.database_principals role ON role.principal_id=membership.role_principal_id
JOIN sys.database_principals member ON member.principal_id=membership.member_principal_id
WHERE role.name=N'sati_demo_refresh' AND member.name=N'$IdentityName' AND member.sid=$sid;
"@
    if ([int]$verify.ExecuteScalar() -ne 1) { throw 'The Demo refresh identity role membership was not verified.' }
    Write-Output "DEMO_REFRESH_IDENTITY_GRANTED identity=$IdentityName role=sati_demo_refresh database=$Database"
}
finally {
    $connection.Dispose()
}
