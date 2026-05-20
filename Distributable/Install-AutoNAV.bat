@echo off
REM ============================================================================
REM AutoNAV Installer — v3.0.0
REM Installs to ALL detected versions of Navisworks Manage (2024 / 2025 / 2026 / 2027)
REM ============================================================================

setlocal enabledelayedexpansion

REM Require admin
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo ============================================================================
    echo  ERROR: Administrator privileges required!
    echo ============================================================================
    echo.
    echo  Please right-click this file and select "Run as administrator".
    echo.
    pause
    exit /b 1
)

set "SCRIPT_DIR=%~dp0"
set "INSTALL_SCRIPT=%SCRIPT_DIR%Install-AutoNAV.ps1"

if not exist "!INSTALL_SCRIPT!" (
    echo.
    echo ============================================================================
    echo  ERROR: Install-AutoNAV.ps1 not found!
    echo ============================================================================
    echo  Expected: !INSTALL_SCRIPT!
    echo  The installer package may be incomplete. Please redownload.
    echo.
    pause
    exit /b 1
)

cls
echo.
echo ============================================================================
echo                        AutoNAV Plugin Installer
echo                             Version 3.0.0
echo ============================================================================
echo.
echo  This installer will find every copy of Navisworks Manage on this machine
echo  (2024, 2025, 2026, 2027) and install AutoNAV to each one automatically.
echo.
echo ============================================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "!INSTALL_SCRIPT!"
set "PS_EXIT=%errorlevel%"

echo.
if !PS_EXIT! equ 0 (
    echo ============================================================================
    echo  Installation complete!
    echo ============================================================================
    echo.
    echo  Launch any installed version of Navisworks Manage and open the
    echo  Add-Ins ribbon tab to find AutoNAV.
    echo.
    timeout /t 5 /nobreak
) else (
    echo ============================================================================
    echo  Installation encountered errors — see messages above.
    echo ============================================================================
    echo.
    pause
    exit /b !PS_EXIT!
)

endlocal
