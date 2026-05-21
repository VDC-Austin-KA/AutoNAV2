################################################################################
# Build-MultiVersion.ps1
#
# Builds AutoNAV.dll for every supported Navisworks version installed on this
# machine (2024, 2025, 2026, 2027), stages each into the matching
# Installer\payload\AutoNAV.bundle\Contents\V<yy>\ subfolder, then rebuilds the
# single-file Distributable\AutoNAV-Installer.exe.
#
# Per-version output:
#   AutoNAV\bin\Release-NW2024\AutoNAV.dll  --> Installer\payload\AutoNAV.bundle\Contents\V24\AutoNAV.dll
#   AutoNAV\bin\Release-NW2025\AutoNAV.dll  --> Installer\payload\AutoNAV.bundle\Contents\V25\AutoNAV.dll
#   AutoNAV\bin\Release-NW2026\AutoNAV.dll  --> Installer\payload\AutoNAV.bundle\Contents\V26\AutoNAV.dll
#   AutoNAV\bin\Release-NW2027\AutoNAV.dll  --> Installer\payload\AutoNAV.bundle\Contents\V27\AutoNAV.dll
#
# Final install target on end-user machines:
#   %APPDATA%\Autodesk\ApplicationPlugins\AutoNAV.bundle\
#
# Versions not installed on this build machine are skipped (their bundle
# subfolder is left untouched -- a previous build's copy remains).
#
# Requirements:
#   - MSBuild (Visual Studio 2022+ or Build Tools)
#   - Go (https://go.dev/dl/) for the final .exe link step
#   - At least one Navisworks Manage 2024/2025/2026/2027 installed
################################################################################

[CmdletBinding()]
param(
    [string]$RepoRoot   = (Split-Path -Parent $PSScriptRoot),
    [string]$MSBuildExe = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe',
    [string[]]$Versions = @('2024','2025','2026','2027'),
    [switch]$SkipInstallerExe
)

$ErrorActionPreference = 'Stop'

$csproj       = Join-Path $RepoRoot 'AutoNAV\AutoNAV.csproj'
$installerDir = Join-Path $RepoRoot 'Installer'
$bundleRoot   = Join-Path $installerDir 'payload\AutoNAV.bundle'
$contentsRoot = Join-Path $bundleRoot   'Contents'
$distDir      = Join-Path $RepoRoot 'Distributable'
$addinSrc     = Join-Path $RepoRoot 'AutoNAV\AutoNAV.addin'
$pkgContents  = Join-Path $bundleRoot 'PackageContents.xml'

if (-not (Test-Path -LiteralPath $csproj))       { throw "Missing project: $csproj" }
if (-not (Test-Path -LiteralPath $addinSrc))     { throw "Missing addin manifest: $addinSrc" }
if (-not (Test-Path -LiteralPath $pkgContents))  { throw "Missing PackageContents.xml: $pkgContents" }
if (-not (Test-Path -LiteralPath $MSBuildExe))   { throw "MSBuild not found at: $MSBuildExe -- pass -MSBuildExe with the correct path." }

# Map year (4-digit) -> bundle subfolder (V24/V25/...)
function Get-BundleSubdir([string]$year) { 'V' + $year.Substring(2) }

Write-Host ""
Write-Host "===============================================================================" -ForegroundColor Cyan
Write-Host "  AutoNAV multi-version build (bundle format)" -ForegroundColor Cyan
Write-Host "===============================================================================" -ForegroundColor Cyan
Write-Host ""

$built   = @()
$skipped = @()

foreach ($v in $Versions) {
    $nwApi = "C:\Program Files\Autodesk\Navisworks Manage $v\Autodesk.Navisworks.Api.dll"
    if (-not (Test-Path -LiteralPath $nwApi)) {
        Write-Host ("  [--] Navisworks {0}  -- API not found, skipping" -f $v) -ForegroundColor DarkGray
        $skipped += $v
        continue
    }

    $config = "Release-NW$v"
    Write-Host ("  [..] Navisworks {0}  -- building {1}|x64" -f $v, $config) -ForegroundColor Yellow

    & $MSBuildExe $csproj `
        /p:Configuration=$config `
        /p:Platform=x64 `
        /t:Clean,Build `
        /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed for $config (exit code $LASTEXITCODE)"
    }

    $builtDll = Join-Path $RepoRoot ("AutoNAV\bin\Release-NW{0}\AutoNAV.dll" -f $v)
    $builtPdb = Join-Path $RepoRoot ("AutoNAV\bin\Release-NW{0}\AutoNAV.pdb" -f $v)
    if (-not (Test-Path -LiteralPath $builtDll)) {
        throw "Build succeeded but DLL not at expected path: $builtDll"
    }

    $sub = Get-BundleSubdir $v
    $bundleVerDir = Join-Path $contentsRoot $sub
    New-Item -ItemType Directory -Path $bundleVerDir -Force | Out-Null
    Copy-Item $builtDll  (Join-Path $bundleVerDir 'AutoNAV.dll')   -Force
    Copy-Item $addinSrc  (Join-Path $bundleVerDir 'AutoNAV.addin') -Force
    if (Test-Path -LiteralPath $builtPdb) {
        Copy-Item $builtPdb (Join-Path $bundleVerDir 'AutoNAV.pdb') -Force
    }

    # Mirror the same bundle into the classic distribution path so the .bat /
    # .ps1 installers ship the exact same tree.
    $classicBundle = Join-Path $distDir ('AutoNAV_v3.0.0\AutoNAV.bundle\Contents\' + $sub)
    New-Item -ItemType Directory -Path $classicBundle -Force | Out-Null
    Copy-Item $builtDll  (Join-Path $classicBundle 'AutoNAV.dll')   -Force
    Copy-Item $addinSrc  (Join-Path $classicBundle 'AutoNAV.addin') -Force
    if (Test-Path -LiteralPath $builtPdb) {
        Copy-Item $builtPdb (Join-Path $classicBundle 'AutoNAV.pdb') -Force
    }

    Write-Host ("       ->  {0}" -f $bundleVerDir)  -ForegroundColor Green
    Write-Host ("       ->  {0}" -f $classicBundle) -ForegroundColor Green
    $built += $v
}

# Sync PackageContents.xml into both bundle roots.
$classicBundleRoot = Join-Path $distDir 'AutoNAV_v3.0.0\AutoNAV.bundle'
New-Item -ItemType Directory -Path $classicBundleRoot -Force | Out-Null
Copy-Item $pkgContents (Join-Path $classicBundleRoot 'PackageContents.xml') -Force

Write-Host ""
Write-Host ("  Built:   {0}" -f ($(if ($built.Count)   { $built   -join ', ' } else { '(none)' }))) -ForegroundColor Green
Write-Host ("  Skipped: {0}" -f ($(if ($skipped.Count) { $skipped -join ', ' } else { '(none)' }))) -ForegroundColor DarkGray

if ($built.Count -eq 0) {
    throw "No Navisworks versions were built.  Install at least one of 2024/2025/2026/2027 and try again."
}

if ($SkipInstallerExe) {
    Write-Host ""
    Write-Host "  -SkipInstallerExe specified; not rebuilding AutoNAV-Installer.exe." -ForegroundColor Yellow
    return
}

if (-not (Get-Command 'go' -ErrorAction SilentlyContinue)) {
    Write-Host ""
    Write-Host "  Go not found on PATH -- skipping .exe build." -ForegroundColor Yellow
    Write-Host "  Install from https://go.dev/dl/ and re-run, or use Build-Installer.ps1 for the .cmd variant." -ForegroundColor Yellow
    return
}

Write-Host ""
Write-Host "  Linking AutoNAV-Installer.exe..." -ForegroundColor Cyan

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
Write-Host ""
Write-Host ("  Wrote {0} ({1:N0} bytes)" -f $outFile, $outSize) -ForegroundColor Green
Write-Host ""
Write-Host "===============================================================================" -ForegroundColor Cyan
Write-Host "  Done." -ForegroundColor Cyan
Write-Host "===============================================================================" -ForegroundColor Cyan
