# AutoNAV multi-version bundle layout

The plugin is distributed as an Autodesk **ApplicationPlugins bundle**, which is
the standard format for shipping one .NET assembly per supported Navisworks
release inside a single discoverable package.

## End-user install path

```
%APPDATA%\Autodesk\ApplicationPlugins\AutoNAV.bundle\
  PackageContents.xml              <-- declares which version loads which DLL
  Contents\
    V24\AutoNAV.dll                <-- compiled against Navisworks 2024 API
    V24\AutoNAV.addin
    V25\AutoNAV.dll                <-- compiled against Navisworks 2025 API
    V25\AutoNAV.addin
    V26\AutoNAV.dll                <-- compiled against Navisworks 2026 API
    V26\AutoNAV.addin
    V27\AutoNAV.dll                <-- compiled against Navisworks 2027 API
    V27\AutoNAV.addin
```

`%APPDATA%` resolves to `C:\Users\<username>\AppData\Roaming`, so the full
path is per-user. **No admin / UAC elevation is required.**

For an all-users install (machine-wide), the installer can drop the bundle into
`%PROGRAMDATA%\Autodesk\ApplicationPlugins\AutoNAV.bundle\` instead — that
location does require admin. The PowerShell installer exposes `-AllUsers` for
this.

## How Navisworks finds it

At startup, Navisworks Manage 2024–2027 scans both ApplicationPlugins
directories. For each `*.bundle` folder it reads the `PackageContents.xml`,
matches each `<Components>` block against the running version via
`RuntimeRequirements/SeriesMin/SeriesMax`, and loads the DLL named in
`<ComponentEntry ModuleName="...">`. The matching version's `.addin` sits
beside the DLL and is loaded by the standard add-in mechanism.

## Source

### `AutoNAV/AutoNAV.csproj` — per-version build configurations

```
msbuild AutoNAV\AutoNAV.csproj /p:Configuration=Release-NW2024 /p:Platform=x64
msbuild AutoNAV\AutoNAV.csproj /p:Configuration=Release-NW2025 /p:Platform=x64
msbuild AutoNAV\AutoNAV.csproj /p:Configuration=Release-NW2026 /p:Platform=x64
msbuild AutoNAV\AutoNAV.csproj /p:Configuration=Release-NW2027 /p:Platform=x64
```

Each configuration pins `$(NWPath)` to the matching `C:\Program Files\Autodesk\Navisworks Manage <year>\`
and outputs to `AutoNAV\bin\Release-NW<year>\AutoNAV.dll`.

### `Installer/payload/AutoNAV.bundle/` — embedded bundle source tree

`PackageContents.xml` lives at the bundle root. The four per-version DLLs land
in `Contents/V24..V27/`. The Go installer in `Installer/main.go` uses
`//go:embed all:payload/AutoNAV.bundle` to pack the entire tree into the .exe;
at install time it walks the embedded FS and reproduces it under
`%APPDATA%\Autodesk\ApplicationPlugins\AutoNAV.bundle\`.

## Single-command Windows build

```powershell
.\Distributable\Build-MultiVersion.ps1
```

This runs MSBuild for every Navisworks year installed on the build box,
stages each DLL into `Installer\payload\AutoNAV.bundle\Contents\V##\`,
mirrors the same tree into `Distributable\AutoNAV_v3.0.0\AutoNAV.bundle\`
for the classic distribution path, then `go build`s the final
`Distributable\AutoNAV-Installer.exe`.

Requirements on the build machine:
- MSBuild (Visual Studio 2022+ or Build Tools)
- Go 1.21+ on PATH
- At least one Navisworks Manage 2024–2027 installed (you can only build
  DLLs against versions you have the SDK / API DLLs for)

## Quick single-DLL build (when you only have one Navisworks)

```powershell
.\Distributable\Build-Installer-EXE.ps1
```

This replicates `Distributable\AutoNAV.dll` into all four `Contents\V##\`
subfolders before linking. Useful for smoke-testing the installer flow
without a full multi-Navisworks build environment. The resulting installer
still uses the bundle layout and PackageContents.xml — it just ships the
same DLL for every version.

## Distribution options

| Output                                     | Best for                                                                  |
| ------------------------------------------ | ------------------------------------------------------------------------- |
| `Distributable\AutoNAV-Installer.exe`      | One-file hand-off to coworkers. Double-click, drops bundle into %APPDATA%. |
| `Distributable\AutoNAV-Installer.cmd`      | Same payload, pure-Windows .cmd (no Go); useful when Go unavailable.       |
| `Distributable\AutoNAV_v3.0.0\` folder     | IT-admin / scripted deploys. Run `Install_AutoNAV.bat` from inside.        |

## Uninstall

```powershell
# Per-user:
Remove-Item "$env:APPDATA\Autodesk\ApplicationPlugins\AutoNAV.bundle" -Recurse -Force

# Or run:
.\Distributable\Install-AutoNAV.ps1 -Uninstall
```

The PowerShell uninstaller also cleans up any legacy installs from earlier
versions that wrote to `C:\ProgramData\Autodesk\Navisworks Manage <year>\Plugins\AutoNAV\`.
