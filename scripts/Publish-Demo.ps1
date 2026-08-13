param(
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\demo-win-x64"
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)

if (Test-Path -LiteralPath $resolvedOutput) {
    $existing = @(Get-ChildItem -LiteralPath $resolvedOutput -Force)
    if ($existing.Count -gt 0) {
        throw "The output directory must not already contain files: $resolvedOutput"
    }
}
else {
    [void](New-Item -ItemType Directory -Path $resolvedOutput)
}

dotnet publish (Join-Path $repositoryRoot "Sati.csproj") `
    --configuration Demo `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $resolvedOutput `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) {
    throw "The Demo publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $resolvedOutput "Sati.Demo.exe"
$publicSettings = Join-Path $resolvedOutput "appsettings.Public.json"
$privateSettings = Join-Path $resolvedOutput "appsettings.json"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "The Demo executable was not produced."
}
if (-not (Test-Path -LiteralPath $publicSettings -PathType Leaf)) {
    throw "The public Demo endpoint configuration was not produced."
}
if (Test-Path -LiteralPath $privateSettings) {
    throw "A private appsettings.json file was found in the Demo artifact. Remove it before distribution."
}

$settings = Get-Content -LiteralPath $publicSettings -Raw | ConvertFrom-Json
$baseAddress = [Uri]$settings.ApiEnvironments.Demo.BaseAddress
if (-not $baseAddress.IsAbsoluteUri -or $baseAddress.Scheme -cne "https") {
    throw "The packaged Demo endpoint is not absolute HTTPS."
}

$hash = Get-FileHash -LiteralPath $executable -Algorithm SHA256
[pscustomobject]@{
    Artifact = $executable
    Bytes = (Get-Item -LiteralPath $executable).Length
    SHA256 = $hash.Hash
    ApiBaseAddress = $baseAddress.AbsoluteUri
    PrivateSettingsPresent = $false
} | Format-List
