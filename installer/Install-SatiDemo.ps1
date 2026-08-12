param()

$ErrorActionPreference = 'Stop'

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
    $manifestPath = Join-Path $sourceRoot 'payload-manifest.txt'
    $versionPath = Join-Path $sourceRoot 'installer-version.txt'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $versionPath -PathType Leaf)) {
        throw 'The installer payload is incomplete.'
    }

    $isTest = $env:SATI_DEMO_INSTALLER_TEST -eq '1'
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
            (Join-Path $programsRoot 'Satilogica\Sati Demo'))
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

    if (-not $isTest) {
        $shell = New-Object -ComObject WScript.Shell
        $startMenuFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'Satilogica'
        New-Item -ItemType Directory -Path $startMenuFolder -Force | Out-Null

        $startMenuShortcut = $shell.CreateShortcut((Join-Path $startMenuFolder 'Sati Demo.lnk'))
        $startMenuShortcut.TargetPath = $appExe
        $startMenuShortcut.WorkingDirectory = $installRoot
        $startMenuShortcut.IconLocation = "$appExe,0"
        $startMenuShortcut.Description = 'Sati Azure demonstration client'
        $startMenuShortcut.Save()

        $desktopShortcut = $shell.CreateShortcut(
            (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Sati Demo.lnk'))
        $desktopShortcut.TargetPath = $appExe
        $desktopShortcut.WorkingDirectory = $installRoot
        $desktopShortcut.IconLocation = "$appExe,0"
        $desktopShortcut.Description = 'Sati Azure demonstration client'
        $desktopShortcut.Save()

        $version = (Get-Content -LiteralPath $versionPath -Raw).Trim()
        $uninstaller = Join-Path $installRoot 'Uninstall-SatiDemo.ps1'
        $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $uninstallCommand = "`"$windowsPowerShell`" -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`""
        $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SatiDemo'
        New-Item -Path $uninstallKey -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'Sati Demo' -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $version -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name Publisher -Value 'Satilogica' -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installRoot -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value "$appExe,0" -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name UninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

        Start-Process -FilePath $appExe -WorkingDirectory $installRoot
    }
}
catch {
    $messageParameters = @{
        Message = "Sati Demo could not be installed.`n`n$($_.Exception.Message)"
        Title = 'Sati Demo installation failed'
        Icon = [System.Windows.MessageBoxImage]::Error
    }
    Show-InstallerMessage @messageParameters
    exit 1
}
