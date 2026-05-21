@echo off
REM AutoNAV v3.0.0 - Uninstallation Script (bundle format)
REM Removes the AutoNAV.bundle from BOTH per-user and all-users
REM ApplicationPlugins locations, plus any legacy installs from the old
REM ProgramData\Navisworks Manage <year>\Plugins\AutoNAV\ paths.

setlocal enabledelayedexpansion

echo.
echo ================================================================================
echo                     AutoNAV v3.0.0 Uninstallation
echo ================================================================================
echo.

REM Close Navisworks if running
tasklist /FI "IMAGENAME eq Roamer.exe" 2>nul | find /I "Roamer.exe" >nul
if not errorlevel 1 (
    echo Closing Navisworks...
    taskkill /F /IM Roamer.exe >nul 2>&1
    timeout /t 2 /nobreak >nul
)

set REMOVED=0

REM Bundle in per-user ApplicationPlugins
set "USER_BUNDLE=%APPDATA%\Autodesk\ApplicationPlugins\AutoNAV.bundle"
if exist "%USER_BUNDLE%" (
    echo Removing %USER_BUNDLE%
    rmdir /S /Q "%USER_BUNDLE%"
    set /a REMOVED+=1
)

REM Bundle in all-users ApplicationPlugins (if it was installed there)
set "ALL_BUNDLE=%PROGRAMDATA%\Autodesk\ApplicationPlugins\AutoNAV.bundle"
if exist "%ALL_BUNDLE%" (
    echo Removing %ALL_BUNDLE%
    rmdir /S /Q "%ALL_BUNDLE%" 2>nul
    if exist "%ALL_BUNDLE%" (
        echo   [!] Could not remove all-users bundle.  Re-run elevated to clean up.
    ) else (
        set /a REMOVED+=1
    )
)

REM Legacy per-version Plugins\AutoNAV\ folders (from pre-bundle installers)
for %%V in (2024 2025 2026 2027) do (
    set "LEGACY=%PROGRAMDATA%\Autodesk\Navisworks Manage %%V\Plugins\AutoNAV"
    if exist "!LEGACY!" (
        echo Removing legacy folder !LEGACY!
        rmdir /S /Q "!LEGACY!" 2>nul
        if not exist "!LEGACY!" set /a REMOVED+=1
    )
)

echo.
echo ================================================================================
if !REMOVED! gtr 0 (
    echo  AutoNAV removed.  !REMOVED! location^(s^) cleaned up.
) else (
    echo  AutoNAV was not found.  Nothing to do.
)
echo ================================================================================
echo.
pause
exit /b 0
