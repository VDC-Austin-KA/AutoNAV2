@echo off
REM AutoNAV v3.0.0 - Installation Script (bundle format)
REM Copies AutoNAV.bundle\ into:
REM   %APPDATA%\Autodesk\ApplicationPlugins\AutoNAV.bundle\
REM
REM PackageContents.xml inside the bundle tells Navisworks 2024 / 2025 / 2026 /
REM 2027 which per-version DLL under Contents\V24..V27\ to load.
REM
REM This is per-user (no admin required).  For all-users install, use the
REM PowerShell installer with -AllUsers.

setlocal enabledelayedexpansion

echo.
echo ================================================================================
echo                     AutoNAV v3.0.0 Installation (bundle)
echo            Targets: Navisworks Manage 2024 / 2025 / 2026 / 2027
echo            Install path: %%APPDATA%%\Autodesk\ApplicationPlugins\AutoNAV.bundle\
echo ================================================================================
echo.

set "SCRIPT_DIR=%~dp0"
set "BUNDLE_SRC=%SCRIPT_DIR%AutoNAV.bundle"

if not exist "%BUNDLE_SRC%\PackageContents.xml" (
    echo ERROR: Bundle source not found.
    echo Expected: %BUNDLE_SRC%\PackageContents.xml
    pause
    exit /b 1
)

REM Close Navisworks if running
tasklist /FI "IMAGENAME eq Roamer.exe" 2>nul | find /I "Roamer.exe" >nul
if not errorlevel 1 (
    echo Closing Navisworks...
    taskkill /F /IM Roamer.exe >nul 2>&1
    timeout /t 2 /nobreak >nul
)

set "DEST_ROOT=%APPDATA%\Autodesk\ApplicationPlugins"
set "DEST_BUNDLE=%DEST_ROOT%\AutoNAV.bundle"

if not exist "%DEST_ROOT%" mkdir "%DEST_ROOT%"

REM Back up any existing bundle
if exist "%DEST_BUNDLE%" (
    set "STAMP=%DATE:~10,4%%DATE:~4,2%%DATE:~7,2%_%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%"
    set "STAMP=!STAMP: =0!"
    echo Backing up existing bundle to %DEST_BUNDLE%.backup_!STAMP!
    move /Y "%DEST_BUNDLE%" "%DEST_BUNDLE%.backup_!STAMP!" >nul
)

echo Copying bundle to %DEST_BUNDLE% ...
xcopy /Y /E /I /Q "%BUNDLE_SRC%" "%DEST_BUNDLE%" >nul
if errorlevel 1 (
    echo ERROR: Failed to copy bundle.  Check permissions on %DEST_ROOT%.
    pause
    exit /b 1
)

echo.
echo ================================================================================
echo  Installation complete!
echo.
echo  Bundle installed at:
echo    %DEST_BUNDLE%
echo.
echo  Layout:
echo    AutoNAV.bundle\
echo    +-- PackageContents.xml      (multi-version manifest)
echo    +-- Contents\
echo        +-- V24\AutoNAV.dll + AutoNAV.addin    (Navisworks 2024)
echo        +-- V25\AutoNAV.dll + AutoNAV.addin    (Navisworks 2025)
echo        +-- V26\AutoNAV.dll + AutoNAV.addin    (Navisworks 2026)
echo        +-- V27\AutoNAV.dll + AutoNAV.addin    (Navisworks 2027)
echo ================================================================================
echo.
echo Next steps:
echo  1. Launch Navisworks Manage
echo  2. Open the Add-Ins ribbon tab
echo  3. Click AutoNAV to begin
echo.
pause
exit /b 0
