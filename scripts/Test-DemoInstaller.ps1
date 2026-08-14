[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [ValidateRange(3, 60)]
    [int]$LaunchSeconds = 15,

    [ValidateRange(1, 20)]
    [int]$LaunchIterations = 1,

    [ValidateRange(5, 60)]
    [int]$CloseTimeoutSeconds = 15,

    [string]$WorkingRoot = (Join-Path ([System.IO.Path]::GetTempPath()) 'Satilogica\SatiDemoInstallerAcceptance'),

    [string]$EvidencePath,

    [switch]$ExternalMachine,

    [switch]$KeepInstalledFiles
)

$ErrorActionPreference = 'Stop'
$installer = [System.IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Installer not found: $installer"
}

$installerName = [System.IO.Path]::GetFileName($installer)
if ($installerName -notmatch '^SatiDemoSetup-(\d+\.\d+\.\d+)\.exe$') {
    throw "Installer name must match SatiDemoSetup-x.y.z.exe: $installerName"
}
$expectedVersion = $Matches[1]

$acceptanceRoot = [System.IO.Path]::GetFullPath($WorkingRoot)
if ($acceptanceRoot -eq [System.IO.Path]::GetPathRoot($acceptanceRoot)) {
    throw 'WorkingRoot cannot be a drive root.'
}
$runRoot = Join-Path $acceptanceRoot ('run-' + [Guid]::NewGuid().ToString('N'))
$priorTestMode = [Environment]::GetEnvironmentVariable('SATI_DEMO_INSTALLER_TEST')
$priorInstallRoot = [Environment]::GetEnvironmentVariable('SATI_DEMO_INSTALL_ROOT')
$app = $null

try {
    if (@(Get-Process -Name 'Sati.Demo' -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'Close Sati Demo before running installer acceptance.'
    }

    [System.IO.Directory]::CreateDirectory($runRoot) | Out-Null
    $env:SATI_DEMO_INSTALLER_TEST = '1'
    $env:SATI_DEMO_INSTALL_ROOT = $runRoot
    $install = Start-Process `
        -FilePath $installer `
        -ArgumentList '/Q' `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($install.ExitCode -ne 0) {
        throw "Installer exited with code $($install.ExitCode)."
    }

    $requiredFiles = @('Sati.Demo.exe', 'appsettings.Public.json', 'Uninstall-SatiDemo.ps1')
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $runRoot $requiredFile) -PathType Leaf)) {
            throw "Installed payload is missing '$requiredFile'."
        }
    }
    $versionedIcon = Join-Path $runRoot "Sati.Demo.$expectedVersion.ico"
    if (-not (Test-Path -LiteralPath $versionedIcon -PathType Leaf)) {
        throw "Installed payload is missing the versioned icon 'Sati.Demo.$expectedVersion.ico'."
    }
    if (Test-Path -LiteralPath (Join-Path $runRoot 'appsettings.json')) {
        throw 'The private appsettings.json file was installed.'
    }

    $publicConfiguration = Get-Content -LiteralPath (Join-Path $runRoot 'appsettings.Public.json') -Raw
    if ($publicConfiguration -match 'ConnectionStrings|Password=|User ID=') {
        throw 'The installed public configuration contains a private connection setting.'
    }

    $appPath = Join-Path $runRoot 'Sati.Demo.exe'
    $actualVersion = (Get-Item -LiteralPath $appPath).VersionInfo.FileVersion
    if (-not $actualVersion.StartsWith("$expectedVersion.", [StringComparison]::Ordinal)) {
        throw "Installed version '$actualVersion' does not match installer version '$expectedVersion'."
    }

    for ($iteration = 1; $iteration -le $LaunchIterations; $iteration++) {
        $app = Start-Process -FilePath $appPath -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru
        $deadline = (Get-Date).AddSeconds($LaunchSeconds)
        do {
            Start-Sleep -Milliseconds 500
            $app.Refresh()
            if ($app.HasExited) {
                throw "Installed app exited during startup iteration $iteration with code $($app.ExitCode)."
            }
        } while ((Get-Date) -lt $deadline)

        if (-not $app.Responding) {
            throw "Installed app stopped responding during launch iteration $iteration."
        }
        if (-not $app.CloseMainWindow()) {
            throw "Installed app did not accept the normal window-close request during iteration $iteration."
        }
        if (-not $app.WaitForExit($CloseTimeoutSeconds * 1000)) {
            throw "Installed app did not exit within $CloseTimeoutSeconds seconds of the normal window-close request during iteration $iteration."
        }
        $app.Refresh()
        if ($app.ExitCode -ne 0) {
            throw "Installed app exited with code $($app.ExitCode) during graceful-close iteration $iteration."
        }

        Write-Output "APP_WINDOW_LIFECYCLE_PASSED iteration=$iteration responding=True gracefulClose=True exitCode=0"
        $app = $null
    }

    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Output "INSTALLER_ACCEPTANCE_PASSED installer=$installerName sha256=$hash"
    Write-Output "INSTALLED_APP_ACCEPTANCE_PASSED version=$actualVersion responding=True launchSeconds=$LaunchSeconds iterations=$LaunchIterations gracefulClose=True"

    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $resolvedEvidence = [System.IO.Path]::GetFullPath($EvidencePath)
        $evidenceParent = [System.IO.Path]::GetDirectoryName($resolvedEvidence)
        if ([string]::IsNullOrWhiteSpace($evidenceParent)) {
            throw 'EvidencePath must include a parent directory.'
        }
        [System.IO.Directory]::CreateDirectory($evidenceParent) | Out-Null
        if (Test-Path -LiteralPath $resolvedEvidence) {
            throw "Refusing to overwrite installer evidence: $resolvedEvidence"
        }

        $sourceTreeDetected = Test-Path -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) '.git')
        [ordered]@{
            SchemaVersion = 1
            Gate = 'CleanMachineLaunch'
            Passed = $true
            CapturedUtc = [DateTime]::UtcNow.ToString('O')
            InstallerFileName = $installerName
            InstallerSha256 = $hash
            InstalledVersion = $actualVersion
            AppResponding = $true
            LaunchSeconds = $LaunchSeconds
            LaunchIterations = $LaunchIterations
            GracefulClosePassed = $true
            MachineName = [Environment]::MachineName
            OsVersion = [Environment]::OSVersion.VersionString
            ExternalMachineConfirmed = [bool]$ExternalMachine
            SourceTreeDetected = [bool]$sourceTreeDetected
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resolvedEvidence -Encoding UTF8
        Write-Output "INSTALLER_EVIDENCE_WRITTEN path=$resolvedEvidence"
        if (-not $ExternalMachine) {
            Write-Warning 'Evidence records a successful launch, but it is not a clean external-machine attestation.'
        }
    }
}
finally {
    if ($null -ne $app -and -not $app.HasExited) {
        Stop-Process -Id $app.Id -ErrorAction Stop
        [void]$app.WaitForExit(10000)
    }

    if ($null -eq $priorTestMode) {
        Remove-Item Env:SATI_DEMO_INSTALLER_TEST -ErrorAction SilentlyContinue
    }
    else {
        $env:SATI_DEMO_INSTALLER_TEST = $priorTestMode
    }
    if ($null -eq $priorInstallRoot) {
        Remove-Item Env:SATI_DEMO_INSTALL_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:SATI_DEMO_INSTALL_ROOT = $priorInstallRoot
    }

    if (-not $KeepInstalledFiles -and (Test-Path -LiteralPath $runRoot)) {
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
        Write-Output 'INSTALLER_ACCEPTANCE_CLEANUP_PASSED'
    }
}
