################################################################################
# AutoNAV Installer Script
# Version: 3.0.0
# Description: Installs AutoNAV plugin to Navisworks Manage 2025
# Author: Keith Acker
# Date: May 2026
################################################################################

param(
    [switch]$Silent,
    [switch]$Uninstall
)

# Set strict mode for error handling
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Color output for better readability
$Colors = @{
    Success = 'Green'
    Error   = 'Red'
    Warning = 'Yellow'
    Info    = 'Cyan'
}

function Write-ColorOutput {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Message,
        
        [Parameter(Mandatory=$false)]
        [ValidateSet('Success', 'Error', 'Warning', 'Info')]
        [string]$Type = 'Info'
    )
    
    $color = $Colors[$Type]
    Write-Host $Message -ForegroundColor $color
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-NavisworksInstallPath {
    param(
        [string]$Version = "2025"
    )
    
    $possiblePaths = @(
        "C:\Program Files\Autodesk\Navisworks Manage $Version",
        "C:\Program Files (x86)\Autodesk\Navisworks Manage $Version"
    )
    
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            return $path
        }
    }
    
    return $null
}

function Get-NavisworksAddinPath {
    param(
        [string]$NavisworksPath
    )
    
    $addinPath = Join-Path $NavisworksPath "AddIns"
    return $addinPath
}

function Install-AutoNAV {
    Write-ColorOutput "`n========================================" -Type Info
    Write-ColorOutput "AutoNAV Installation" -Type Info
    Write-ColorOutput "Version: 3.0.0" -Type Info
    Write-ColorOutput "========================================`n" -Type Info
    
    # Step 1: Check admin privileges
    if (-not (Test-Administrator)) {
        Write-ColorOutput "ERROR: This installer must be run as Administrator!" -Type Error
        Write-ColorOutput "Please right-click the script and select 'Run with PowerShell as Administrator'" -Type Warning
        exit 1
    }
    Write-ColorOutput "[+] Administrator privileges confirmed" -Type Success
    
    # Step 2: Find Navisworks installation
    Write-ColorOutput "`nSearching for Navisworks Manage 2025..." -Type Info
    $navisworksPath = Get-NavisworksInstallPath -Version "2025"
    
    if (-not $navisworksPath) {
        Write-ColorOutput "Searching for Navisworks Manage 2024..." -Type Info
        $navisworksPath = Get-NavisworksInstallPath -Version "2024"
    }
    
    if (-not $navisworksPath) {
        Write-ColorOutput "ERROR: Navisworks Manage 2025 or 2024 not found!" -Type Error
        Write-ColorOutput "Please install Navisworks Manage before running this installer." -Type Warning
        exit 1
    }
    Write-ColorOutput "[+] Found Navisworks at: $navisworksPath" -Type Success
    
    # Step 3: Create or verify AddIns directory
    $addinPath = Get-NavisworksAddinPath -NavisworksPath $navisworksPath
    if (-not (Test-Path $addinPath)) {
        Write-ColorOutput "Creating AddIns directory..." -Type Info
        New-Item -ItemType Directory -Path $addinPath -Force | Out-Null
        Write-ColorOutput "[+] AddIns directory created" -Type Success
    }
    else {
        Write-ColorOutput "[+] AddIns directory exists" -Type Success
    }
    
    # Step 4: Locate installer files
    $scriptPath = $PSScriptRoot
    if ([string]::IsNullOrEmpty($scriptPath)) {
        $scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    if ([string]::IsNullOrEmpty($scriptPath)) {
        $scriptPath = Get-Location
    }
    $dllFile = Join-Path $scriptPath "AutoNAV.dll"
    $addinFile = Join-Path $scriptPath "AutoNAV.addin"
    $pdbFile = Join-Path $scriptPath "AutoNAV.pdb"
    
    # Verify files exist
    $missingFiles = @()
    if (-not (Test-Path $dllFile)) { $missingFiles += "AutoNAV.dll" }
    if (-not (Test-Path $addinFile)) { $missingFiles += "AutoNAV.addin" }
    
    if ($missingFiles.Count -gt 0) {
        Write-ColorOutput "ERROR: Missing required files: $($missingFiles -join ', ')" -Type Error
        Write-ColorOutput "Installation package is incomplete. Please redownload the installer." -Type Warning
        exit 1
    }
    Write-ColorOutput "[+] All required files found" -Type Success
    
    # Step 5: Backup existing files if they exist
    $dllTarget = Join-Path $addinPath "AutoNAV.dll"
    $addinTarget = Join-Path $addinPath "AutoNAV.addin"
    
    if ((Test-Path $dllTarget) -or (Test-Path $addinTarget)) {
        Write-ColorOutput "`nPrevious installation found. Creating backup..." -Type Warning
        $backupPath = Join-Path $addinPath "AutoNAV_Backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
        New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
        
        if (Test-Path $dllTarget) { Copy-Item $dllTarget $backupPath -Force }
        if (Test-Path $addinTarget) { Copy-Item $addinTarget $backupPath -Force }
        
        Write-ColorOutput "[+] Backup created at: $backupPath" -Type Success
    }
    
    # Step 6: Close Navisworks if running
    $navisworksProc = Get-Process -Name "*Navisworks*" -ErrorAction SilentlyContinue
    if ($null -ne $navisworksProc) {
        Write-ColorOutput "`nNavisworks is currently running. Attempting to close..." -Type Warning
        try {
            $navisworksProc | Stop-Process -Force -ErrorAction Stop
            Start-Sleep -Seconds 2
            Write-ColorOutput "[+] Navisworks closed successfully" -Type Success
        }
        catch {
            Write-ColorOutput "WARNING: Could not close Navisworks automatically." -Type Warning
            Write-ColorOutput "Please close Navisworks manually and try again." -Type Warning
            exit 1
        }
    }
    else {
        Write-ColorOutput "[+] Navisworks is not running" -Type Success
    }
    
    # Step 7: Copy files
    Write-ColorOutput "`nInstalling AutoNAV files..." -Type Info
    try {
        Copy-Item $dllFile $dllTarget -Force
        Copy-Item $addinFile $addinTarget -Force
        if (Test-Path $pdbFile) {
            Copy-Item $pdbFile (Join-Path $addinPath "AutoNAV.pdb") -Force
        }
        Write-ColorOutput "[+] Files copied successfully" -Type Success
    }
    catch {
        Write-ColorOutput "ERROR: Failed to copy files: $_" -Type Error
        exit 1
    }
    
    # Step 8: Verify installation
    Write-ColorOutput "`nVerifying installation..." -Type Info
    if ((Test-Path $dllTarget) -and (Test-Path $addinTarget)) {
        $dllInfo = Get-Item $dllTarget
        Write-ColorOutput "[+] AutoNAV.dll installed ($([math]::Round($dllInfo.Length/1KB, 2)) KB)" -Type Success
        Write-ColorOutput "[+] AutoNAV.addin manifest installed" -Type Success
    }
    else {
        Write-ColorOutput "ERROR: Verification failed - files not found after installation" -Type Error
        exit 1
    }
    
    # Step 9: Success message
    Write-ColorOutput "`n========================================" -Type Success
    Write-ColorOutput "Installation Completed Successfully!" -Type Success
    Write-ColorOutput "========================================`n" -Type Success
    
    Write-ColorOutput "What's next:" -Type Info
    Write-ColorOutput "1. Launch Navisworks Manage 2025" -Type Info
    Write-ColorOutput "2. Look for AutoNAV in the Add-Ins ribbon tab" -Type Info
    Write-ColorOutput "3. Click to open the AutoNAV panel" -Type Info
    Write-ColorOutput "`nInstallation location: $addinPath`n" -Type Info
}

function Uninstall-AutoNAV {
    Write-ColorOutput "`n========================================" -Type Info
    Write-ColorOutput "AutoNAV Uninstallation" -Type Info
    Write-ColorOutput "========================================`n" -Type Info
    
    # Step 1: Check admin privileges
    if (-not (Test-Administrator)) {
        Write-ColorOutput "ERROR: This uninstaller must be run as Administrator!" -Type Error
        exit 1
    }
    
    # Step 2: Find Navisworks installation
    $navisworksPath = Get-NavisworksInstallPath -Version "2025"
    if (-not $navisworksPath) {
        $navisworksPath = Get-NavisworksInstallPath -Version "2024"
    }
    
    if (-not $navisworksPath) {
        Write-ColorOutput "ERROR: Navisworks installation not found" -Type Error
        exit 1
    }
    
    $addinPath = Get-NavisworksAddinPath -NavisworksPath $navisworksPath
    $dllTarget = Join-Path $addinPath "AutoNAV.dll"
    $addinTarget = Join-Path $addinPath "AutoNAV.addin"
    
    # Step 3: Close Navisworks if running
    $navisworksProc = Get-Process -Name "*Navisworks*" -ErrorAction SilentlyContinue
    if ($null -ne $navisworksProc) {
        Write-ColorOutput "Closing Navisworks..." -Type Warning
        $navisworksProc | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    
    # Step 4: Remove files
    Write-ColorOutput "Removing AutoNAV files..." -Type Info
    try {
        if (Test-Path $dllTarget) { Remove-Item $dllTarget -Force }
        if (Test-Path $addinTarget) { Remove-Item $addinTarget -Force }
        $pdbTarget = Join-Path $addinPath "AutoNAV.pdb"
        if (Test-Path $pdbTarget) { Remove-Item $pdbTarget -Force }
        Write-ColorOutput "[+] AutoNAV uninstalled successfully" -Type Success
    }
    catch {
        Write-ColorOutput "ERROR: Failed to remove files: $_" -Type Error
        exit 1
    }
}

# Main script execution
try {
    if ($Uninstall) {
        Uninstall-AutoNAV
    }
    else {
        Install-AutoNAV
    }
}
catch {
    Write-ColorOutput "ERROR: An unexpected error occurred: $_" -Type Error
    exit 1
}
