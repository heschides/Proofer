param()

$ErrorActionPreference = 'Stop'

try {
    $msi = Join-Path $PSScriptRoot 'SqlLocalDB.msi'
    if (-not (Test-Path -LiteralPath $msi -PathType Leaf)) {
        throw 'The diagnostic package is missing SqlLocalDB.msi.'
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $desktop = [Environment]::GetFolderPath('Desktop')
    $log = Join-Path $desktop "Sati-LocalDB-$stamp.log"
    $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') `
        -ArgumentList @('/i', ('"' + $msi + '"'), '/L*v', ('"' + $log + '"'), '/norestart') `
        -Verb RunAs -Wait -PassThru

    $summary = Join-Path $desktop "Sati-LocalDB-$stamp-summary.txt"
    @(
        "LocalDB diagnostic run: $(Get-Date -Format o)",
        "MSI exit code: $($process.ExitCode)",
        "Verbose MSI log: $log",
        'Send both files to Sati support. Do not include consumer records.'
    ) | Set-Content -LiteralPath $summary -Encoding UTF8

    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show(
        "LocalDB returned exit code $($process.ExitCode).`n`nVerbose log: $log`nSummary: $summary",
        'Sati LocalDB diagnostic', 'OK', 'Information') | Out-Null
    exit $process.ExitCode
}
catch {
    $path = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Sati-LocalDB-diagnostic-error.txt'
    $_.Exception.ToString() | Set-Content -LiteralPath $path -Encoding UTF8
    throw
}
