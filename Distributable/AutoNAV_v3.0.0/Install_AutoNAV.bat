@echo off
REM AutoNAV Installation Script
REM This script installs AutoNAV plugin to Navisworks Manage

setlocal enabledelayedexpansion

echo.
echo ================================================================================
echo                        AutoNAV v3.0.0 Installation
echo ================================================================================
echo.

REM Check for admin privileges
openfiles >nul 2>&1
if errorlevel 1 (
    echo ERROR: This script requires Administrator privileges.
    echo Please right-click this file and select "Run as administrator"
    pause
    exit /b 1
)

REM Get the script directory
set SCRIPT_DIR=%~dp0
set PLUGIN_SOURCE=%SCRIPT_DIR%Plugin

if not exist "%PLUGIN_SOURCE%\AutoNAV.dll" (
    echo ERROR: Plugin files not found in %PLUGIN_SOURCE%
    echo Please ensure AutoNAV.dll and AutoNAV.addin are in the Plugin subfolder.
    pause
    exit /b 1
)

echo Detecting Navisworks installation...
echo.

REM Check for Navisworks 2026
if exist "C:\ProgramData\Autodesk\Navisworks Manage 2026" (
    set PLUGIN_DEST=C:\ProgramData\Autodesk\Navisworks Manage 2026\Plugins
    set NAVVERSION=2026
    goto install
)

REM Check for Navisworks 2025
if exist "C:\ProgramData\Autodesk\Navisworks Manage 2025" (
    set PLUGIN_DEST=C:\ProgramData\Autodesk\Navisworks Manage 2025\Plugins
    set NAVVERSION=2025
    goto install
)

REM Check for Navisworks 2024
if exist "C:\ProgramData\Autodesk\Navisworks Manage 2024" (
    set PLUGIN_DEST=C:\ProgramData\Autodesk\Navisworks Manage 2024\Plugins
    set NAVVERSION=2024
    goto install
)

echo ERROR: No compatible Navisworks installation found.
echo This plugin requires Navisworks 2024, 2025, or 2026.
echo.
echo Please ensure Navisworks Manage is installed before running this installer.
pause
exit /b 1

:install
echo Found Navisworks Manage %NAVVERSION%
echo Installation path: %PLUGIN_DEST%
echo.

REM Create the plugins directory if it doesn't exist
if not exist "%PLUGIN_DEST%" (
    echo Creating plugins directory...
    mkdir "%PLUGIN_DEST%"
    if errorlevel 1 (
        echo ERROR: Failed to create plugins directory
        pause
        exit /b 1
    )
)

REM Copy plugin files
echo Installing plugin files...
copy "%PLUGIN_SOURCE%\AutoNAV.dll" "%PLUGIN_DEST%\" >nul 2>&1
if errorlevel 1 (
    echo ERROR: Failed to copy AutoNAV.dll
    pause
    exit /b 1
)

copy "%PLUGIN_SOURCE%\AutoNAV.addin" "%PLUGIN_DEST%\" >nul 2>&1
if errorlevel 1 (
    echo ERROR: Failed to copy AutoNAV.addin
    pause
    exit /b 1
)

echo.
echo ================================================================================
echo                    Installation Successful!
echo ================================================================================
echo.
echo AutoNAV has been installed to:
echo %PLUGIN_DEST%
echo.
echo Next steps:
echo 1. Close any running Navisworks instances
echo 2. Restart Navisworks Manage
echo 3. Go to Add-ins menu to access AutoNAV
echo.
echo For more information, see README.txt
echo.
pause
