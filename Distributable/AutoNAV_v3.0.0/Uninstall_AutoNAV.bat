@echo off
REM AutoNAV v3.0.0 - Uninstallation Script
REM Removes AutoNAV from ALL detected Navisworks Manage versions (2024 / 2025 / 2026 / 2027)
REM Cleans both the current plugin path and the legacy AddIns path

setlocal enabledelayedexpansion

echo.
echo ================================================================================
echo                      AutoNAV v3.0.0 Uninstallation
echo            Removes from: Navisworks Manage 2024 / 2025 / 2026 / 2027
echo ================================================================================
echo.

openfiles >nul 2>&1
if errorlevel 1 (
    echo ERROR: Administrator privileges required.
    echo Please right-click this file and select "Run as administrator".
    pause
    exit /b 1
)

REM Close Navisworks if running
tasklist /FI "IMAGENAME eq Navisworks*" 2>nul | find /I "Navisworks" >nul
if not errorlevel 1 (
    echo Closing Navisworks...
    taskkill /F /IM "Navisworks*" >nul 2>&1
    timeout /t 2 /nobreak >nul
)

set REMOVED_COUNT=0
set REMOVED_VERSIONS=

for %%V in (2024 2025 2026 2027) do (
    call :TryRemove %%V
)

echo.
echo ================================================================================
if !REMOVED_COUNT! gtr 0 (
    echo  AutoNAV removed from: Navisworks !REMOVED_VERSIONS!
) else (
    echo  AutoNAV was not found in any Navisworks installation.
)
echo ================================================================================
echo.
pause
exit /b 0

:TryRemove
set "NW_VERSION=%~1"
set FOUND=0

REM Current plugin path: ProgramData\...\Plugins\AutoNAV\
set "PLUGIN_DIR=C:\ProgramData\Autodesk\Navisworks Manage %NW_VERSION%\Plugins\AutoNAV"

REM Legacy path: Program Files\...\AddIns\ (used by older AutoNAV installs)
set "ADDIN_DIR=C:\Program Files\Autodesk\Navisworks Manage %NW_VERSION%\AddIns"

if exist "!PLUGIN_DIR!\AutoNAV.dll"   ( del /F /Q "!PLUGIN_DIR!\AutoNAV.dll"   >nul 2>&1 & set FOUND=1 )
if exist "!PLUGIN_DIR!\AutoNAV.addin" ( del /F /Q "!PLUGIN_DIR!\AutoNAV.addin" >nul 2>&1 & set FOUND=1 )
if exist "!PLUGIN_DIR!\AutoNAV.pdb"   ( del /F /Q "!PLUGIN_DIR!\AutoNAV.pdb"   >nul 2>&1 )

if exist "!ADDIN_DIR!\AutoNAV.dll"   ( del /F /Q "!ADDIN_DIR!\AutoNAV.dll"   >nul 2>&1 & set FOUND=1 )
if exist "!ADDIN_DIR!\AutoNAV.addin" ( del /F /Q "!ADDIN_DIR!\AutoNAV.addin" >nul 2>&1 & set FOUND=1 )
if exist "!ADDIN_DIR!\AutoNAV.pdb"   ( del /F /Q "!ADDIN_DIR!\AutoNAV.pdb"   >nul 2>&1 )

if !FOUND! equ 1 (
    echo  [+] Navisworks %NW_VERSION% -- removed
    set /a REMOVED_COUNT+=1
    if "!REMOVED_VERSIONS!"=="" (
        set "REMOVED_VERSIONS=%NW_VERSION%"
    ) else (
        set "REMOVED_VERSIONS=!REMOVED_VERSIONS!, %NW_VERSION%"
    )
) else (
    echo  [--] Navisworks %NW_VERSION% -- not installed, skipped
)
goto :eof
