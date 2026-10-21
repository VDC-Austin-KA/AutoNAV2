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
1. Remove AutoNAV copies left by earlier installers (`%ProgramData%` / `%APPDATA%` `Autodesk\ApplicationPlugins\AutoNAV.bundle`, and `Plugins\AutoNAV` under `%ProgramData%`, `%APPDATA%` and `%LOCALAPPDATA%` `\Autodesk\Navisworks Manage <year>`). Navisworks loads plugins from those folders too, so an old copy there would be loaded instead of the new one. The AutoNAV window header shows the version that actually loaded.
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

Five tabs: **1. Setup** (Functions 1–3), **2. Clash Tests** (Function 4), **3. Group Clashes** (Functions 5–7), **4. Rename**, **5. AutoNAVismate**.

| Function | Purpose |
|---|---|
| 1 | One search set per loaded model in `1. DISCIPLINES`, selecting that whole model and nothing else. The search is *Item Name contains* the shortest unique tail of the model's name **including the extension** (`ARCH_L01_PAGE.nwc` / `ARCH_L01_HOK.nwc` → `PAGE.nwc` / `HOK.nwc`; `A.nwc` → `A.nwc`, never a bare `A`). The set is named without the extension (`PAGE`). If even the full name sits inside another model's name, the search is *Name equals* the full name. The discipline dictionary (ARCH, STR, MECH…) only tags each set for Function 2 defaults and Function 4 ordering. |
| 2 | Element-property search sets in `2. CLASH SETS/<model set>/`. Every set's search scope is its model's Function 1 set, so *Walls* only finds walls in that model; the summary flags any set saved without that scope. |
| 3 | Custom search sets from any property value in a model, scoped the same way. |
| 4 | Generate every clash test pair from `2. CLASH SETS` and auto-run them. |
| Group Clashes (5 + 6) | Per test: existing groups are kept; loose clashes touching a Walls / Floors set go into **Walls** / **Floors** first; the rest are grouped by nearest grid intersection + clashed set pair and named `{Month}/{Day}_{Level}_{Area} \| {TestName} - {SelectionA} vs {SelectionB} {#}`. Single clashes and clashes with no grid stay loose unless there are more than the limit (default 15, editable), in which case they're grouped by clashed set pair as `{Month}/{Day}_{TestName} - {SelectionA} vs {SelectionB} {#}`. Walls / Floors groups are only broken up when *Ungroup existing Walls / Floors groups* is ticked. |
| 5 | Walls / Floors only (the first step above), keeping existing groups. |
| 7 | Manual grouping modes (primary + sub-grouping dropdowns). *Selection A / B* group and name by the clash test's search set the clashed item belongs to (e.g. "Walls"), falling back to the element only when it's in none of them. |
| Templates | The naming list has presets with and without `{Level}`, plus **Custom** (type your own in the box below it). The fallback template is an editable list of the same presets. Token buttons insert any of `{Month} {Day} {Year} {Time} {Level} {Area} {TestName} {SelectionA} {SelectionB} {ClashCount} {Status} {GroupIndex} {#}` into the box you last clicked. |
| **AutoNAVismate** | One button: 1 → 2 → 4 → Group Clashes with the defaults. |

## Model sources

AutoNAV is built around Navisworks exports (`.nwc`), and also works with:

| Source | How AutoNAV handles it |
|---|---|
| `.nwc` / `.nwd` exports | One live search set per model on its Selection Tree name (see Function 1). |
| Native Revit (`.rvt`) appended directly | Same as `.nwc`, with `.rvt` in the search value. |
| Cloud-hosted models (Autodesk Docs / Forma, incl. Model Coordination) | Same, using the Selection Tree name. If that name is a GUID / URN rather than a file name, the model gets a fixed set of that model (named from its file name), listed in Function 1's summary. |

Models opened from Model Coordination load Autodesk's cloud (SVF) representation, which has no **Element** tab ([Autodesk article](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Revit-model-element-properties-appear-to-be-missing-in-Navisworks.html)). When Function 2's chosen property (e.g. *Element → System Abbreviation*) finds nothing, it looks for the same property under whichever tab holds it; category values such as "Revit Walls" are named "Walls" so Function 5 still finds Walls / Floors. Function 3 lists the properties those models actually have. When the models carry no grids, Group Clashes leaves clashes loose or, above the limit, groups them with the fallback template (Function 7's grid / level modes use "No Grid intersection" / "No Level").

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
