################################################################################
# AutoNAV Installer Script
# Version: 3.0.0
# Installs AutoNAV to all detected Navisworks Manage versions (2024-2027)
# Plugin path: C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\
# Author: Keith Acker
#
# File layout copied to each Navisworks version's Plugins\AutoNAV\ folder:
#   Track A (AddInPlugin - required):
#     - AutoNAV.dll
#     - AutoNAV.addin
#     - AutoNAV.pdb        (optional, debug symbols)
#   Track B (CommandHandlerPlugin - copied only if present in source folder):
#     - AutoNAV.xaml       (RibbonLayout XAML, if using a custom ribbon tab)
#     - en-US\             (localized XAML + .name resource file)
#     - Images\            (PNG icons referenced by the XAML)
# See NAVISWORKS_PLUGIN_REQUIREMENTS.md at the repo root for the full reference.
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

# Returns the Navisworks install directory if found, otherwise null
function Get-NavisworksInstallDir {
    param([string]$Version)
    $candidates = @(
        "C:\Program Files\Autodesk\Navisworks Manage $Version",
        "C:\Program Files (x86)\Autodesk\Navisworks Manage $Version"
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

# Returns the per-machine plugin folder (AutoNAV subfolder inside Plugins)
function Get-PluginInstallPath {
    param([string]$Version)
    return "C:\ProgramData\Autodesk\Navisworks Manage $Version\Plugins\AutoNAV"
}

function Install-ToVersion {
    param(
        [string]$Version,
        [string]$SourceDir,
        [string]$DllFile,
        [string]$AddinFile,
        [string]$PdbFile
    )

    $installDir = Get-NavisworksInstallDir -Version $Version
    if (-not $installDir) { return $false }

    Write-ColorOutput ("  Installing to Navisworks Manage " + $Version + "...") -Type Info

    $pluginPath = Get-PluginInstallPath -Version $Version

    if (-not (Test-Path $pluginPath)) {
        New-Item -ItemType Directory -Path $pluginPath -Force | Out-Null
    }

    $dllTarget   = Join-Path $pluginPath "AutoNAV.dll"
    $addinTarget = Join-Path $pluginPath "AutoNAV.addin"
    $pdbTarget   = Join-Path $pluginPath "AutoNAV.pdb"

    # Back up existing payload (DLL / .addin / Track-B subfolders) before overwriting
    if ((Test-Path $dllTarget) -or (Test-Path $addinTarget)) {
        $backupPath = Join-Path $pluginPath ("Backup_" + (Get-Date -Format 'yyyyMMdd_HHmmss'))
        New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
        if (Test-Path $dllTarget)   { Copy-Item $dllTarget   $backupPath -Force }
        if (Test-Path $addinTarget) { Copy-Item $addinTarget $backupPath -Force }
        foreach ($sub in @("en-US", "Images")) {
            $existing = Join-Path $pluginPath $sub
            if (Test-Path $existing) {
                Copy-Item $existing $backupPath -Recurse -Force
            }
        }
        Write-ColorOutput ("    Backup saved to: " + $backupPath) -Type Warning
    }

    # Track A (required): DLL + .addin
    Copy-Item $DllFile   $dllTarget   -Force
    Copy-Item $AddinFile $addinTarget -Force
    if ($PdbFile -and (Test-Path $PdbFile)) { Copy-Item $PdbFile $pdbTarget -Force }

    # Track B (CommandHandlerPlugin) assets: copy only if present in source.
    # See NAVISWORKS_PLUGIN_REQUIREMENTS.md section 4 for the required layout.
    $trackBCopied = @()

    # Ribbon layout XAML (sits next to the DLL)
    $xamlSource = Join-Path $SourceDir "AutoNAV.xaml"
    if (Test-Path $xamlSource) {
        Copy-Item $xamlSource (Join-Path $pluginPath "AutoNAV.xaml") -Force
        $trackBCopied += "AutoNAV.xaml"
    }

    # Localized XAML + .name strings (en-US folder is the default Navisworks locale)
    $enUSSource = Join-Path $SourceDir "en-US"
    if (Test-Path $enUSSource) {
        $enUSTarget = Join-Path $pluginPath "en-US"
        if (Test-Path $enUSTarget) { Remove-Item $enUSTarget -Recurse -Force }
        Copy-Item $enUSSource $enUSTarget -Recurse -Force
        $trackBCopied += "en-US\"
    }

    # Button icons (referenced by relative paths in the XAML)
    $imagesSource = Join-Path $SourceDir "Images"
    if (Test-Path $imagesSource) {
        $imagesTarget = Join-Path $pluginPath "Images"
        if (Test-Path $imagesTarget) { Remove-Item $imagesTarget -Recurse -Force }
        Copy-Item $imagesSource $imagesTarget -Recurse -Force
        $trackBCopied += "Images\"
    }

    $size = [math]::Round((Get-Item $dllTarget).Length / 1KB, 1)
    Write-ColorOutput ("  [+] Navisworks " + $Version + " -- installed (" + $size + " KB) -> " + $pluginPath) -Type Success
    if ($trackBCopied.Count -gt 0) {
        Write-ColorOutput ("      + ribbon assets: " + ($trackBCopied -join ', ')) -Type Success
    }
    return $true
}

function Uninstall-FromVersion {
    param([string]$Version)

    $pluginPath = Get-PluginInstallPath -Version $Version
    $dllTarget   = Join-Path $pluginPath "AutoNAV.dll"
    $addinTarget = Join-Path $pluginPath "AutoNAV.addin"
    $pdbTarget   = Join-Path $pluginPath "AutoNAV.pdb"
    $xamlTarget  = Join-Path $pluginPath "AutoNAV.xaml"
    $enUSTarget  = Join-Path $pluginPath "en-US"
    $imagesTarget = Join-Path $pluginPath "Images"

    # Also check old AddIns location so upgrades from older installs are cleaned up
    $oldAddInsPath = "C:\Program Files\Autodesk\Navisworks Manage $Version\AddIns"
    $oldDll        = Join-Path $oldAddInsPath "AutoNAV.dll"
    $oldAddin      = Join-Path $oldAddInsPath "AutoNAV.addin"
    $oldPdb        = Join-Path $oldAddInsPath "AutoNAV.pdb"

    $found = (Test-Path $dllTarget) -or (Test-Path $addinTarget) -or
             (Test-Path $xamlTarget) -or (Test-Path $enUSTarget) -or (Test-Path $imagesTarget) -or
             (Test-Path $oldDll) -or (Test-Path $oldAddin)
    if (-not $found) { return $false }

    # Track A files
    if (Test-Path $dllTarget)   { Remove-Item $dllTarget   -Force }
    if (Test-Path $addinTarget) { Remove-Item $addinTarget -Force }
    if (Test-Path $pdbTarget)   { Remove-Item $pdbTarget   -Force }

    # Track B (CommandHandlerPlugin) assets, if previously installed
    if (Test-Path $xamlTarget)   { Remove-Item $xamlTarget   -Force }
    if (Test-Path $enUSTarget)   { Remove-Item $enUSTarget   -Recurse -Force }
    if (Test-Path $imagesTarget) { Remove-Item $imagesTarget -Recurse -Force }

    # Legacy AddIns location from earlier AutoNAV releases
    if (Test-Path $oldDll)   { Remove-Item $oldDll   -Force }
    if (Test-Path $oldAddin) { Remove-Item $oldAddin -Force }
    if (Test-Path $oldPdb)   { Remove-Item $oldPdb   -Force }

    Write-ColorOutput ("  [+] Navisworks " + $Version + " -- removed") -Type Success
    return $true
}

function Install-AutoNAV {
    Write-ColorOutput "`n========================================" -Type Info
    Write-ColorOutput "  AutoNAV Installation  --  v3.0.0" -Type Info
    Write-ColorOutput "  Supports: Navisworks 2024 / 2025 / 2026 / 2027" -Type Info
    Write-ColorOutput "========================================`n" -Type Info

    if (-not (Test-Administrator)) {
        Write-ColorOutput "ERROR: Must be run as Administrator." -Type Error
        Write-ColorOutput "Right-click the .bat file and choose 'Run as administrator'." -Type Warning
        exit 1
    }

    $scriptPath = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $dllFile    = Join-Path $scriptPath "AutoNAV.dll"
    $addinFile  = Join-Path $scriptPath "AutoNAV.addin"
    $pdbFile    = Join-Path $scriptPath "AutoNAV.pdb"

    $missing = @()
    if (-not (Test-Path $dllFile))   { $missing += "AutoNAV.dll" }
    if (-not (Test-Path $addinFile)) { $missing += "AutoNAV.addin" }
    if ($missing.Count -gt 0) {
        Write-ColorOutput ("ERROR: Missing files: " + ($missing -join ', ')) -Type Error
        exit 1
    }
    Write-ColorOutput "[+] Installer files found`n" -Type Success

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

    $installed = @()
    $skipped   = @()

    foreach ($ver in $SupportedVersions) {
        try {
            $ok = Install-ToVersion -Version $ver -SourceDir $scriptPath -DllFile $dllFile -AddinFile $addinFile -PdbFile $pdbFile
            if ($ok) { $installed += $ver } else { $skipped += $ver }
        } catch {
            Write-ColorOutput ("  [!] Navisworks " + $ver + " -- error: " + $_) -Type Error
        }
    }

    Write-ColorOutput "`n========================================" -Type Info
    if ($installed.Count -gt 0) {
        Write-ColorOutput ("Installed to: Navisworks " + ($installed -join ', ')) -Type Success
    }
    if ($skipped.Count -gt 0) {
        Write-ColorOutput ("Not found (skipped): " + ($skipped -join ', ')) -Type Warning
    }

    if ($installed.Count -eq 0) {
        Write-ColorOutput "`nERROR: No compatible Navisworks installation found." -Type Error
        Write-ColorOutput "Install Navisworks Manage 2024, 2025, 2026, or 2027 and try again." -Type Warning
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
            Write-ColorOutput ("  [!] Navisworks " + $ver + " -- error: " + $_) -Type Error
        }
    }

    if ($removed.Count -gt 0) {
        Write-ColorOutput ("`n[+] Removed from: Navisworks " + ($removed -join ', ')) -Type Success
    } else {
        Write-ColorOutput "`nAutoNAV was not found in any Navisworks installation." -Type Warning
    }
    Write-ColorOutput "========================================`n" -Type Info
}

try {
    if ($Uninstall) { Uninstall-AutoNAV } else { Install-AutoNAV }
} catch {
    Write-ColorOutput ("ERROR: " + $_) -Type Error
    exit 1
}
