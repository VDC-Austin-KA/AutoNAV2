@echo off
REM AutoNAV Uninstallation Script
REM This script removes AutoNAV plugin from Navisworks Manage

setlocal enabledelayedexpansion

echo.
echo ================================================================================
echo                      AutoNAV v3.0.0 Uninstallation
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

echo Detecting Navisworks installation...
echo.

REM Check for Navisworks 2026
if exist "C:\ProgramData\Autodesk\Navisworks Manage 2026\Plugins" (
    set PLUGIN_DEST=C:\ProgramData\Autodesk\Navisworks Manage 2026\Plugins
    set NAVVERSION=2026
    goto uninstall
)

REM Check for Navisworks 2025
if exist "C:\ProgramData\Autodesk\Navisworks Manage 2025\Plugins" (
    set PLUGIN_DEST=C:\ProgramData\Autodesk\Navisworks Manage 2025\Plugins
    set NAVVERSION=2025
    goto uninstall
)

REM Check for Navisworks 2024
if exist "C:\ProgramData\Autodesk\Navisworks Manage 2024\Plugins" (
    set PLUGIN_DEST=C:\ProgramData\Autodesk\Navisworks Manage 2024\Plugins
    set NAVVERSION=2024
    goto uninstall
)

echo No Navisworks installation found. Nothing to uninstall.
pause
exit /b 0

:uninstall
echo Found Navisworks Manage %NAVVERSION%
echo Uninstalling from: %PLUGIN_DEST%
echo.

REM Remove plugin files
if exist "%PLUGIN_DEST%\AutoNAV.dll" (
    echo Removing AutoNAV.dll...
    del "%PLUGIN_DEST%\AutoNAV.dll" >nul 2>&1
)

if exist "%PLUGIN_DEST%\AutoNAV.addin" (
    echo Removing AutoNAV.addin...
    del "%PLUGIN_DEST%\AutoNAV.addin" >nul 2>&1
)

echo.
echo ================================================================================
echo                    Uninstallation Complete
echo ================================================================================
echo.
echo AutoNAV has been removed from Navisworks Manage.
echo Next Navisworks startup will not load AutoNAV.
echo.
pause
