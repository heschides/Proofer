<#
.SYNOPSIS
    Builds the Sati workflow promotional brochure PDF from its HTML source.

.DESCRIPTION
    marketing/brochure/brochure.html is the source of truth. Each slide is one 960x540pt
    <svg> in the PDF's own coordinate system. This script prints it to PDF with headless
    Edge (Chromium), which embeds font subsets and keeps the text selectable and vector.

    The output lands in output/pdf/, which is gitignored - the PDF is a build artifact.

.PARAMETER Output
    Where to write the PDF. Defaults to output/pdf/Sati_Workflow_Promotional_Brochure.pdf.

.PARAMETER Publish
    Also copy the result to the OneDrive Marketing folder that the sales copies live in.

.PARAMETER Preview
    Also write output/brochure-preview.html: the same deck with every screenshot inlined as a
    data URI. Viewers that load the page from a data URL or a sandbox cannot resolve the
    relative assets/ paths, so the slides come up with no images. The preview file is
    self-contained and shows correctly anywhere. It is generated, never edited.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/build-brochure.ps1
    powershell -ExecutionPolicy Bypass -File scripts/build-brochure.ps1 -Publish
#>
[CmdletBinding()]
param(
    [string] $Output,
    [switch] $Publish,
    [switch] $Preview
)

$ErrorActionPreference = 'Stop'

$repo   = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'marketing/brochure/brochure.html'

if (-not (Test-Path $source)) { throw "Brochure source not found: $source" }

if (-not $Output) { $Output = Join-Path $repo 'output/pdf/Sati_Workflow_Promotional_Brochure.pdf' }
$outDir = Split-Path -Parent $Output
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }

# Chromium is the renderer. Edge ships with Windows; Chrome is used if it happens to be there.
$candidates = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe"
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe"
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
)
$browser = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $browser) { throw "No Chromium browser found. Install Edge or Chrome, or pass a path." }

# Headless Chromium will not print from a working directory it does not own, and it needs a
# throwaway profile so a running Edge window does not hand the job to the existing instance.
$profileDir = Join-Path ([System.IO.Path]::GetTempPath()) ("sati-brochure-" + [guid]::NewGuid().ToString('N'))

$uri = ([uri]("file:///" + $source.Replace('\', '/'))).AbsoluteUri

$browserArgs = @(
    '--headless=new'
    '--disable-gpu'
    '--no-first-run'
    '--no-default-browser-check'
    "--user-data-dir=$profileDir"
    '--no-pdf-header-footer'
    '--run-all-compositor-stages-before-draw'
    '--virtual-time-budget=15000'
    "--print-to-pdf=$Output"
    $uri
)

Write-Host "Rendering  $source"
Write-Host "  with     $(Split-Path -Leaf $browser)"

# Edge does not set a reliable exit code when it hands the job to its own process, so success is
# judged by a freshly written file rather than by $LASTEXITCODE.
if (Test-Path $Output) { Remove-Item $Output -Force }

# Chromium chatters on stderr even on a clean run. Under Windows PowerShell that chatter becomes
# a terminating NativeCommandError, so stderr is dropped and errors are judged by the output file.
$previousPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & $browser @browserArgs 2>$null | Out-Null
}
finally {
    $ErrorActionPreference = $previousPreference
    if (Test-Path $profileDir) { Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue }
}

if (-not (Test-Path $Output)) { throw "Render produced no file at $Output" }

$size = (Get-Item $Output).Length
Write-Host ("  wrote    {0} ({1:N0} bytes)" -f $Output, $size)

if ($Preview) {
    $previewPath = Join-Path $outDir 'brochure-preview.html'
    $html = [System.IO.File]::ReadAllText($source)
    $assetDir = Join-Path (Split-Path -Parent $source) 'assets'
    $inlined = 0

    foreach ($asset in Get-ChildItem $assetDir -File) {
        $needle = 'href="assets/{0}"' -f $asset.Name
        if (-not $html.Contains($needle)) { continue }
        $mime = switch ($asset.Extension.ToLowerInvariant()) {
            '.png'  { 'image/png' }
            '.jpg'  { 'image/jpeg' }
            '.jpeg' { 'image/jpeg' }
            default { $null }
        }
        if (-not $mime) { continue }
        $b64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($asset.FullName))
        $html = $html.Replace($needle, ('href="data:{0};base64,{1}"' -f $mime, $b64))
        $inlined++
    }

    [System.IO.File]::WriteAllText($previewPath, $html, (New-Object System.Text.UTF8Encoding($false)))
    # 'assets/...' is the literal example inside the placeholder comment, not a real reference.
    $stillRelative = ([regex]::Matches($html, 'href="assets/(?!\.\.\.")')).Count
    if ($stillRelative -gt 0) { Write-Warning "$stillRelative asset reference(s) could not be inlined" }
    Write-Host ("  preview  {0} ({1:N0} bytes, {2} images inlined)" -f $previewPath, (Get-Item $previewPath).Length, $inlined)
}

if ($Publish) {
    $marketing = Join-Path $env:USERPROFILE 'RobinBradleyAMS\SatiLogica - Documents\Marketing'
    if (-not (Test-Path $marketing)) { throw "Marketing folder not found: $marketing" }
    Copy-Item $Output (Join-Path $marketing (Split-Path -Leaf $Output)) -Force
    Write-Host "  published to $marketing"
}
