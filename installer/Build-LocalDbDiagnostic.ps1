param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.2.31',
    [Parameter(Mandatory)]
    [string]$LocalDbMsiPath
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$work = Join-Path $root ('.localdb-diagnostic-' + [Guid]::NewGuid().ToString('N'))
$artifactRoot = Join-Path $root 'artifacts\SatiLocalDbDiagnostic'
$installer = Join-Path $artifactRoot "SatiLocalDbDiagnostic-$Version.exe"
try {
    $msi = [IO.Path]::GetFullPath($LocalDbMsiPath)
    if (-not (Test-Path -LiteralPath $msi -PathType Leaf)) { throw 'SqlLocalDB.msi was not found.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $msi
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
        throw 'SqlLocalDB.msi must have a valid Microsoft signature.'
    }
    if (Test-Path -LiteralPath $installer) { throw "Diagnostic installer already exists: $installer" }
    New-Item -ItemType Directory -Path $work,$artifactRoot -Force | Out-Null
    Copy-Item -LiteralPath $msi -Destination (Join-Path $work 'SqlLocalDB.msi')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-LocalDbDiagnostic.ps1') -Destination (Join-Path $work 'Install-SatiLocal.ps1')
    $zip = Join-Path $work 'SatiPayload.zip'
    Compress-Archive -LiteralPath (Join-Path $work 'Install-SatiLocal.ps1') -DestinationPath $zip
    $stagedMsi = Join-Path $work 'SqlLocalDB.msi'
    dotnet publish (Join-Path $PSScriptRoot 'Sati.LocalBootstrap\Sati.LocalBootstrap.csproj') -c Release -r win-x64 --self-contained true --output (Join-Path $work 'publish') -p:PublishSingleFile=true -p:Version=$Version -p:PayloadZip=$zip "-p:LocalDbMsi=$stagedMsi"
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic bootstrapper publish failed.' }
    Copy-Item -LiteralPath (Join-Path $work 'publish\SatiLocalSetup.exe') -Destination $installer
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$installer.sha256" -Value "$hash  $(Split-Path $installer -Leaf)" -Encoding ASCII
    Write-Output "INSTALLER=$installer"
    Write-Output "SHA256=$hash"
}
finally { if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force } }
