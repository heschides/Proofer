param()

$ErrorActionPreference = 'Stop'

function Show-UninstallerMessage {
    param([string]$Message, [System.Windows.MessageBoxImage]$Icon)

    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show(
        $Message,
        'Sati Demo',
        [System.Windows.MessageBoxButton]::OK,
        $Icon) | Out-Null
}

try {
    $localAppData = [Environment]::GetFolderPath('LocalApplicationData')
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        throw 'The Windows local application-data folder could not be resolved.'
    }

    $programsRoot = [System.IO.Path]::GetFullPath((Join-Path $localAppData 'Programs'))
    $expectedInstallRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $programsRoot 'SatiLogica\Sati Demo'))
    $installRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
    if ($installRoot -ne $expectedInstallRoot -or
        -not $installRoot.StartsWith($programsRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The Sati Demo installation folder could not be validated.'
    }

    $running = @(Get-Process -Name 'Sati.Demo' -ErrorAction SilentlyContinue)
    if ($running.Count -ne 0) {
        $messageParameters = @{
            Message = 'Close Sati Demo before uninstalling it, then try again.'
            Icon = [System.Windows.MessageBoxImage]::Warning
        }
        Show-UninstallerMessage @messageParameters
        exit 2
    }

    $startMenuShortcut = Join-Path (
        (Join-Path ([Environment]::GetFolderPath('Programs')) 'SatiLogica')) 'Sati Demo.lnk'
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Sati Demo.lnk'
    if (Test-Path -LiteralPath $startMenuShortcut) {
        Remove-Item -LiteralPath $startMenuShortcut -Force
    }
    if (Test-Path -LiteralPath $desktopShortcut) {
        Remove-Item -LiteralPath $desktopShortcut -Force
    }

    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SatiDemo'
    if (Test-Path -LiteralPath $uninstallKey) {
        Remove-Item -LiteralPath $uninstallKey -Recurse -Force
    }

    $messageParameters = @{
        Message = 'Sati Demo has been uninstalled.'
        Icon = [System.Windows.MessageBoxImage]::Information
    }
    Show-UninstallerMessage @messageParameters

    $escapedInstallRoot = $installRoot.Replace("'", "''")
    $cleanup = "Start-Sleep -Seconds 2; Remove-Item -LiteralPath '$escapedInstallRoot' -Recurse -Force"
    $encodedCleanup = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cleanup))
    $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    Start-Process -FilePath $windowsPowerShell -WindowStyle Hidden -ArgumentList @(
        '-NoProfile',
        '-EncodedCommand',
        $encodedCleanup)
}
catch {
    $messageParameters = @{
        Message = "Sati Demo could not be uninstalled.`n`n$($_.Exception.Message)"
        Icon = [System.Windows.MessageBoxImage]::Error
    }
    Show-UninstallerMessage @messageParameters
    exit 1
}
