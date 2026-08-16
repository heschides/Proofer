# Create or repair a Sati account with an elevated role, from outside the app.
#
# NOT the normal path. First launch against a database with no Admin opens the
# Create Administrator window (FirstRunAdminWindow), which is how the first admin is
# meant to be made. This script is the recovery hatch for when that is not available:
# the sole admin is locked out or forgotten, or an account needs a role the UI will not
# grant. It also re-points an existing username, so it doubles as a password reset.
#
# Background on why an outside tool is needed at all: the login screen's "Create an
# account" always produces a CaseManager (NewUserViewModel hardcodes
# UserRole.CaseManager), and the only role editor -- Supervisor dashboard >
# User Management -- sits behind a tab requiring Supervisor/Admin/Director.
#
# Passwords use the same algorithm as Data/PasswordHasher.cs: PBKDF2-SHA256, 100,000
# iterations, 16-byte salt, 32-byte key, both stored base64. Role is stored as its enum
# NAME because SatiContext maps it with HasConversion<string>().
#
# Usage:  .\New-SatiUser.ps1                      (prompts for everything)
#         .\New-SatiUser.ps1 -Username josh -Role Admin

[CmdletBinding()]
param(
    [string]$Username,
    [string]$DisplayName,
    [ValidateSet('CaseManager', 'Supervisor', 'Director', 'Admin')]
    [string]$Role = 'Admin',
    [int]$AgencyId = 1,
    [string]$ConnectionString = 'Server=(localdb)\MSSQLLocalDB;Database=Sati;Trusted_Connection=True;TrustServerCertificate=True'
)

if (-not $Username)    { $Username    = Read-Host 'Username' }
if (-not $DisplayName) { $DisplayName = Read-Host 'Display name' }

$secure  = Read-Host "Password for '$Username'" -AsSecureString
$confirm = Read-Host 'Confirm password' -AsSecureString

function ConvertFrom-Secure([System.Security.SecureString]$s) {
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($s)
    try { [Runtime.InteropServices.Marshal]::PtrToStringUni($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($ptr) }
}

$plain = ConvertFrom-Secure $secure
if ($plain -ne (ConvertFrom-Secure $confirm)) { throw 'Passwords do not match.' }
if ([string]::IsNullOrWhiteSpace($plain))     { throw 'Password cannot be empty.' }

# Mirror of PasswordHasher.HashPassword. Note the app can call the static
# RandomNumberGenerator.GetBytes(int) because it targets .NET 10; Windows PowerShell 5.1
# runs on .NET Framework, where that overload does not exist -- hence Create()/GetBytes().
$salt = New-Object byte[] 16
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($salt) } finally { $rng.Dispose() }
$kdf  = New-Object System.Security.Cryptography.Rfc2898DeriveBytes(
    [System.Text.Encoding]::UTF8.GetBytes($plain),
    $salt,
    100000,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256)
$hash = $kdf.GetBytes(32)
$plain = $null

$conn = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
$conn.Open()
try {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = @'
IF EXISTS (SELECT 1 FROM Users WHERE Username = @Username)
    UPDATE Users
       SET DisplayName = @DisplayName, PasswordHash = @Hash, Salt = @Salt,
           Role = @Role, AgencyId = @AgencyId
     WHERE Username = @Username;
ELSE
    INSERT INTO Users (Username, DisplayName, PasswordHash, Salt, Role, AgencyId)
    VALUES (@Username, @DisplayName, @Hash, @Salt, @Role, @AgencyId);
'@
    [void]$cmd.Parameters.AddWithValue('@Username', $Username)
    [void]$cmd.Parameters.AddWithValue('@DisplayName', $DisplayName)
    [void]$cmd.Parameters.AddWithValue('@Hash', [Convert]::ToBase64String($hash))
    [void]$cmd.Parameters.AddWithValue('@Salt', [Convert]::ToBase64String($salt))
    [void]$cmd.Parameters.AddWithValue('@Role', $Role)
    [void]$cmd.Parameters.AddWithValue('@AgencyId', $AgencyId)
    [void]$cmd.ExecuteNonQuery()

    $cmd2 = $conn.CreateCommand()
    $cmd2.CommandText = "SELECT Id, Username, DisplayName, Role, AgencyId FROM Users WHERE Username = @u"
    [void]$cmd2.Parameters.AddWithValue('@u', $Username)
    $r = $cmd2.ExecuteReader()
    while ($r.Read()) {
        "Account ready -> Id=$($r[0])  $($r[1])  '$($r[2])'  Role=$($r[3])  AgencyId=$($r[4])"
    }
    $r.Close()
}
finally { $conn.Close() }
