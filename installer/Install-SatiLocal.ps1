param()

$ErrorActionPreference = 'Stop'
$installerProgress = $null

function Show-InstallerMessage {
    param(
        [string]$Message,
        [string]$Title,
        [int]$Icon
    )

    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show(
        $Message,
        $Title,
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]$Icon) | Out-Null
}

try {
    $sourceRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
    $progressScript = Join-Path $sourceRoot 'InstallerProgress.ps1'
    if (-not (Test-Path -LiteralPath $progressScript -PathType Leaf)) {
        throw 'The installer is missing its progress-window support.'
    }
    . $progressScript

    $manifestPath = Join-Path $sourceRoot 'payload-manifest.txt'
    $versionPath = Join-Path $sourceRoot 'installer-version.txt'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $versionPath -PathType Leaf)) {
        throw 'The installer payload is incomplete.'
    }

    $isTest = $env:SATI_LOCAL_INSTALLER_TEST -eq '1'
    if (-not $isTest) {
        $installerProgress = Start-SatiInstallerProgress `
            -Title 'Sati Setup' `
            -Heading 'Installing Sati' `
            -Detail 'Checking your Windows and LocalDB setup...'
    }

    $localDbMsi = Join-Path $sourceRoot 'SqlLocalDB.msi'
    if (-not (Test-Path -LiteralPath $localDbMsi -PathType Leaf)) {
        throw 'The installer is missing the LocalDB prerequisite.'
    }

    $localDbCommand = Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue
    if (-not $isTest -and $null -eq $localDbCommand) {
        Update-SatiInstallerProgress $installerProgress `
            -Heading 'Preparing LocalDB' `
            -Detail 'Windows may ask permission to install the Microsoft database prerequisite.'
        $msi = Start-Process `
            -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') `
            -ArgumentList @('/i', ('"' + $localDbMsi + '"'), '/qn', '/norestart') `
            -Verb RunAs `
            -Wait `
            -PassThru
        if ($msi.ExitCode -notin @(0, 1641, 3010)) {
            throw "Microsoft SQL Server LocalDB installation failed with exit code $($msi.ExitCode)."
        }
        $localDbCommand = Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue
        if ($null -eq $localDbCommand) {
            $candidate = Get-ChildItem 'C:\Program Files\Microsoft SQL Server' `
                -Filter SqlLocalDB.exe -Recurse -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending |
                Select-Object -First 1
            if ($null -ne $candidate) { $localDbCommand = Get-Command $candidate.FullName }
        }
    }

    if (-not $isTest -and $null -eq $localDbCommand) {
        throw 'LocalDB was installed but SqlLocalDB.exe could not be located.'
    }

    if (-not $isTest) {
        Update-SatiInstallerProgress $installerProgress `
            -Heading 'Preparing your workspace' `
            -Detail 'Starting the local database for Sati...'
        & $localDbCommand.Source info MSSQLLocalDB *> $null
        if ($LASTEXITCODE -ne 0) {
            & $localDbCommand.Source create MSSQLLocalDB *> $null
            if ($LASTEXITCODE -ne 0) { throw 'The MSSQLLocalDB instance could not be created.' }
        }
        & $localDbCommand.Source start MSSQLLocalDB *> $null
        if ($LASTEXITCODE -ne 0) { throw 'The MSSQLLocalDB instance could not be started.' }
    }

    $version = (Get-Content -LiteralPath $versionPath -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw 'The installer version is invalid.'
    }

    if ($isTest) {
        if ([string]::IsNullOrWhiteSpace($env:SATI_LOCAL_INSTALL_ROOT)) {
            throw 'SATI_LOCAL_INSTALL_ROOT is required in installer test mode.'
        }
        $installRoot = [System.IO.Path]::GetFullPath($env:SATI_LOCAL_INSTALL_ROOT)
    }
    else {
        $localAppData = [Environment]::GetFolderPath('LocalApplicationData')
        if ([string]::IsNullOrWhiteSpace($localAppData)) {
            throw 'The Windows local application-data folder could not be resolved.'
        }

        $programsRoot = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'Programs'))
        $installRoot = [System.IO.Path]::GetFullPath((Join-Path $programsRoot 'SatiLogica\Sati'))
        if (-not $installRoot.StartsWith($programsRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The installer resolved an unsafe destination path.'
        }
    }

    if (-not $isTest -and @(Get-Process -Name 'Sati' -ErrorAction SilentlyContinue).Count -ne 0) {
        Show-InstallerMessage `
            -Message 'Close Sati before installing this update, then run the installer again.' `
            -Title 'Sati is running' `
            -Icon 48
        exit 2
    }

    Update-SatiInstallerProgress $installerProgress `
        -Heading 'Installing Sati' `
        -Detail 'Copying the application to your Windows account...'
    New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
    $payloadFiles = @(Get-Content -LiteralPath $manifestPath | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_)
    })
    if ($payloadFiles.Count -eq 0) {
        throw 'The installer payload manifest is empty.'
    }

    foreach ($fileName in $payloadFiles) {
        if ([System.IO.Path]::IsPathRooted($fileName) -or
            [System.IO.Path]::GetFileName($fileName) -ne $fileName -or
            $fileName.Contains('..')) {
            throw "The installer payload contains an unsafe file name: $fileName"
        }

        $source = Join-Path $sourceRoot $fileName
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "The installer payload is missing '$fileName'."
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $installRoot $fileName) -Force
    }

    $appExe = Join-Path $installRoot 'Sati.exe'
    $versionedIcon = Join-Path $installRoot "Sati.$version.ico"
    if (-not (Test-Path -LiteralPath $appExe -PathType Leaf) -or
        -not (Test-Path -LiteralPath $versionedIcon -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $installRoot 'appsettings.json') -PathType Leaf)) {
        throw 'The LocalDB application payload was not installed completely.'
    }

    if (-not $isTest) {
        Update-SatiInstallerProgress $installerProgress `
            -Heading 'Almost ready' `
            -Detail 'Creating shortcuts and finishing the installation...'
        $shell = New-Object -ComObject WScript.Shell
        $startMenuFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'SatiLogica'
        New-Item -ItemType Directory -Path $startMenuFolder -Force | Out-Null

        $startMenuShortcut = $shell.CreateShortcut((Join-Path $startMenuFolder 'Sati.lnk'))
        $startMenuShortcut.TargetPath = $appExe
        $startMenuShortcut.WorkingDirectory = $installRoot
        $startMenuShortcut.IconLocation = "$versionedIcon,0"
        $startMenuShortcut.Description = 'Sati LocalDB client'
        $startMenuShortcut.Save()

        $desktopShortcut = $shell.CreateShortcut(
            (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Sati.lnk'))
        $desktopShortcut.TargetPath = $appExe
        $desktopShortcut.WorkingDirectory = $installRoot
        $desktopShortcut.IconLocation = "$versionedIcon,0"
        $desktopShortcut.Description = 'Sati LocalDB client'
        $desktopShortcut.Save()

        $uninstaller = Join-Path $installRoot 'Uninstall-SatiLocal.ps1'
        $hiddenLauncher = Join-Path $installRoot 'Run-PowerShellHidden.vbs'
        $windowsScriptHost = Join-Path $env:SystemRoot 'System32\wscript.exe'
        $uninstallCommand = "`"$windowsScriptHost`" //B `"$hiddenLauncher`" Uninstall-SatiLocal.ps1"
        $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SatiLocal'
        New-Item -Path $uninstallKey -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'Sati (LocalDB)' -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $version -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name Publisher -Value 'SatiLogica' -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installRoot -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value "$versionedIcon,0" -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name UninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

        $iconRefresh = Join-Path $env:SystemRoot 'System32\ie4uinit.exe'
        if (Test-Path -LiteralPath $iconRefresh -PathType Leaf) {
            Start-Process -FilePath $iconRefresh -ArgumentList '-show' -WindowStyle Hidden -Wait
        }

        Stop-SatiInstallerProgress $installerProgress
        $installerProgress = $null
        Start-Process -FilePath $appExe -WorkingDirectory $installRoot
    }
}
catch {
    if ($null -ne $installerProgress) {
        Stop-SatiInstallerProgress $installerProgress
        $installerProgress = $null
    }
    if ($env:SATI_LOCAL_INSTALLER_TEST -eq '1' -and
        -not [string]::IsNullOrWhiteSpace($env:SATI_LOCAL_INSTALL_ROOT)) {
        New-Item -ItemType Directory -Path $env:SATI_LOCAL_INSTALL_ROOT -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $env:SATI_LOCAL_INSTALL_ROOT 'install-error.txt') `
            -Value $_.Exception.ToString() -Encoding UTF8
        exit 1
    }
    Show-InstallerMessage `
        -Message "Sati LocalDB could not be installed.`n`n$($_.Exception.Message)" `
        -Title 'Sati LocalDB installation failed' `
        -Icon 16
    exit 1
}
finally {
    if ($null -ne $installerProgress) {
        Stop-SatiInstallerProgress $installerProgress
    }
}
