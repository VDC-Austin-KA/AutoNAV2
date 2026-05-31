# AutoNAV

Automated Design Coordination plugin for Autodesk Navisworks Manage 2024 / 2025 / 2026 / 2027.

## Install

Download **`AutoNAV-Plugin.zip`** (~225 KB) and extract anywhere. The folder will contain:

```
AutoNAV-Plugin/
    Install.cmd          ← run this
    Uninstall.cmd
    README.txt
    V24/AutoNAV.dll      ← for Navisworks Manage 2024
    V24/AutoNAV.addin
    V25/…                ← for 2025
    V26/…                ← for 2026
    V27/…                ← for 2027
```

Right-click **`Install.cmd`** → **Run as administrator** → confirm UAC. Done.

That's it — launch Navisworks → **Add-Ins ribbon tab** → **AutoNAV**.

### Why a ZIP and not a single .exe / .cmd

Earlier releases shipped a single self-extracting `.cmd` and a NSIS `.exe`. Both reliably trip Windows Defender's heuristics for "self-extracting installer" — the same pattern is used by malware droppers (embedded base64 payload → `certutil -decode` → drop binaries → run). Defender flags it as a virus even though the contents are benign, and there's no honest fix short of paying for a code-signing certificate (~$200–700/year from DigiCert / Sectigo / Comodo).

The ZIP approach sidesteps the problem entirely: the `.cmd` inside it does *only* `mkdir` and `copy` (the exact operations you'd do by hand in File Explorer), so it doesn't match any malware fingerprints. The DLLs travel as plain files. No base64, no `certutil`, no embedded executables. Standard Windows ZIPs aren't scanned heuristically the same way self-extracting installers are.

### Fully manual install (no script at all)

For each Navisworks Manage 2024 / 2025 / 2026 / 2027 you have:

1. Create `C:\Program Files\Autodesk\Navisworks Manage <year>\Plugins\AutoNAV\` (you'll need admin rights).
2. Copy `AutoNAV.dll` and `AutoNAV.addin` from the matching `V<yy>/` folder in the ZIP into that `AutoNAV\` folder.

Restart Navisworks. Done.

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
