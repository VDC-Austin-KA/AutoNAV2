@echo off
REM AutoNAV v3.0.0 - Installation Script
REM Installs to ALL detected Navisworks Manage versions (2024 / 2025 / 2026 / 2027)
REM Plugin path: C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\
REM
REM Files copied to each version's Plugins\AutoNAV\ folder:
REM   Track A (AddInPlugin - required):
REM     - AutoNAV.dll
REM     - AutoNAV.addin
REM     - AutoNAV.pdb     (optional, debug symbols)
REM   Track B (CommandHandlerPlugin - copied only if present in .\Plugin\):
REM     - AutoNAV.xaml    (RibbonLayout XAML for a custom ribbon tab)
REM     - en-US\          (localized XAML + .name strings)
REM     - Images\         (PNG icons referenced by the XAML)
REM See NAVISWORKS_PLUGIN_REQUIREMENTS.md at the repo root for the full reference.

setlocal enabledelayedexpansion

echo.
echo ================================================================================
echo                        AutoNAV v3.0.0 Installation
echo            Supports: Navisworks Manage 2024 / 2025 / 2026 / 2027
echo ================================================================================
echo.

REM Require admin
openfiles >nul 2>&1
if errorlevel 1 (
    echo ERROR: Administrator privileges required.
    echo Please right-click this file and select "Run as administrator".
    pause
    exit /b 1
)

set "SCRIPT_DIR=%~dp0"
set "PLUGIN_ROOT=%SCRIPT_DIR%Plugin"

REM Per-version subfolders (Plugin\2024\, Plugin\2025\, ...) take precedence over
REM the legacy single-folder layout (Plugin\AutoNAV.dll).  Either layout is OK as
REM long as at least one per-version subfolder or the legacy pair exists.
set "HAS_ANY_SOURCE=0"
for %%V in (2024 2025 2026 2027) do (
    if exist "%PLUGIN_ROOT%\%%V\AutoNAV.dll" if exist "%PLUGIN_ROOT%\%%V\AutoNAV.addin" set "HAS_ANY_SOURCE=1"
)
if exist "%PLUGIN_ROOT%\AutoNAV.dll" if exist "%PLUGIN_ROOT%\AutoNAV.addin" set "HAS_ANY_SOURCE=1"

if "!HAS_ANY_SOURCE!"=="0" (
    echo ERROR: No plugin payload found.
    echo Expected one of:
    echo   %PLUGIN_ROOT%\^<year^>\AutoNAV.dll + AutoNAV.addin   (per-version, preferred)
    echo   %PLUGIN_ROOT%\AutoNAV.dll + AutoNAV.addin            (legacy single-version)
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

set INSTALLED_COUNT=0
set INSTALLED_VERSIONS=

REM ---- Try each supported version ----
for %%V in (2024 2025 2026 2027) do (
    call :TryInstall %%V
)

echo.
echo ================================================================================
if !INSTALLED_COUNT! gtr 0 (
    echo  Installation successful!
    echo  Installed to: Navisworks !INSTALLED_VERSIONS!
) else (
    echo  ERROR: No compatible Navisworks installation found.
    echo  Install Navisworks Manage 2024, 2025, 2026, or 2027 and try again.
)
echo ================================================================================
echo.
echo Next steps:
echo  1. Launch Navisworks Manage
echo  2. Open the Add-Ins ribbon tab
echo  3. Click AutoNAV to begin
echo.
pause
exit /b 0

REM ---- Subroutine: install to one version ----
:TryInstall
set "NW_VERSION=%~1"
set "NW_INSTALL_DIR=C:\Program Files\Autodesk\Navisworks Manage %NW_VERSION%"

REM Navisworks must be installed to proceed
if not exist "!NW_INSTALL_DIR!" (
    echo  [--] Navisworks %NW_VERSION% not found -- skipped
    goto :eof
)

REM Per-version source if available, else fall back to legacy single-folder layout.
set "PLUGIN_SOURCE=%PLUGIN_ROOT%\%NW_VERSION%"
if not exist "!PLUGIN_SOURCE!\AutoNAV.dll" (
    set "PLUGIN_SOURCE=%PLUGIN_ROOT%"
)
if not exist "!PLUGIN_SOURCE!\AutoNAV.dll" (
    echo  [!] Navisworks %NW_VERSION% -- no source DLL found in Plugin\%NW_VERSION%\ or Plugin\
    goto :eof
)

REM Plugin destination: ProgramData\...\Plugins\AutoNAV\ (works for all versions 2024-2027)
set "DEST=C:\ProgramData\Autodesk\Navisworks Manage %NW_VERSION%\Plugins\AutoNAV"

if not exist "!DEST!" mkdir "!DEST!"

REM ---- Track A (required): DLL + .addin (+ optional PDB) ----
copy /Y "!PLUGIN_SOURCE!\AutoNAV.dll"   "!DEST!\AutoNAV.dll"   >nul 2>&1
copy /Y "!PLUGIN_SOURCE!\AutoNAV.addin" "!DEST!\AutoNAV.addin" >nul 2>&1
if exist "!PLUGIN_SOURCE!\AutoNAV.pdb" (
    copy /Y "!PLUGIN_SOURCE!\AutoNAV.pdb" "!DEST!\AutoNAV.pdb" >nul 2>&1
)

if errorlevel 1 (
    echo  [!] Navisworks %NW_VERSION% -- copy failed, check permissions
    goto :eof
)

REM ---- Track B (optional): RibbonLayout XAML, en-US strings, Images icons ----
REM Copied only if present in the per-version source folder, then falling back to
REM the root Plugin\ folder.  Required by Navisworks only when the plugin uses
REM CommandHandlerPlugin with a custom ribbon tab.
set "TRACKB_EXTRA="
set "RIBBON_SRC=!PLUGIN_SOURCE!"
if not exist "!RIBBON_SRC!\AutoNAV.xaml" if exist "%PLUGIN_ROOT%\AutoNAV.xaml" set "RIBBON_SRC=%PLUGIN_ROOT%"

if exist "!RIBBON_SRC!\AutoNAV.xaml" (
    copy /Y "!RIBBON_SRC!\AutoNAV.xaml" "!DEST!\AutoNAV.xaml" >nul 2>&1
    set "TRACKB_EXTRA=!TRACKB_EXTRA! AutoNAV.xaml"
)

if exist "!RIBBON_SRC!\en-US" (
    if not exist "!DEST!\en-US" mkdir "!DEST!\en-US"
    xcopy /Y /E /I /Q "!RIBBON_SRC!\en-US" "!DEST!\en-US" >nul 2>&1
    set "TRACKB_EXTRA=!TRACKB_EXTRA! en-US\"
)

if exist "!RIBBON_SRC!\Images" (
    if not exist "!DEST!\Images" mkdir "!DEST!\Images"
    xcopy /Y /E /I /Q "!RIBBON_SRC!\Images" "!DEST!\Images" >nul 2>&1
    set "TRACKB_EXTRA=!TRACKB_EXTRA! Images\"
)

echo  [+] Navisworks %NW_VERSION% -- installed to !DEST!
if not "!TRACKB_EXTRA!"=="" echo      + ribbon assets:!TRACKB_EXTRA!
set /a INSTALLED_COUNT+=1
if "!INSTALLED_VERSIONS!"=="" (
    set "INSTALLED_VERSIONS=%NW_VERSION%"
) else (
    set "INSTALLED_VERSIONS=!INSTALLED_VERSIONS!, %NW_VERSION%"
)
goto :eof
