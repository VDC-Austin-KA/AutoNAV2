# AutoNAV multi-version build layout

This document explains how the repo produces a *properly-versioned* AutoNAV
plugin for Navisworks Manage 2024 / 2025 / 2026 / 2027 and packages them all
into one Windows installer .exe.

## Why per-version DLLs

Each Navisworks Manage release ships its own `Autodesk.Navisworks.Api.dll`
(plus `Autodesk.Navisworks.Clash.dll`, etc.) with a different assembly
version. A plugin built referencing the 2024 API can fail to load in 2027
when the assembly version on disk doesn't match what the DLL was bound
against. The fix is straightforward: compile the plugin separately against
each Navisworks year's API DLLs, then ship the matching DLL to the matching
Navisworks install.

## Source: `AutoNAV.csproj` configurations

```
msbuild AutoNAV\AutoNAV.csproj /p:Configuration=Release-NW2024 /p:Platform=x64
msbuild AutoNAV\AutoNAV.csproj /p:Configuration=Release-NW2025 /p:Platform=x64
msbuild AutoNAV\AutoNAV.csproj /p:Configuration=Release-NW2026 /p:Platform=x64
msbuild AutoNAV\AutoNAV.csproj /p:Configuration=Release-NW2027 /p:Platform=x64
```

Each configuration pins `$(NWPath)` to the matching `C:\Program Files\Autodesk\Navisworks Manage <year>\`
and outputs to `AutoNAV\bin\Release-NW<year>\AutoNAV.dll`.

The pre-existing `Release|x64` config is preserved for the developer
auto-detect / single-build workflow described in `CLAUDE.md`.

## Staging: per-version subfolders

The build outputs are staged into two locations:

```
Installer/payload/
  2024/AutoNAV.dll      <-- embedded in AutoNAV-Installer.exe
  2024/AutoNAV.addin
  2025/AutoNAV.dll
  2025/AutoNAV.addin
  2026/AutoNAV.dll
  2026/AutoNAV.addin
  2027/AutoNAV.dll
  2027/AutoNAV.addin

Distributable/AutoNAV_v3.0.0/Plugin/
  2024/AutoNAV.dll      <-- read by Install_AutoNAV.bat (classic distribution)
  2024/AutoNAV.addin
  2024/AutoNAV.pdb
  2025/...
  2026/...
  2027/...
```

The `AutoNAV.addin` is byte-identical across years because it references the
DLL by relative path (`<AssemblyFile>AutoNAV.dll</AssemblyFile>`).

## Packaging: `AutoNAV-Installer.exe`

The Go program in `Installer/main.go` uses `//go:embed` to bake all four
DLL/addin pairs into a single Windows .exe. At install time it:

1. Self-elevates via UAC.
2. Closes Navisworks if running.
3. For each Navisworks year detected under `C:\Program Files\Autodesk\`,
   writes `payload/<year>/AutoNAV.dll` + `AutoNAV.addin` into
   `C:\ProgramData\Autodesk\Navisworks Manage <year>\Plugins\AutoNAV\`.
4. Skips versions not installed.

## One-command full build (Windows)

```powershell
.\Distributable\Build-MultiVersion.ps1
```

This invokes MSBuild for every Navisworks year installed on the build box,
stages each into both locations above, then runs `go build` to produce
`Distributable\AutoNAV-Installer.exe`.

Requirements on the build machine:
- MSBuild (Visual Studio 2022+ or Build Tools)
- Go 1.21+ on PATH
- At least one Navisworks Manage 2024–2027 installed (you can only build
  DLLs against versions you have installed)

## Quick single-DLL build (when you only have one Navisworks installed)

```powershell
.\Distributable\Build-Installer-EXE.ps1
```

This replicates a single `Distributable\AutoNAV.dll` into all four
`payload\<year>\` subfolders before linking. Useful for smoke-testing the
installer flow without a full multi-Navisworks build environment.

## Distribution options

| Output                                     | Best for                                                              |
| ------------------------------------------ | --------------------------------------------------------------------- |
| `Distributable\AutoNAV-Installer.exe`      | Hand-off to coworkers: one double-click, self-elevates, one file.     |
| `Distributable\AutoNAV-Installer.cmd`      | Same as above but pure-Windows (no Go); single DLL only.              |
| `Distributable\AutoNAV_v3.0.0\` folder     | IT-admin / scripted deploys; explicit per-version DLLs under `Plugin\<year>\`. |
