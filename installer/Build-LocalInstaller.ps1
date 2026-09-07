param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.3.2',

    [Parameter(Mandatory)]
    [string]$LocalDbMsiPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$workRoot = Join-Path $repoRoot ('.installer-build-local-' + [Guid]::NewGuid().ToString('N'))
$publishRoot = Join-Path $workRoot 'publish'
$stageRoot = Join-Path $workRoot 'stage'
$artifactRoot = Join-Path $repoRoot 'artifacts\SatiLocalInstaller'
$installerPath = Join-Path $artifactRoot "SatiLocalSetup-$Version.exe"
$transientArtifactPattern = "~SatiLocalSetup-$Version.*"

try {
    $resolvedLocalDbMsi = [System.IO.Path]::GetFullPath($LocalDbMsiPath)
    if (-not (Test-Path -LiteralPath $resolvedLocalDbMsi -PathType Leaf) -or
        [System.IO.Path]::GetFileName($resolvedLocalDbMsi) -ne 'SqlLocalDB.msi') {
        throw 'LocalDbMsiPath must name Microsoft SqlLocalDB.msi.'
    }
    $localDbSignature = Get-AuthenticodeSignature -LiteralPath $resolvedLocalDbMsi
    if ($localDbSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $localDbSignature.SignerCertificate.Subject -notmatch 'Microsoft') {
        throw 'SqlLocalDB.msi does not have a valid Microsoft Authenticode signature.'
    }

    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $artifactRoot -Filter $transientArtifactPattern -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
    if (Test-Path -LiteralPath $installerPath) {
        throw "Installer already exists: $installerPath"
    }

    $publishArguments = @(
        'publish',
        (Join-Path $repoRoot 'Sati.csproj'),
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $publishRoot,
        '--no-restore',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-p:SatelliteResourceLanguages=en-US',
        "-p:Version=$Version")
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'The Sati LocalDB publish failed.'
    }

    $versionedIconName = "Sati.$Version.ico"
    Copy-Item `
        -LiteralPath (Join-Path $repoRoot 'images\sati.ico') `
        -Destination (Join-Path $publishRoot $versionedIconName) `
        -Force

    $requiredFiles = @('Sati.exe', 'appsettings.json', $versionedIconName)
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $requiredFile) -PathType Leaf)) {
            throw "The publish output is missing '$requiredFile'."
        }
    }

    # Local Production is allowed to carry its workstation connection mapping, but
    # never a reusable SQL credential. The shipped configuration must use the
    # signed-in Windows identity.
    $privateConfiguration = Get-Content -LiteralPath (
        Join-Path $publishRoot 'appsettings.json') -Raw
    if ($privateConfiguration -match '(?i)Password\s*=' -or
        $privateConfiguration -match '(?i)User ID\s*=') {
        throw 'The LocalDB publish contains a SQL username or password.'
    }
    if ($privateConfiguration -notmatch '(?i)(Trusted_Connection|Integrated Security)\s*=\s*true') {
        throw 'The LocalDB publish does not use Windows integrated security.'
    }

    Copy-Item -Path (Join-Path $publishRoot '*') -Destination $stageRoot -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-SatiLocal.ps1') -Destination $stageRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall-SatiLocal.ps1') -Destination $stageRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'InstallerProgress.ps1') -Destination $stageRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run-PowerShellHidden.vbs') -Destination $stageRoot
    Copy-Item -LiteralPath $resolvedLocalDbMsi -Destination (Join-Path $stageRoot 'SqlLocalDB.msi')

    $payloadFiles = @(Get-ChildItem -LiteralPath $publishRoot -File |
        Sort-Object Name |
        Select-Object -ExpandProperty Name)
    $payloadFiles += @('Uninstall-SatiLocal.ps1', 'Run-PowerShellHidden.vbs')
    Set-Content -LiteralPath (Join-Path $stageRoot 'payload-manifest.txt') -Value $payloadFiles -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $stageRoot 'installer-version.txt') -Value $Version -Encoding ASCII

    $stageFiles = @(Get-ChildItem -LiteralPath $stageRoot -File | Sort-Object Name)
    $stagedLocalDbMsi = Join-Path $stageRoot 'SqlLocalDB.msi'
    $payloadZip = Join-Path $workRoot 'SatiPayload.zip'
    $payloadFiles = @($stageFiles | Where-Object Name -ne 'SqlLocalDB.msi' | Select-Object -ExpandProperty FullName)
    Compress-Archive -LiteralPath $payloadFiles -DestinationPath $payloadZip -CompressionLevel Optimal

    $bootstrapOutput = Join-Path $workRoot 'bootstrap'
    $bootstrapArguments = @(
        'publish',
        (Join-Path $PSScriptRoot 'Sati.LocalBootstrap\Sati.LocalBootstrap.csproj'),
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $bootstrapOutput,
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:Version=$Version",
        "-p:PayloadZip=$payloadZip",
        "-p:LocalDbMsi=$stagedLocalDbMsi")
    & dotnet @bootstrapArguments
    if ($LASTEXITCODE -ne 0) { throw 'The combined Sati bootstrapper publish failed.' }
    $bootstrapExe = Join-Path $bootstrapOutput 'SatiLocalSetup.exe'
    if (-not (Test-Path -LiteralPath $bootstrapExe -PathType Leaf)) {
        throw 'The combined Sati bootstrapper executable was not produced.'
    }
    Copy-Item -LiteralPath $bootstrapExe -Destination $installerPath

    $installer = Get-Item -LiteralPath $installerPath
    $stageLength = ($stageFiles | Measure-Object -Property Length -Sum).Sum
    $minimumInstallerLength = [Math]::Max(1MB, [long]($stageLength * 0.1))
    if ($installer.Length -lt $minimumInstallerLength) {
        throw "The bootstrapper is incomplete ($($installer.Length) bytes; expected at least $minimumInstallerLength bytes)."
    }

    $hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
    $hashLine = "$($hash.Hash.ToLowerInvariant())  $($installer.Name)"
    Set-Content -LiteralPath (Join-Path $artifactRoot "$($installer.Name).sha256") -Value $hashLine -Encoding ASCII
    Write-Output ('INSTALLER=' + $installer.FullName)
    Write-Output ('BYTES=' + $installer.Length)
    Write-Output ('SHA256=' + $hash.Hash.ToLowerInvariant())
}
finally {
    if (Test-Path -LiteralPath $artifactRoot) {
        Get-ChildItem -LiteralPath $artifactRoot -Filter $transientArtifactPattern -File -ErrorAction SilentlyContinue |
            Remove-Item -Force
    }
    if (Test-Path -LiteralPath $workRoot) {
        $resolvedWork = [System.IO.Path]::GetFullPath($workRoot)
        if ([System.IO.Directory]::GetParent($resolvedWork).FullName -ne $repoRoot -or
            -not [System.IO.Path]::GetFileName($resolvedWork).StartsWith('.installer-build-local-')) {
            throw "Refusing to clean unexpected installer work directory: $resolvedWork"
        }
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force
    }
}
