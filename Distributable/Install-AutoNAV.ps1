################################################################################
# AutoNAV Installer Script (bundle format)
# Version: 3.0.0
# Installs the AutoNAV.bundle into the Autodesk ApplicationPlugins folder so
# Navisworks Manage 2024 / 2025 / 2026 / 2027 all pick it up.
# Author: Keith Acker
#
# Final layout on the user's machine:
#   %APPDATA%\Autodesk\ApplicationPlugins\AutoNAV.bundle\
#     PackageContents.xml
#     Contents\
#       V24\AutoNAV.dll + AutoNAV.addin     (Navisworks 2024)
#       V25\AutoNAV.dll + AutoNAV.addin     (Navisworks 2025)
#       V26\AutoNAV.dll + AutoNAV.addin     (Navisworks 2026)
#       V27\AutoNAV.dll + AutoNAV.addin     (Navisworks 2027)
#
# Per-user (%APPDATA%) install is the default and requires no admin.
# Pass -AllUsers to install machine-wide under %PROGRAMDATA%\Autodesk\ApplicationPlugins\
# (which DOES require admin).
################################################################################

param(
    [switch]$Silent,
    [switch]$Uninstall,
    [switch]$AllUsers
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

function Get-DestRoot {
    param([switch]$AllUsers)
    if ($AllUsers) { return Join-Path $env:PROGRAMDATA 'Autodesk\ApplicationPlugins' }
    return Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
}

function Find-BundleSource {
    param([string]$ScriptDir)
    # Source candidates next to this script:
    #   1. <ScriptDir>\AutoNAV.bundle\PackageContents.xml          (script colocated with bundle)
    #   2. <ScriptDir>\AutoNAV_v3.0.0\AutoNAV.bundle\PackageContents.xml
    foreach ($d in @(
        (Join-Path $ScriptDir 'AutoNAV.bundle'),
        (Join-Path $ScriptDir 'AutoNAV_v3.0.0\AutoNAV.bundle')
    )) {
        if (Test-Path (Join-Path $d 'PackageContents.xml')) { return $d }
    }
    return $null
}

function Install-Bundle {
    Write-ColorOutput "`n=========================================" -Type Info
    Write-ColorOutput "  AutoNAV Installation  (bundle format)   " -Type Info
    Write-ColorOutput "  Navisworks 2024 / 2025 / 2026 / 2027    " -Type Info
    Write-ColorOutput "=========================================`n" -Type Info

    if ($AllUsers -and -not (Test-Administrator)) {
        Write-ColorOutput "ERROR: -AllUsers requires running as Administrator." -Type Error
        exit 1
    }

    $scriptPath = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $bundleSrc = Find-BundleSource -ScriptDir $scriptPath
    if (-not $bundleSrc) {
        Write-ColorOutput "ERROR: Could not find AutoNAV.bundle next to this script." -Type Error
        Write-ColorOutput "Expected one of:" -Type Warning
        Write-ColorOutput "  .\AutoNAV.bundle\PackageContents.xml" -Type Warning
        Write-ColorOutput "  .\AutoNAV_v3.0.0\AutoNAV.bundle\PackageContents.xml" -Type Warning
        exit 1
    }
    Write-ColorOutput ("[+] Source bundle: " + $bundleSrc) -Type Success

    # Close Navisworks if running so DLLs aren't locked
    $nwProc = Get-Process -Name 'Roamer' -ErrorAction SilentlyContinue
    if ($nwProc) {
        Write-ColorOutput "Closing Navisworks..." -Type Warning
        try {
            $nwProc | Stop-Process -Force -ErrorAction Stop
            Start-Sleep -Seconds 2
        } catch {
            Write-ColorOutput "WARNING: Could not close Navisworks. Close it manually and retry." -Type Warning
            exit 1
        }
    }

    $destRoot   = Get-DestRoot -AllUsers:$AllUsers
    $destBundle = Join-Path $destRoot 'AutoNAV.bundle'

    if (-not (Test-Path $destRoot)) {
        New-Item -ItemType Directory -Path $destRoot -Force | Out-Null
    }

    if (Test-Path $destBundle) {
        $stamp  = Get-Date -Format 'yyyyMMdd_HHmmss'
        $backup = $destBundle + '.backup_' + $stamp
        Write-ColorOutput ("Backing up existing bundle -> " + $backup) -Type Warning
        Move-Item -Path $destBundle -Destination $backup -Force
    }

    Write-ColorOutput ("Copying bundle to " + $destBundle) -Type Info
    Copy-Item -Path $bundleSrc -Destination $destBundle -Recurse -Force

    Write-ColorOutput "`n========================================" -Type Info
    Write-ColorOutput ("Bundle installed at: " + $destBundle) -Type Success

    $count = (Get-ChildItem -Path $destBundle -Recurse -File).Count
    Write-ColorOutput ("Files written: " + $count) -Type Success

    Write-ColorOutput "`nNext steps:" -Type Info
    Write-ColorOutput "  1. Launch Navisworks Manage" -Type Info
    Write-ColorOutput "  2. Open the Add-Ins ribbon tab" -Type Info
    Write-ColorOutput "  3. Click AutoNAV to begin" -Type Info
    Write-ColorOutput "========================================`n" -Type Info
}

function Uninstall-Bundle {
    Write-ColorOutput "`n=========================================" -Type Info
    Write-ColorOutput "  AutoNAV Uninstallation" -Type Info
    Write-ColorOutput "=========================================`n" -Type Info

    $nwProc = Get-Process -Name 'Roamer' -ErrorAction SilentlyContinue
    if ($nwProc) {
        $nwProc | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }

    $removed = 0
    foreach ($root in @(
        (Join-Path $env:APPDATA     'Autodesk\ApplicationPlugins\AutoNAV.bundle'),
        (Join-Path $env:PROGRAMDATA 'Autodesk\ApplicationPlugins\AutoNAV.bundle')
    )) {
        if (Test-Path $root) {
            try {
                Remove-Item -Path $root -Recurse -Force
                Write-ColorOutput ("  [+] Removed " + $root) -Type Success
                $removed++
            } catch {
                Write-ColorOutput ("  [!] Could not remove " + $root + " -- " + $_) -Type Error
            }
        }
    }

    # Legacy per-version Plugins\AutoNAV\ from older releases
    foreach ($v in @('2024','2025','2026','2027')) {
        $legacy = "C:\ProgramData\Autodesk\Navisworks Manage $v\Plugins\AutoNAV"
        if (Test-Path $legacy) {
            try {
                Remove-Item -Path $legacy -Recurse -Force
                Write-ColorOutput ("  [+] Removed legacy " + $legacy) -Type Success
                $removed++
            } catch {
                Write-ColorOutput ("  [!] Could not remove " + $legacy + " -- " + $_) -Type Warning
            }
        }
    }

    if ($removed -eq 0) {
        Write-ColorOutput "`nAutoNAV was not found in any install location." -Type Warning
    } else {
        Write-ColorOutput ("`nUninstall complete: " + $removed + " location(s) cleaned up.") -Type Success
    }
    Write-ColorOutput "========================================`n" -Type Info
}

try {
    if ($Uninstall) { Uninstall-Bundle } else { Install-Bundle }
} catch {
    Write-ColorOutput ("ERROR: " + $_) -Type Error
    exit 1
}
