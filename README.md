# AutoNAV

Automated Design Coordination plugin for Autodesk Navisworks Manage 2024 / 2025 / 2026 / 2027.

## Install

Download these two files from the [latest commit](../../tree/main):

- **`Install-AutoNAV.cmd`**
- **`Install-AutoNAV.ps1`**

Keep them in the **same folder**, then double-click **`Install-AutoNAV.cmd`**.

Windows will prompt for administrator rights (the installer writes into `C:\Program Files\Autodesk\…`). Accept, and the script will:

1. Close Navisworks if it's running.
2. For every detected Navisworks Manage 2024 / 2025 / 2026 / 2027 install, drop the matching `AutoNAV.dll` + `AutoNAV.addin` into `C:\Program Files\Autodesk\Navisworks Manage <year>\Plugins\AutoNAV\`.
3. Back up any prior install into `…\Plugins\AutoNAV\Backup_<timestamp>\`.
4. Print a per-year status summary.

Launch Navisworks → **Add-Ins ribbon tab** → **AutoNAV**.

### Why .cmd + .ps1 instead of a .exe?

The installer was previously an `.exe`. Windows SmartScreen and most browsers flag unsigned Windows executables as "potentially dangerous" because they can't verify a publisher signature. A plain-text PowerShell script with a small `.cmd` wrapper gets no such warning on download — they're text files, not executable binaries.

Behaviour is identical to the old `.exe`: same files written, same locations, same UAC elevation. The per-version DLLs are embedded inside `Install-AutoNAV.ps1` as base64 strings, so it's still a "single-script-plus-launcher" download with no separate `.zip` extraction.

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
