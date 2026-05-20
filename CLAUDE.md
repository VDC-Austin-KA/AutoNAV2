# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**AutoNAV2** is an Autodesk Navisworks plugin (v3.0.0) that automates VDC (Virtual Design and Construction) coordination workflows. It is a C# .NET Framework 4.8 WPF add-in that integrates with Navisworks Manage to automate search set creation, clash test generation, and clash result grouping.

## Build Commands

```powershell
# Release build (x64)
msbuild AutoNAV\AutoNAV.csproj /p:Configuration=Release /p:Platform=x64

# Debug build (x64)
msbuild AutoNAV\AutoNAV.csproj /p:Configuration=Debug /p:Platform=x64
```

Output lands in `AutoNAV\bin\Release\AutoNAV.dll` or `AutoNAV\bin\Debug\AutoNAV.dll`.

There are no automated tests in this codebase.

## Navisworks API Dependency

The project auto-detects the Navisworks installation path at build time (prefers Manage 2025, falls back to 2024):

```
C:\Program Files\Autodesk\Navisworks Manage 2025\
C:\Program Files\Autodesk\Navisworks Manage 2024\
```

Key assemblies (referenced as `Private=False` — not copied to output):
- `Autodesk.Navisworks.Api.dll`
- `Autodesk.Navisworks.Clash.dll`
- `Autodesk.Navisworks.ComApi.dll`
- `Autodesk.Navisworks.Interop.ComApi.dll`

## Plugin Installation

Copy `AutoNAV.dll` and `AutoNAV.addin` to the Navisworks plugins directory. The `.addin` file declares entry point `AutoNAV.PluginMain` and add-in metadata. The distributable package under `Distributable/` contains install/uninstall scripts.

## Architecture

### Entry Point
`PluginMain.cs` — `[Plugin("AutoNAV", "ACLP_VDC")]` decorated `AddInPlugin` subclass. `Execute()` opens the `MainWindow` as a modal dialog.

### UI Layer
`MainWindow.xaml` / `MainWindow.xaml.cs` — Single WPF window with all six functions exposed as button actions. Owns instances of `SearchSetGenerator` and `ClashTestGeneratorEngine`. Handles all UI event wiring and discipline checkbox panel construction.

### Core Classes

**`SearchSetGenerator.cs`** — Static-heavy class responsible for Functions 1, 2, and 3:
- **Function 1** (`GenerateFunction1SearchSets`): Reads all loaded model filenames, computes minimal unique `CONTAINS` patterns per discipline group (via `ComputeDisciplinePatterns`), and creates search sets under the `"1. DISCIPLINES"` folder in the Navisworks Selection Sets tree.
- **Function 2** (`GenerateFunction2SearchSets`): For each discipline, enumerates unique BIM property values (e.g. `Element/Category`) from the discipline's models and creates child search sets under `"2. CLASH SETS\<Discipline>"`.
- **Function 3** (`GenerateCustomSearchSets`): Same as Function 2 but the user picks an arbitrary property category and name from dropdowns.

**`ClashTestGeneratorEngine.cs`** — Functions 4 and 5:
- **Function 4** (`GenerateClashTests`): Reads disciplines from `"1. DISCIPLINES"` and clash set folders from `"2. CLASH SETS"`, then creates all pairwise `ClashTest` entries in Clash Detective using `SelectionSource` objects built from each discipline's search sets.
- **Function 5** (`RunClashTestsAndGroupResults`): Runs clash tests and groups results by Walls/Floors membership.

**`ClashGrouper.cs`** — Function 6 and general clash result grouping:
- **`GroupClashes`**: Groups clash results for a single test by one of 15 `GroupingMode` values (Level, GridIntersection, SelectionA/B, ModelA/B, Status, AssignedTo, ApprovedBy, File, Layer, First, Last, LastUnique, WallsAndFloors).
- **`GroupAllTestsByWallsAndFloors`** (Function 6): Iterates all clash tests and groups each by Walls/Floors membership, leaving non-matching results ungrouped for downstream tools (Sherlock Distill).
- Walls/Floors detection uses `Search.FindAll()` to build `HashSet<ModelItem>` for each search set (with descendants pre-expanded), then checks each clash result item for set membership — this is intentional and avoids unreliable property string matching.

**`ClashResultGrouper.cs`** — Additional grouping utilities (supplementary to `ClashGrouper`).

### Selection Set Folder Convention
The plugin operates on two top-level folders in the Navisworks Selection Sets pane:
- `"1. DISCIPLINES"` — one `SelectionSet` per discipline (Function 1 output)
- `"2. CLASH SETS"` — one subfolder per discipline, each containing category `SelectionSet` entries (Functions 2/3 output)

Functions 4–6 require both folders to exist (Functions 1–3 must be run first).

### Discipline Pattern Algorithm (`ComputeDisciplinePatterns`)
Parses model filenames to find the shortest separator-wrapped segment (e.g. `-MP-`) that:
1. Appears in ALL files of a discipline group
2. Does NOT appear in any other group's files

Falls back to adjacent segment pairs, then triples, then the full discipline string. Separator is auto-detected by majority vote of `-` vs `_` across all filenames. Level codes (e.g. `L06`, `B01`, `RF01`) are filtered out during grouping.
