@echo off
:: Removes the AutoNAV plugin from every Navisworks Manage install.
setlocal EnableDelayedExpansion

net session >nul 2>&1
if errorlevel 1 (
    echo Requesting administrator privileges...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo.
echo  AutoNAV plugin uninstaller
echo.

tasklist /FI "IMAGENAME eq Roamer.exe" 2>nul | find /I "Roamer.exe" >nul
if not errorlevel 1 (
    echo  Closing Navisworks...
    taskkill /F /IM Roamer.exe >nul 2>&1
    timeout /t 2 /nobreak >nul
)

set "REMOVED="
for %%Y in (2024 2025 2026 2027) do (
    set "DEST=C:\Program Files\Autodesk\Navisworks Manage %%Y\Plugins\AutoNAV"
    if exist "!DEST!\" (
        rmdir /S /Q "!DEST!"
        echo  [-] Removed: !DEST!
        set "REMOVED=!REMOVED! %%Y"
    )
)

REM Copies left by earlier installers too.
call :RemoveStale "%ProgramData%\Autodesk\ApplicationPlugins\AutoNAV.bundle"
call :RemoveStale "%APPDATA%\Autodesk\ApplicationPlugins\AutoNAV.bundle"
for %%Y in (2024 2025 2026 2027) do (
    call :RemoveStale "%ProgramData%\Autodesk\Navisworks Manage %%Y\Plugins\AutoNAV"
    call :RemoveStale "%APPDATA%\Autodesk\Navisworks Manage %%Y\Plugins\AutoNAV"
    call :RemoveStale "%LOCALAPPDATA%\Autodesk\Navisworks Manage %%Y\Plugins\AutoNAV"
)

if defined STALE set "REMOVED=!REMOVED! (plus old copies)"

if defined REMOVED (
    echo.
    echo  Removed from: Navisworks Manage !REMOVED!
) else (
    echo  No AutoNAV install was found.
)
echo.
pause
exit /b 0

:RemoveStale
if not exist "%~1\" exit /b 0
rmdir /S /Q "%~1" >nul 2>&1
if exist "%~1\" (
    echo  [!!] Could not remove old copy: %~1
) else (
    echo  [-] Removed old copy: %~1
    set "STALE=1"
)
exit /b 0
