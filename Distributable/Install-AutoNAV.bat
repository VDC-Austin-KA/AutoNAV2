@echo off
REM ============================================================================
REM AutoNAV Installer (bundle format) - v3.0.0
REM Installs AutoNAV.bundle\ into:
REM   %APPDATA%\Autodesk\ApplicationPlugins\AutoNAV.bundle\
REM PackageContents.xml inside the bundle routes Navisworks 2024/25/26/27 to
REM its matching per-version DLL.
REM
REM Per-user install: no admin required.
REM ============================================================================

setlocal enabledelayedexpansion

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
echo                        AutoNAV Plugin Installer (bundle)
echo                              Version 3.0.0
echo ============================================================================
echo.
echo  Installs the AutoNAV.bundle to:
echo    %%APPDATA%%\Autodesk\ApplicationPlugins\AutoNAV.bundle\
echo.
echo  Navisworks Manage 2024, 2025, 2026, and 2027 will all pick it up via
echo  PackageContents.xml.
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
    echo  Launch any installed Navisworks Manage version and open the
    echo  Add-Ins ribbon tab to find AutoNAV.
    echo.
    timeout /t 5 /nobreak
) else (
    echo ============================================================================
    echo  Installation encountered errors -- see messages above.
    echo ============================================================================
    echo.
    pause
    exit /b !PS_EXIT!
)

endlocal
