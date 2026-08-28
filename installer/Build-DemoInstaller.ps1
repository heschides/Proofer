param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.2.30'
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$workRoot = Join-Path $repoRoot ('.installer-build-' + [Guid]::NewGuid().ToString('N'))
$publishRoot = Join-Path $workRoot 'publish'
$stageRoot = Join-Path $workRoot 'stage'
$artifactRoot = Join-Path $repoRoot 'artifacts\SatiDemoInstaller'
$installerPath = Join-Path $artifactRoot "SatiDemoSetup-$Version.exe"
$transientArtifactPattern = "~SatiDemoSetup-$Version.*"

try {
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
        '--configuration', 'Demo',
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
        throw 'The Sati Demo publish failed.'
    }

    $versionedIconName = "Sati.Demo.$Version.ico"
    Copy-Item `
        -LiteralPath (Join-Path $repoRoot 'images\sati.ico') `
        -Destination (Join-Path $publishRoot $versionedIconName) `
        -Force

    $requiredFiles = @('Sati.Demo.exe', 'appsettings.Public.json', $versionedIconName)
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $requiredFile) -PathType Leaf)) {
            throw "The publish output is missing '$requiredFile'."
        }
    }
    if (Test-Path -LiteralPath (Join-Path $publishRoot 'appsettings.json')) {
        throw 'The Demo publish unexpectedly contains the private/local appsettings.json file.'
    }

    $configurationFiles = @(Get-ChildItem -LiteralPath $publishRoot -File | Where-Object {
        $_.Extension -in @('.json', '.config', '.xml')
    })
    foreach ($configurationFile in $configurationFiles) {
        if ((Get-Content -LiteralPath $configurationFile.FullName -Raw) -match 'ConnectionStrings|Password=|User ID=') {
            throw "The Demo publish contains a database credential or connection string in '$($configurationFile.Name)'."
        }
    }

    Copy-Item -Path (Join-Path $publishRoot '*') -Destination $stageRoot -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-SatiDemo.ps1') -Destination $stageRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall-SatiDemo.ps1') -Destination $stageRoot

    $payloadFiles = @(Get-ChildItem -LiteralPath $publishRoot -File | Sort-Object Name | Select-Object -ExpandProperty Name)
    $payloadFiles += 'Uninstall-SatiDemo.ps1'
    Set-Content -LiteralPath (Join-Path $stageRoot 'payload-manifest.txt') -Value $payloadFiles -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $stageRoot 'installer-version.txt') -Value $Version -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $stageRoot 'Install-SatiDemo.cmd') -Encoding ASCII -Value @(
        '@echo off',
        'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-SatiDemo.ps1"',
        'exit /b %errorlevel%')

    $stageFiles = @(Get-ChildItem -LiteralPath $stageRoot -File | Sort-Object Name)
    $stringLines = [System.Collections.Generic.List[string]]::new()
    $sourceLines = [System.Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $stageFiles.Count; $index++) {
        $stringLines.Add("FILE$index=`"$($stageFiles[$index].Name)`"")
        $sourceLines.Add("%FILE$index%=")
    }

    $sedPath = Join-Path $workRoot 'SatiDemoInstaller.sed'
    $sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=Install-SatiDemo.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=Install-SatiDemo.cmd
UserQuietInstCmd=Install-SatiDemo.cmd
SourceFiles=SourceFiles
[Strings]
InstallPrompt=Install Sati Demo $Version for this Windows account?
FinishMessage=Sati Demo was installed successfully.
TargetName=$installerPath
FriendlyName=Sati Demo Installer
$($stringLines -join "`r`n")
[SourceFiles]
SourceFiles0=$stageRoot\
[SourceFiles0]
$($sourceLines -join "`r`n")
"@
    Set-Content -LiteralPath $sedPath -Value $sed -Encoding ASCII

    $iexpressParameters = @{
        FilePath = Join-Path $env:SystemRoot 'System32\iexpress.exe'
        ArgumentList = @('/N', '/Q', $sedPath)
        WindowStyle = 'Hidden'
        PassThru = $true
    }
    $iexpressProcess = Start-Process @iexpressParameters
    $packagingStartedAt = $iexpressProcess.StartTime.AddSeconds(-5)
    $packagingComplete = $false
    $deadline = (Get-Date).AddMinutes(8)
    do {
        $iexpressProcess.Refresh()
        $packagingHelpers = @(Get-Process -Name 'makecab', 'wextract' -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime -ge $packagingStartedAt })
        if ($iexpressProcess.HasExited -and $packagingHelpers.Count -eq 0) {
            $packagingComplete = $true
            break
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    if (-not $packagingComplete) {
        throw 'IExpress did not finish creating the Sati Demo installer before the packaging deadline.'
    }
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw 'IExpress did not create the Sati Demo installer.'
    }

    $lastLength = -1L
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $currentLength = (Get-Item -LiteralPath $installerPath).Length
        if ($currentLength -gt 0 -and $currentLength -eq $lastLength) {
            break
        }
        $lastLength = $currentLength
        Start-Sleep -Milliseconds 500
    }
    Get-ChildItem -LiteralPath $artifactRoot -Filter $transientArtifactPattern -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    $installer = Get-Item -LiteralPath $installerPath
    $stageLength = ($stageFiles | Measure-Object -Property Length -Sum).Sum
    $minimumInstallerLength = [Math]::Max(1MB, [long]($stageLength * 0.1))
    if ($installer.Length -lt $minimumInstallerLength) {
        throw "IExpress created an incomplete installer ($($installer.Length) bytes; expected at least $minimumInstallerLength bytes)."
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
            -not [System.IO.Path]::GetFileName($resolvedWork).StartsWith('.installer-build-')) {
            throw "Refusing to clean unexpected installer work directory: $resolvedWork"
        }
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force
    }
}
