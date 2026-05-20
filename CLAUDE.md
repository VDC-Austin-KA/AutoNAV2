# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "AutoNAV\AutoNAV.csproj" /p:Configuration=Release /p:Platform=x64 /t:Clean,Build /v:minimal
```

Output lands in `AutoNAV\bin\Release\AutoNAV.dll`. After a successful build, copy the DLL (and PDB) into both distributable locations:
- `Distributable\AutoNAV.dll`
- `Distributable\AutoNAV_v3.0.0\Plugin\AutoNAV.dll`

## Plugin Architecture

AutoNAV is a Navisworks Manage add-in targeting .NET Framework 4.8 / x64 / WPF.

**Entry point**: `PluginMain.cs` — decorated with `[Plugin]` and `[AddInPlugin(AddInLocation.AddIn)]`. Navisworks discovers and loads it at startup.

**Main window**: `MainWindow.xaml` / `MainWindow.xaml.cs` — single WPF dialog with six numbered function sections. Each `OnFunctionNClick` handler drives one automation workflow.

**Core modules**:
| File | Responsibility |
|---|---|
| `SearchSetGenerator.cs` | Functions 1–3: create/update Navisworks Selection Sets |
| `ClashTestGeneratorEngine.cs` | Function 4: generate clash tests from selection sets |
| `ClashResultGrouper.cs` | Function 5: group clash results by element category |
| `ClashGrouper.cs` | Function 6: group clash results by Walls/Floors per discipline |

## Plugin Installation Paths

Navisworks discovers third-party plugins from this directory (consistent across 2024–2027):

```
C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\
```

Both `AutoNAV.dll` and `AutoNAV.addin` **must** be in the same `AutoNAV\` subfolder. The `.addin` XML references `<AssemblyFile>AutoNAV.dll</AssemblyFile>` as a relative path.

The `AddIns\` folder under `Program Files\Autodesk\...` is an Autodesk-internal path and should not be used for third-party plugins.

## Selection Set Folder Convention

Functions 1–3 create selection sets organized under numbered top-level folders:
- `1. DISCIPLINE SETS` — Function 1 output (name-contains search)
- `2. CLASH SETS` — Function 2 output (category-equals search)
- `3. REFINED SETS` — Function 3 output

Function 6 (`ClashGrouper.cs`) reads discipline sets from `2. CLASH SETS` to know which model items belong to each discipline's Walls and Floors.

## Discipline Pattern Detection

`SearchSetGenerator` detects discipline names from model item names using a prefix/substring approach. Function 6 (`ClashGrouper.cs`) maps "DiscA vs DiscB" clash test names to those same discipline keys to scope which Walls/Floors sets to merge for each test.

## Critical Navisworks API Pattern — Clash Grouping

`TestsAddCopy(parent, group)` does **not** deep-copy an in-memory group's children. The correct pattern for adding a group with results is:

```csharp
// 1. Add empty shell
docClash.TestsData.TestsAddCopy(liveTest, new ClashResultGroup { DisplayName = name });

// 2. Get live reference (just added, last child)
ClashResultGroup liveGroup = null;
for (int i = liveTest.Children.Count - 1; i >= 0; i--)
{
    if (liveTest.Children[i] is ClashResultGroup crg) { liveGroup = crg; break; }
}

// 3. Add items one-by-one to the live group
foreach (SavedItem child in sourceGroup.Children)
{
    if (child is ClashResult cr)
        docClash.TestsData.TestsAddCopy(liveGroup, cr);
}
```

## Branching Strategy

- `main` — stable, released builds only
- `feature/*` — active development; merge to main when verified working
- Current active branch: `feature/fix-function6-grouping`
