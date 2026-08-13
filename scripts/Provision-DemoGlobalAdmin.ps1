param(
    [ValidateSet('SatiDemo')]
    [string]$DatabaseName = 'SatiDemo',
    [string]$SqlServer = '(localdb)\MSSQLLocalDB',
    [string]$AccessToken,
    [string]$Username = 'global-admin',
    [string]$DisplayName = 'Global Admin',
    [int]$AnchorAgencyId = 1,
    [securestring]$Password,
    [string]$CredentialPath = (Join-Path $env:LOCALAPPDATA 'Satilogica\Sati\Credentials\demo-global-admin.xml')
)

$ErrorActionPreference = 'Stop'

if ($DatabaseName -cne 'SatiDemo') {
    throw 'The Global Admin provisioner is restricted to SatiDemo.'
}
if ($Username -notmatch '^[a-z0-9][a-z0-9._-]{2,49}$') {
    throw 'Username must contain 3-50 lowercase letters, numbers, dots, hyphens, or underscores.'
}
if ([string]::IsNullOrWhiteSpace($DisplayName) -or $DisplayName.Length -gt 150) {
    throw 'DisplayName must contain 1-150 characters.'
}

function New-RandomPassword {
    $bytes = [byte[]]::new(24)
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }
    return 'Ga!' + [Convert]::ToBase64String($bytes).Replace('+', 'A').Replace('/', 'b').TrimEnd('=')
}

$generatedPassword = $false
if ($null -eq $Password) {
    $plainPassword = New-RandomPassword
    $Password = ConvertTo-SecureString $plainPassword -AsPlainText -Force
    $generatedPassword = $true
}
else {
    $credential = [System.Net.NetworkCredential]::new('', $Password)
    $plainPassword = $credential.Password
}

try {
    if ($plainPassword.Length -lt 16 -or $plainPassword.Length -gt 128) {
        throw 'The Global Admin password must contain 16-128 characters.'
    }

    $saltBytes = [byte[]]::new(16)
    $saltGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $saltGenerator.GetBytes($saltBytes)
    }
    finally {
        $saltGenerator.Dispose()
    }
    $passwordBytes = [System.Text.Encoding]::UTF8.GetBytes($plainPassword)
    $deriver = [System.Security.Cryptography.Rfc2898DeriveBytes]::new(
        $plainPassword,
        $saltBytes,
        100000,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $hashBytes = $deriver.GetBytes(32)
    }
    finally {
        $deriver.Dispose()
    }
    $salt = [Convert]::ToBase64String($saltBytes)
    $hash = [Convert]::ToBase64String($hashBytes)

    if ($generatedPassword) {
        $resolvedCredentialPath = [System.IO.Path]::GetFullPath($CredentialPath)
        $credentialDirectory = [System.IO.Path]::GetDirectoryName($resolvedCredentialPath)
        if ([string]::IsNullOrWhiteSpace($credentialDirectory)) {
            throw 'CredentialPath must include a parent directory.'
        }
        [System.IO.Directory]::CreateDirectory($credentialDirectory) | Out-Null
        if (Test-Path -LiteralPath $resolvedCredentialPath) {
            throw "Refusing to overwrite the existing encrypted credential file: $resolvedCredentialPath"
        }
        $pendingCredentialPath = "$resolvedCredentialPath.pending-$([Guid]::NewGuid().ToString('N'))"
        [pscredential]::new($Username, $Password) | Export-Clixml -LiteralPath $pendingCredentialPath
    }

    $connectionString = if ([string]::IsNullOrWhiteSpace($AccessToken)) {
        "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=15;"
    }
    else {
        "Server=$SqlServer;Database=$DatabaseName;Encrypt=true;TrustServerCertificate=false;Connect Timeout=30;"
    }
    $connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
    if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
        $connection.AccessToken = $AccessToken
    }
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 60
        $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'SatiDemo'
    THROW 51100, 'The Global Admin provisioner can run only in SatiDemo.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 51101, 'The selected database does not have the Demo identity marker.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.Agencies WHERE Id = @agencyId)
    THROW 51102, 'The anchor agency does not exist.', 1;
IF EXISTS (SELECT 1 FROM dbo.Users WHERE Username = @username AND Role <> N'PlatformOperator')
    THROW 51103, 'The username belongs to an agency user and cannot be converted.', 1;

BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM dbo.Users WHERE Username = @username)
BEGIN
    UPDATE dbo.Users
    SET DisplayName = @displayName,
        PasswordHash = @passwordHash,
        Salt = @salt,
        Role = N'PlatformOperator',
        SupervisorId = NULL,
        AgencyId = @agencyId,
        Email = NULL,
        Phone = NULL
    WHERE Username = @username;
END
ELSE
BEGIN
    INSERT dbo.Users (Username, DisplayName, PasswordHash, Salt, Role, SupervisorId, AgencyId, Email, Phone)
    VALUES (@username, @displayName, @passwordHash, @salt, N'PlatformOperator', NULL, @agencyId, NULL, NULL);
END;
COMMIT TRANSACTION;

SELECT Id, Username, DisplayName, Role, AgencyId
FROM dbo.Users
WHERE Username = @username;
'@
        [void]$command.Parameters.Add('@username', [System.Data.SqlDbType]::NVarChar, 50)
        [void]$command.Parameters.Add('@displayName', [System.Data.SqlDbType]::NVarChar, 150)
        [void]$command.Parameters.Add('@passwordHash', [System.Data.SqlDbType]::NVarChar, 4000)
        [void]$command.Parameters.Add('@salt', [System.Data.SqlDbType]::NVarChar, 4000)
        [void]$command.Parameters.Add('@agencyId', [System.Data.SqlDbType]::Int)
        $command.Parameters['@username'].Value = $Username
        $command.Parameters['@displayName'].Value = $DisplayName
        $command.Parameters['@passwordHash'].Value = $hash
        $command.Parameters['@salt'].Value = $salt
        $command.Parameters['@agencyId'].Value = $AnchorAgencyId
        $reader = $command.ExecuteReader()
        if (-not $reader.Read()) {
            throw 'The Global Admin row was not returned after provisioning.'
        }
        $result = [ordered]@{
            UserId = $reader.GetInt32(0)
            Username = $reader.GetString(1)
            DisplayName = $reader.GetString(2)
            Role = $reader.GetString(3)
            AnchorAgencyId = $reader.GetInt32(4)
        }
        $reader.Close()
    }
    finally {
        $connection.Dispose()
    }

    if ($generatedPassword) {
        Move-Item -LiteralPath $pendingCredentialPath -Destination $resolvedCredentialPath
        $pendingCredentialPath = $null
        $result.CredentialPath = $resolvedCredentialPath
        $result.CredentialProtection = 'Windows user-bound DPAPI encryption'
    }
    else {
        $result.CredentialPath = 'Not written; caller supplied the password'
        $result.CredentialProtection = 'Caller responsibility'
    }

    [pscustomobject]$result
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($pendingCredentialPath) -and
        (Test-Path -LiteralPath $pendingCredentialPath -PathType Leaf)) {
        Remove-Item -LiteralPath $pendingCredentialPath -Force
    }
    $plainPassword = $null
    $passwordBytes = $null
    $hashBytes = $null
}
