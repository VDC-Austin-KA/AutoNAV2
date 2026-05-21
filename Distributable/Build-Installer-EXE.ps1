################################################################################
# Build-Installer-EXE.ps1
#
# Regenerates Distributable\AutoNAV-Installer.exe - a single-file, double-click,
# self-elevating Windows .exe installer for AutoNAV.  Run this on a Windows
# machine after rebuilding AutoNAV.dll.
#
# Requirements:
#   - Go installed and on PATH        https://go.dev/dl/
#
# Inputs:
#   - Distributable\AutoNAV.dll       (copied into Installer\payload\)
#   - Distributable\AutoNAV.addin     (copied into Installer\payload\)
#
# Output:
#   - Distributable\AutoNAV-Installer.exe   (~2 MB, single file; distribute as-is)
#
# If Go is not available, use Build-Installer.ps1 instead to produce
# AutoNAV-Installer.cmd, which has identical behavior in 141 KB without any
# build tooling beyond what's bundled with Windows.
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
$payloadDir   = Join-Path $installerDir 'payload'
$distDir      = Join-Path $RepoRoot 'Distributable'

foreach ($p in @(
    (Join-Path $distDir 'AutoNAV.dll'),
    (Join-Path $distDir 'AutoNAV.addin')
)) {
    if (-not (Test-Path -LiteralPath $p)) { throw "Missing input file: $p" }
}

if (-not (Test-Path -LiteralPath $payloadDir)) {
    New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
}

Copy-Item (Join-Path $distDir 'AutoNAV.dll')   (Join-Path $payloadDir 'AutoNAV.dll')   -Force
Copy-Item (Join-Path $distDir 'AutoNAV.addin') (Join-Path $payloadDir 'AutoNAV.addin') -Force

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
Write-Host "  Embedded AutoNAV.dll  : $((Get-Item (Join-Path $distDir 'AutoNAV.dll')).Length) bytes raw"
Write-Host "  Embedded AutoNAV.addin: $((Get-Item (Join-Path $distDir 'AutoNAV.addin')).Length) bytes raw"
