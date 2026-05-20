################################################################################
# AutoNAV Installer Script
# Version: 3.0.0
# Description: Installs AutoNAV plugin to all detected Navisworks Manage versions
#              (2024, 2025, 2026, 2027)
# Author: Keith Acker
################################################################################

param(
    [switch]$Silent,
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$SupportedVersions = @("2024", "2025", "2026", "2027")

$Colors = @{
    Success = 'Green'
    Error   = 'Red'
    Warning = 'Yellow'
    Info    = 'Cyan'
}

function Write-ColorOutput {
    param(
        [Parameter(Mandatory=$true)][string]$Message,
        [ValidateSet('Success','Error','Warning','Info')][string]$Type = 'Info'
    )
    Write-Host $Message -ForegroundColor $Colors[$Type]
}

function Test-Administrator {
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-NavisworksAddinPath {
    param([string]$Version)
    $candidates = @(
        "C:\Program Files\Autodesk\Navisworks Manage $Version\AddIns",
        "C:\Program Files (x86)\Autodesk\Navisworks Manage $Version\AddIns"
    )
    foreach ($p in $candidates) {
        # Return path if parent Navisworks folder exists (AddIns may not yet exist)
        if (Test-Path (Split-Path $p -Parent)) { return $p }
    }
    return $null
}

function Install-ToVersion {
    param([string]$Version, [string]$DllFile, [string]$AddinFile, [string]$PdbFile)

    $addinPath = Get-NavisworksAddinPath -Version $Version
    if (-not $addinPath) { return $false }

    Write-ColorOutput "  Installing to Navisworks Manage $Version..." -Type Info

    if (-not (Test-Path $addinPath)) {
        New-Item -ItemType Directory -Path $addinPath -Force | Out-Null
    }

    $dllTarget   = Join-Path $addinPath "AutoNAV.dll"
    $addinTarget = Join-Path $addinPath "AutoNAV.addin"
    $pdbTarget   = Join-Path $addinPath "AutoNAV.pdb"

    # Backup existing files
    if ((Test-Path $dllTarget) -or (Test-Path $addinTarget)) {
        $backupPath = Join-Path $addinPath "AutoNAV_Backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
        New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
        if (Test-Path $dllTarget)   { Copy-Item $dllTarget   $backupPath -Force }
        if (Test-Path $addinTarget) { Copy-Item $addinTarget $backupPath -Force }
        Write-ColorOutput "    Backup saved to: $backupPath" -Type Warning
    }

    Copy-Item $DllFile   $dllTarget   -Force
    Copy-Item $AddinFile $addinTarget -Force
    if (Test-Path $PdbFile) { Copy-Item $PdbFile $pdbTarget -Force }

    $size = [math]::Round((Get-Item $dllTarget).Length / 1KB, 1)
    Write-ColorOutput "  [+] Navisworks $Version — installed ($size KB) → $addinPath" -Type Success
    return $true
}

function Uninstall-FromVersion {
    param([string]$Version)

    $addinPath = Get-NavisworksAddinPath -Version $Version
    if (-not $addinPath) { return $false }

    $dllTarget   = Join-Path $addinPath "AutoNAV.dll"
    $addinTarget = Join-Path $addinPath "AutoNAV.addin"
    $pdbTarget   = Join-Path $addinPath "AutoNAV.pdb"

    $found = (Test-Path $dllTarget) -or (Test-Path $addinTarget)
    if (-not $found) { return $false }

    if (Test-Path $dllTarget)   { Remove-Item $dllTarget   -Force }
    if (Test-Path $addinTarget) { Remove-Item $addinTarget -Force }
    if (Test-Path $pdbTarget)   { Remove-Item $pdbTarget   -Force }

    Write-ColorOutput "  [+] Navisworks $Version — removed" -Type Success
    return $true
}

function Install-AutoNAV {
    Write-ColorOutput "`n========================================" -Type Info
    Write-ColorOutput "  AutoNAV Installation  —  v3.0.0" -Type Info
    Write-ColorOutput "  Supports: Navisworks 2024 / 2025 / 2026 / 2027" -Type Info
    Write-ColorOutput "========================================`n" -Type Info

    if (-not (Test-Administrator)) {
        Write-ColorOutput "ERROR: Must be run as Administrator." -Type Error
        Write-ColorOutput "Right-click the .bat file and choose 'Run as administrator'." -Type Warning
        exit 1
    }

    # Locate installer files (same folder as this script)
    $scriptPath = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $dllFile    = Join-Path $scriptPath "AutoNAV.dll"
    $addinFile  = Join-Path $scriptPath "AutoNAV.addin"
    $pdbFile    = Join-Path $scriptPath "AutoNAV.pdb"

    $missing = @()
    if (-not (Test-Path $dllFile))   { $missing += "AutoNAV.dll" }
    if (-not (Test-Path $addinFile)) { $missing += "AutoNAV.addin" }
    if ($missing.Count -gt 0) {
        Write-ColorOutput "ERROR: Missing files: $($missing -join ', ')" -Type Error
        exit 1
    }
    Write-ColorOutput "[+] Installer files found`n" -Type Success

    # Close Navisworks if running
    $nwProc = Get-Process -Name "*Navisworks*" -ErrorAction SilentlyContinue
    if ($nwProc) {
        Write-ColorOutput "Closing Navisworks..." -Type Warning
        try {
            $nwProc | Stop-Process -Force -ErrorAction Stop
            Start-Sleep -Seconds 2
            Write-ColorOutput "[+] Navisworks closed`n" -Type Success
        } catch {
            Write-ColorOutput "WARNING: Could not close Navisworks. Please close it manually and retry." -Type Warning
            exit 1
        }
    }

    # Install to every detected version
    $installed = @()
    $skipped   = @()

    foreach ($ver in $SupportedVersions) {
        try {
            $ok = Install-ToVersion -Version $ver -DllFile $dllFile -AddinFile $addinFile -PdbFile $pdbFile
            if ($ok) { $installed += $ver } else { $skipped += $ver }
        } catch {
            Write-ColorOutput "  [!] Navisworks $ver — error: $_" -Type Error
        }
    }

    Write-ColorOutput "`n========================================" -Type Info
    if ($installed.Count -gt 0) {
        Write-ColorOutput "Installed to: Navisworks $($installed -join ', ')" -Type Success
    }
    if ($skipped.Count -gt 0) {
        Write-ColorOutput "Not found (skipped): $($skipped -join ', ')" -Type Warning
    }

    if ($installed.Count -eq 0) {
        Write-ColorOutput "`nERROR: No compatible Navisworks installation found." -Type Error
        Write-ColorOutput "Install Navisworks Manage 2024–2027 before running this installer." -Type Warning
        exit 1
    }

    Write-ColorOutput "`nNext steps:" -Type Info
    Write-ColorOutput "  1. Launch Navisworks Manage" -Type Info
    Write-ColorOutput "  2. Open the Add-Ins ribbon tab" -Type Info
    Write-ColorOutput "  3. Click AutoNAV to begin" -Type Info
    Write-ColorOutput "========================================`n" -Type Info
}

function Uninstall-AutoNAV {
    Write-ColorOutput "`n========================================" -Type Info
    Write-ColorOutput "  AutoNAV Uninstallation" -Type Info
    Write-ColorOutput "========================================`n" -Type Info

    if (-not (Test-Administrator)) {
        Write-ColorOutput "ERROR: Must be run as Administrator." -Type Error
        exit 1
    }

    $nwProc = Get-Process -Name "*Navisworks*" -ErrorAction SilentlyContinue
    if ($nwProc) {
        $nwProc | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }

    $removed = @()
    foreach ($ver in $SupportedVersions) {
        try {
            $ok = Uninstall-FromVersion -Version $ver
            if ($ok) { $removed += $ver }
        } catch {
            Write-ColorOutput "  [!] Navisworks $ver — error: $_" -Type Error
        }
    }

    if ($removed.Count -gt 0) {
        Write-ColorOutput "`n[+] Removed from: Navisworks $($removed -join ', ')" -Type Success
    } else {
        Write-ColorOutput "`nAutoNAV was not found in any Navisworks installation." -Type Warning
    }
    Write-ColorOutput "========================================`n" -Type Info
}

# Entry point
try {
    if ($Uninstall) { Uninstall-AutoNAV } else { Install-AutoNAV }
} catch {
    Write-ColorOutput "ERROR: $_" -Type Error
    exit 1
}
