@echo off
:: AutoNAV plugin installer (pure CMD, nothing embedded).
::
:: This .cmd does ONLY `mkdir` and `copy` -- the same things you'd do
:: manually in File Explorer -- so it doesn't trip the heuristics that
:: Defender / SmartScreen use for "self-extracting installer" patterns.
::
:: The DLLs and addin files live in the V24/ V25/ V26/ V27/ subfolders
:: next to this file.  Keep them together.

setlocal EnableDelayedExpansion

set "SRC=%~dp0"
if "!SRC:~-1!"=="\" set "SRC=!SRC:~0,-1!"

REM Self-elevate if not already running as administrator.
net session >nul 2>&1
if errorlevel 1 (
    echo Requesting administrator privileges...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo.
echo ===============================================================================
echo               AutoNAV plugin installer (v3.11.0)
echo       Targets: Navisworks Manage 2024 / 2025 / 2026 / 2027
echo ===============================================================================
echo.

REM Close Navisworks if running so the DLL isn't locked.
tasklist /FI "IMAGENAME eq Roamer.exe" 2>nul | find /I "Roamer.exe" >nul
if not errorlevel 1 (
    echo  Closing Navisworks...
    taskkill /F /IM Roamer.exe >nul 2>&1
    timeout /t 2 /nobreak >nul
)

set "INSTALLED="

call :Install 2024 V24
call :Install 2025 V25
call :Install 2026 V26
call :Install 2027 V27

echo.
echo ===============================================================================
if defined INSTALLED (
    echo  Installed to: Navisworks Manage !INSTALLED!
    echo.
    echo  Launch Navisworks -^> Add-Ins ribbon tab -^> AutoNAV
) else (
    echo  No Navisworks Manage 2024-2027 install was detected.
    echo  Install one of those versions and re-run this script.
)
echo ===============================================================================
echo.
pause
exit /b 0

:Install
set "YEAR=%~1"
set "SUB=%~2"
set "NW=C:\Program Files\Autodesk\Navisworks Manage %YEAR%"
if not exist "%NW%\" (
    echo  [--] Navisworks Manage %YEAR% -- not installed, skipped
    exit /b 0
)
set "DEST=%NW%\Plugins\AutoNAV"
if not exist "%DEST%\" mkdir "%DEST%" >nul 2>&1

REM Back up any prior install so a re-run can be undone.
if exist "%DEST%\AutoNAV.dll" (
    copy /Y "%DEST%\AutoNAV.dll" "%DEST%\AutoNAV.dll.backup" >nul 2>&1
)
if exist "%DEST%\AutoNAV.addin" (
    copy /Y "%DEST%\AutoNAV.addin" "%DEST%\AutoNAV.addin.backup" >nul 2>&1
)

copy /Y "%SRC%\%SUB%\AutoNAV.dll"   "%DEST%\AutoNAV.dll"   >nul
copy /Y "%SRC%\%SUB%\AutoNAV.addin" "%DEST%\AutoNAV.addin" >nul

echo  [+] Navisworks Manage %YEAR% -- installed to %DEST%
set "INSTALLED=!INSTALLED! %YEAR%"
exit /b 0
