[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [string]$WorkingRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($WorkingRoot)) {
    $WorkingRoot = Join-Path $repoRoot 'artifacts\SatiLocalInstallerAcceptance'
}
$installer = [System.IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Installer not found: $installer"
}

$installerName = [System.IO.Path]::GetFileName($installer)
if ($installerName -notmatch '^SatiLocalSetup-(\d+\.\d+\.\d+)\.exe$') {
    throw "Installer name must match SatiLocalSetup-x.y.z.exe: $installerName"
}
$expectedVersion = $Matches[1]

$acceptanceRoot = [System.IO.Path]::GetFullPath($WorkingRoot)
if ($acceptanceRoot -eq [System.IO.Path]::GetPathRoot($acceptanceRoot)) {
    throw 'WorkingRoot cannot be a drive root.'
}
$runRoot = Join-Path $acceptanceRoot ('run-' + [Guid]::NewGuid().ToString('N'))
$priorTestMode = [Environment]::GetEnvironmentVariable('SATI_LOCAL_INSTALLER_TEST')
$priorInstallRoot = [Environment]::GetEnvironmentVariable('SATI_LOCAL_INSTALL_ROOT')

try {
    [System.IO.Directory]::CreateDirectory($runRoot) | Out-Null
    $env:SATI_LOCAL_INSTALLER_TEST = '1'
    $env:SATI_LOCAL_INSTALL_ROOT = $runRoot
    $install = Start-Process `
        -FilePath $installer `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($install.ExitCode -ne 0) {
        $bootstrapErrorPath = Join-Path $runRoot 'bootstrap-error.txt'
        $installErrorPath = Join-Path $runRoot 'install-error.txt'
        $bootstrapError = if (Test-Path -LiteralPath $installErrorPath -PathType Leaf) {
            (Get-Content -LiteralPath $installErrorPath -Raw).Trim()
        } elseif (Test-Path -LiteralPath $bootstrapErrorPath -PathType Leaf) {
            (Get-Content -LiteralPath $bootstrapErrorPath -Raw).Trim()
        } else { 'No bootstrap diagnostic was written.' }
        throw "Installer exited with code $($install.ExitCode). $bootstrapError"
    }

    # Large self-extracting packages can return from the outer IExpress process while its
    # extraction helper is still handing off to the PowerShell installer. Wait for the payload
    # marker instead of racing that helper and reporting a false missing-file failure.
    $payloadDeadline = (Get-Date).AddMinutes(2)
    while (-not (Test-Path -LiteralPath (Join-Path $runRoot 'Sati.exe') -PathType Leaf) -and
           (Get-Date) -lt $payloadDeadline) {
        Start-Sleep -Milliseconds 500
    }

    $requiredFiles = @(
        'Sati.exe',
        'appsettings.json',
        'Uninstall-SatiLocal.ps1',
        'Run-PowerShellHidden.vbs')
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $runRoot $requiredFile) -PathType Leaf)) {
            throw "Installed payload is missing '$requiredFile'."
        }
    }
    $versionedIcon = Join-Path $runRoot "Sati.$expectedVersion.ico"
    if (-not (Test-Path -LiteralPath $versionedIcon -PathType Leaf)) {
        throw "Installed payload is missing the versioned icon 'Sati.$expectedVersion.ico'."
    }

    $privateConfiguration = Get-Content -LiteralPath (Join-Path $runRoot 'appsettings.json') -Raw
    if ($privateConfiguration -match '(?i)Password\s*=' -or
        $privateConfiguration -match '(?i)User ID\s*=') {
        throw 'The installed LocalDB configuration contains a SQL username or password.'
    }
    if ($privateConfiguration -notmatch '(?i)(Trusted_Connection|Integrated Security)\s*=\s*true') {
        throw 'The installed LocalDB configuration does not use Windows integrated security.'
    }

    $actualVersion = (Get-Item -LiteralPath (Join-Path $runRoot 'Sati.exe')).VersionInfo.FileVersion
    if (-not $actualVersion.StartsWith("$expectedVersion.", [StringComparison]::Ordinal)) {
        throw "Installed version '$actualVersion' does not match installer version '$expectedVersion'."
    }

    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Output "LOCAL_INSTALLER_ACCEPTANCE_PASSED installer=$installerName sha256=$hash version=$actualVersion integratedSecurity=True"
}
finally {
    if ($null -eq $priorTestMode) {
        Remove-Item Env:SATI_LOCAL_INSTALLER_TEST -ErrorAction SilentlyContinue
    }
    else {
        $env:SATI_LOCAL_INSTALLER_TEST = $priorTestMode
    }
    if ($null -eq $priorInstallRoot) {
        Remove-Item Env:SATI_LOCAL_INSTALL_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:SATI_LOCAL_INSTALL_ROOT = $priorInstallRoot
    }

    if (Test-Path -LiteralPath $runRoot) {
        $resolvedRunRoot = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $runRoot))
        if ([System.IO.Directory]::GetParent($resolvedRunRoot).FullName -cne $acceptanceRoot -or
            [System.IO.Path]::GetFileName($resolvedRunRoot) -notlike 'run-*') {
            throw "Refusing to clean unexpected acceptance path: $resolvedRunRoot"
        }
        $reparsePoints = @(Get-ChildItem -LiteralPath $resolvedRunRoot -Recurse -Force | Where-Object {
            $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint
        })
        if ($reparsePoints.Count -ne 0) {
            throw 'Refusing cleanup because the acceptance folder contains a reparse point.'
        }
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
        Write-Output 'LOCAL_INSTALLER_ACCEPTANCE_CLEANUP_PASSED'
    }
}
