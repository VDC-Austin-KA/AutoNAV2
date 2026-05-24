@echo off
REM AutoNAV plugin installer launcher.
REM Just calls the PowerShell installer next to this file with execution
REM policy bypass so users can double-click instead of right-click.
REM Both files are plain text -- no Windows SmartScreen / AV warning.

set "PSPATH=%~dp0Install-AutoNAV.ps1"
if not exist "%PSPATH%" (
    echo ERROR: Install-AutoNAV.ps1 not found next to this script.
    echo Looked for: "%PSPATH%"
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PSPATH%"
