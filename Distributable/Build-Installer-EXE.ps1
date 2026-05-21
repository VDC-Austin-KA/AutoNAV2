################################################################################
# Build-Installer-EXE.ps1  (single-DLL convenience build, bundle format)
#
# Regenerates Distributable\AutoNAV-Installer.exe from a SINGLE AutoNAV.dll by
# replicating that DLL into every Installer\payload\AutoNAV.bundle\Contents\V##\
# subfolder before linking.  Use this when you only have one Navisworks
# installed and just want a functional installer for testing.
#
# For a CORRECT multi-version build (a separately-compiled DLL per Navisworks
# year), use Build-MultiVersion.ps1 instead.
#
# Requirements:
#   - Go installed and on PATH        https://go.dev/dl/
#
# Inputs:
#   - Distributable\AutoNAV.dll       (replicated into bundle\Contents\V24..V27\)
#   - Distributable\AutoNAV.addin
#
# Output:
#   - Distributable\AutoNAV-Installer.exe   (~2 MB, single file; distribute as-is)
################################################################################

[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command 'go' -ErrorAction SilentlyContinue)) {
    throw "Go is required to build the .exe.  Install from https://go.dev/dl/ or use Build-Installer.ps1 to produce AutoNAV-Installer.cmd instead."
}

$installerDir = Join-Path $RepoRoot 'Installer'
$bundleRoot   = Join-Path $installerDir 'payload\AutoNAV.bundle'
$contentsRoot = Join-Path $bundleRoot 'Contents'
$distDir      = Join-Path $RepoRoot 'Distributable'

foreach ($p in @(
    (Join-Path $distDir 'AutoNAV.dll'),
    (Join-Path $distDir 'AutoNAV.addin'),
    (Join-Path $bundleRoot 'PackageContents.xml')
)) {
    if (-not (Test-Path -LiteralPath $p)) { throw "Missing input file: $p" }
}

foreach ($sub in @('V24','V25','V26','V27')) {
    $subDir = Join-Path $contentsRoot $sub
    if (-not (Test-Path -LiteralPath $subDir)) {
        New-Item -ItemType Directory -Path $subDir -Force | Out-Null
    }
    Copy-Item (Join-Path $distDir 'AutoNAV.dll')   (Join-Path $subDir 'AutoNAV.dll')   -Force
    Copy-Item (Join-Path $distDir 'AutoNAV.addin') (Join-Path $subDir 'AutoNAV.addin') -Force
}

$outFile = Join-Path $distDir 'AutoNAV-Installer.exe'

$env:GOOS        = 'windows'
$env:GOARCH      = 'amd64'
$env:CGO_ENABLED = '0'

Push-Location $installerDir
try {
    & go build -ldflags='-s -w' -o $outFile .
    if ($LASTEXITCODE -ne 0) { throw "go build failed (exit code $LASTEXITCODE)" }
} finally {
    Pop-Location
}

$outSize = (Get-Item $outFile).Length
Write-Host ("Wrote {0} ({1:N0} bytes)" -f $outFile, $outSize) -ForegroundColor Green
Write-Host "  Bundle staged at: $bundleRoot"
Write-Host "  PackageContents.xml controls per-version DLL routing inside the bundle."
