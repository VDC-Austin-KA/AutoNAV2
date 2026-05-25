# AutoNAV

Automated Design Coordination plugin for Autodesk Navisworks Manage 2024 / 2025 / 2026 / 2027.

## Install

Pick whichever works best for you:

### Option A — Single-file `.cmd` (recommended; zero AV warnings)

Download **`AutoNAV-Setup.cmd`** (~1.1 MB, single plain-text file) and double-click.

This is one file. No companion `.ps1` needed. The `.cmd` carries the entire installer (per-version DLLs + install logic) as a base64-encoded PowerShell payload embedded at the bottom; on launch it extracts the payload to `%TEMP%`, runs it (self-elevating via UAC), and cleans up afterwards. Plain text — Windows SmartScreen, Defender, and Chrome safe-browsing never flag it.

### Option B — NSIS-built `.exe`

Download **`AutoNAV-Setup.exe`** (~165 KB, built with NSIS) and double-click. NSIS is the same installer technology used by 7-Zip / OBS / Audacity / FileZilla etc., so the bootloader has long-standing reputation in most AV databases. The installer is still unsigned though, so SmartScreen *may* show a one-time "Windows protected your PC" prompt — if so, click **More info → Run anyway**.

### Option C — Two-file `.cmd` + `.ps1`

Download both `Install-AutoNAV.cmd` and `Install-AutoNAV.ps1` and keep them in the same folder, then double-click the `.cmd`. Functionally identical to Option A but split across two files.

### What either installer does

Both methods perform the same install. Windows will prompt for administrator rights (the installer writes into `C:\Program Files\Autodesk\…`). Accept, and the script will:

1. Close Navisworks if it's running.
2. For every detected Navisworks Manage 2024 / 2025 / 2026 / 2027 install, drop the matching `AutoNAV.dll` + `AutoNAV.addin` into `C:\Program Files\Autodesk\Navisworks Manage <year>\Plugins\AutoNAV\`.
3. Back up any prior install (`AutoNAV-Setup.exe` → `*.backup` files; `.ps1` → `Backup_<timestamp>\` folder).
4. Print a per-year status summary.

Launch Navisworks → **Add-Ins ribbon tab** → **AutoNAV**.

### Why isn't the .exe truly warning-free?

Code-signing — the only way to *guarantee* zero SmartScreen prompts — requires a signing certificate (~$200–700/year from DigiCert / Sectigo / Comodo). Without one, Windows treats every unsigned binary as untrusted by default, regardless of how it's built. NSIS-built installers fare better than raw Go binaries because the bootloader itself has a long reputation history, but they're not bulletproof. The PowerShell option (B) sidesteps the problem entirely because text files don't get SmartScreen treatment.

### Running from PowerShell directly

If you'd rather skip the `.cmd` wrapper:

```powershell
# Right-click Install-AutoNAV.ps1 → Run with PowerShell
# OR from a PowerShell prompt:
powershell -ExecutionPolicy Bypass -File Install-AutoNAV.ps1
```

## Uninstall

Delete the matching `Plugins\AutoNAV\` folder under each Navisworks Manage install. For example:

```
C:\Program Files\Autodesk\Navisworks Manage 2025\Plugins\AutoNAV\
```

## What's in the plugin

Six functions plus a one-button AutoNAVismate tab:

| Function | Purpose |
|---|---|
| 1 | Create per-discipline search sets from loaded model filenames (NCS dictionary + per-file fallback + picker for unknowns). |
| 2 | Create element-property search sets (System Abbreviation / Workset / etc., discipline-aware). |
| 3 | Custom search sets from any property value in a discipline's model. |
| 4 | Generate every cross-discipline clash test pair and auto-run them. |
| 5 | Group clash results into Walls / Floors per discipline pair. |
| 6 | Group clashes by proximity (nearest grid intersection) and name with the chosen template. Status filters + Rename Selected support. |
| 7 | Manual / advanced grouping fallback with primary + sub-grouping mode dropdowns. |
| **AutoNAVismate** | Single-button workflow runner that executes 1 → 2 → 4 → 5 → 6 with sensible defaults. |

## For developers

Source layout:

```
AutoNAV/
  AutoNAV.csproj                  legacy MSBuild project (local Navisworks install)
  AutoNAV.CI.csproj               SDK-style project (Speckle.Navisworks.API NuGet)
  *.cs, MainWindow.xaml           plugin source
```

Build for a specific Navisworks year via the SDK-style project (no Navisworks needed on the build machine):

```
dotnet build AutoNAV/AutoNAV.CI.csproj -c Release -p:Platform=x64 \
    -p:NWYear=2025 -p:NWPackageVersion=2025.0.0
```

Supported `NWYear` / `NWPackageVersion` pairs: `2024 / 2024.0.0`, `2025 / 2025.0.0`, `2026 / 2026.0.1`, `2027 / 2027.0.0`.

## License

Internal use only. © Keith Acker.
