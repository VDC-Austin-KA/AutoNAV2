@echo off
REM ============================================================================
REM AutoNAV Installer Batch Script
REM Version: 3.0.0
REM Description: User-friendly installer launcher for AutoNAV
REM ============================================================================

setlocal enabledelayedexpansion

REM Check for admin privileges
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo ============================================================================
    echo ERROR: Administrator privileges required!
    echo ============================================================================
    echo.
    echo This installer must be run as Administrator.
    echo.
    echo Please follow these steps:
    echo 1. Press Windows Key + X
    echo 2. Select "Windows Terminal (Admin)" or "Command Prompt (Admin)"
    echo 3. Navigate to this folder using: cd /d "%~dp0"
    echo 4. Run this batch file again
    echo.
    pause
    exit /b 1
)

REM Get the directory where this script is located
set "SCRIPT_DIR=%~dp0"
set "INSTALL_SCRIPT=%SCRIPT_DIR%Install-AutoNAV.ps1"

REM Check if PowerShell script exists
if not exist "!INSTALL_SCRIPT!" (
    echo.
    echo ============================================================================
    echo ERROR: Installation script not found!
    echo ============================================================================
    echo.
    echo Expected file: !INSTALL_SCRIPT!
    echo.
    echo The installer package appears to be incomplete.
    echo Please redownload and try again.
    echo.
    pause
    exit /b 1
)

REM Display welcome message
cls
echo.
echo ============================================================================
echo                         AutoNAV Plugin Installer
echo                            Version 3.0.0
echo ============================================================================
echo.
echo This installer will:
echo  - Detect your Navisworks Manage installation
echo  - Install the AutoNAV plugin
echo  - Register it in Navisworks
echo.
echo Installation location: AutoNAV plugin folder
echo.
echo ============================================================================
echo.

REM Launch PowerShell installer with proper execution policy
echo Launching installer...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "!INSTALL_SCRIPT!"
set "PS_EXIT_CODE=!errorlevel!"

echo.
if !PS_EXIT_CODE! equ 0 (
    echo ============================================================================
    echo Installation completed successfully!
    echo ============================================================================
    echo.
    echo Next steps:
    echo  1. Launch Navisworks Manage 2025
    echo  2. Look for AutoNAV in the Add-Ins ribbon tab
    echo  3. Click to start using AutoNAV
    echo.
    timeout /t 5 /nobreak
) else (
    echo ============================================================================
    echo Installation failed!
    echo ============================================================================
    echo.
    echo Please check the error messages above for details.
    echo.
    pause
    exit /b !PS_EXIT_CODE!
)

endlocal
