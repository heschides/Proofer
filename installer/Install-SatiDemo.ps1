param()

$ErrorActionPreference = 'Stop'
$installerProgress = $null

function Show-InstallerMessage {
    param(
        [string]$Message,
        [string]$Title,
        [System.Windows.MessageBoxImage]$Icon
    )

    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show(
        $Message,
        $Title,
        [System.Windows.MessageBoxButton]::OK,
        $Icon) | Out-Null
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
    $version = (Get-Content -LiteralPath $versionPath -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw 'The installer version is invalid.'
    }

    $isTest = $env:SATI_DEMO_INSTALLER_TEST -eq '1'
    if (-not $isTest) {
        $installerProgress = Start-SatiInstallerProgress `
            -Title 'Sati Demo Setup' `
            -Heading 'Installing Sati Demo' `
            -Detail 'Checking the installation package...'
    }
    if ($isTest) {
        if ([string]::IsNullOrWhiteSpace($env:SATI_DEMO_INSTALL_ROOT)) {
            throw 'SATI_DEMO_INSTALL_ROOT is required in installer test mode.'
        }
        $installRoot = [System.IO.Path]::GetFullPath($env:SATI_DEMO_INSTALL_ROOT)
    }
    else {
        $localAppData = [Environment]::GetFolderPath('LocalApplicationData')
        if ([string]::IsNullOrWhiteSpace($localAppData)) {
            throw 'The Windows local application-data folder could not be resolved.'
        }

        $programsRoot = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'Programs'))
        $installRoot = [System.IO.Path]::GetFullPath(
            (Join-Path $programsRoot 'SatiLogica\Sati Demo'))
        if (-not $installRoot.StartsWith($programsRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The installer resolved an unsafe destination path.'
        }
    }

    $running = @(Get-Process -Name 'Sati.Demo' -ErrorAction SilentlyContinue)
    if ($running.Count -ne 0) {
        $messageParameters = @{
            Message = 'Close Sati Demo before installing this update, then run the installer again.'
            Title = 'Sati Demo is running'
            Icon = [System.Windows.MessageBoxImage]::Warning
        }
        Show-InstallerMessage @messageParameters
        exit 2
    }

    Update-SatiInstallerProgress $installerProgress `
        -Heading 'Installing Sati Demo' `
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

    $appExe = Join-Path $installRoot 'Sati.Demo.exe'
    if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
        throw 'Sati.Demo.exe was not installed.'
    }
    $versionedIcon = Join-Path $installRoot "Sati.Demo.$version.ico"
    if (-not (Test-Path -LiteralPath $versionedIcon -PathType Leaf)) {
        throw 'The versioned Sati Demo icon was not installed.'
    }

    if (-not $isTest) {
        Update-SatiInstallerProgress $installerProgress `
            -Heading 'Almost ready' `
            -Detail 'Creating shortcuts and finishing the installation...'
        $shell = New-Object -ComObject WScript.Shell
        $startMenuFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'SatiLogica'
        New-Item -ItemType Directory -Path $startMenuFolder -Force | Out-Null

        $startMenuShortcut = $shell.CreateShortcut((Join-Path $startMenuFolder 'Sati Demo.lnk'))
        $startMenuShortcut.TargetPath = $appExe
        $startMenuShortcut.WorkingDirectory = $installRoot
        $startMenuShortcut.IconLocation = "$versionedIcon,0"
        $startMenuShortcut.Description = 'Sati Azure demonstration client'
        $startMenuShortcut.Save()

        $desktopShortcut = $shell.CreateShortcut(
            (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Sati Demo.lnk'))
        $desktopShortcut.TargetPath = $appExe
        $desktopShortcut.WorkingDirectory = $installRoot
        $desktopShortcut.IconLocation = "$versionedIcon,0"
        $desktopShortcut.Description = 'Sati Azure demonstration client'
        $desktopShortcut.Save()

        $taskbarFolder = Join-Path (
            [Environment]::GetFolderPath('ApplicationData')) (
            'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar')
        if (Test-Path -LiteralPath $taskbarFolder -PathType Container) {
            foreach ($pinnedFile in Get-ChildItem -LiteralPath $taskbarFolder -Filter '*.lnk' -File) {
                $pinnedShortcut = $shell.CreateShortcut($pinnedFile.FullName)
                $pinnedTargetName = [System.IO.Path]::GetFileName($pinnedShortcut.TargetPath)
                $isCurrentTarget = $pinnedShortcut.TargetPath -eq $appExe
                $isRecognizedSatiPin = $pinnedFile.BaseName -in @('Sati', 'Sati Demo') -and
                    $pinnedTargetName -in @('Sati.exe', 'Sati.Demo.exe')
                if ($isCurrentTarget -or $isRecognizedSatiPin) {
                    $pinnedShortcut.TargetPath = $appExe
                    $pinnedShortcut.WorkingDirectory = $installRoot
                    $pinnedShortcut.IconLocation = "$versionedIcon,0"
                    $pinnedShortcut.Description = 'Sati Azure demonstration client'
                    $pinnedShortcut.Save()
                }
            }
        }

        $uninstaller = Join-Path $installRoot 'Uninstall-SatiDemo.ps1'
        $hiddenLauncher = Join-Path $installRoot 'Run-PowerShellHidden.vbs'
        $windowsScriptHost = Join-Path $env:SystemRoot 'System32\wscript.exe'
        $uninstallCommand = "`"$windowsScriptHost`" //B `"$hiddenLauncher`" Uninstall-SatiDemo.ps1"
        $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SatiDemo'
        New-Item -Path $uninstallKey -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'Sati Demo' -PropertyType String -Force | Out-Null
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
    $messageParameters = @{
        Message = "Sati Demo could not be installed.`n`n$($_.Exception.Message)"
        Title = 'Sati Demo installation failed'
        Icon = [System.Windows.MessageBoxImage]::Error
    }
    Show-InstallerMessage @messageParameters
    exit 1
}
finally {
    if ($null -ne $installerProgress) {
        Stop-SatiInstallerProgress $installerProgress
    }
}
