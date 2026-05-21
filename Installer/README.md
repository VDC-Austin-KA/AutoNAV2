# Installer (Go source)

Go module that produces **`Distributable/AutoNAV-Installer.exe`** — the single-file Windows installer handed to coworkers.

## What this builds

A ~2 MB Windows console `.exe` (PE32+ x86-64) that:

1. Self-elevates via UAC on launch.
2. Closes Navisworks if running.
3. Detects each installed Navisworks Manage 2024 / 2025 / 2026 / 2027.
4. For each detected version, writes the **matching per-version** `AutoNAV.dll` + `AutoNAV.addin` into `C:\ProgramData\Autodesk\Navisworks Manage <year>\Plugins\AutoNAV\` (backing up any existing files first).
5. Prints a summary and pauses.

The per-version plugin payload is embedded into the `.exe` at compile time via Go's [`embed`](https://pkg.go.dev/embed) directive, so the .exe is fully self-contained — no internet, no separate file copies on the target machine.

## Payload layout

The installer embeds four separate DLL/.addin pairs, one per supported Navisworks version:

```
Installer/payload/
  2024/AutoNAV.dll      <-- compiled against Navisworks 2024 API
  2024/AutoNAV.addin
  2025/AutoNAV.dll      <-- compiled against Navisworks 2025 API
  2025/AutoNAV.addin
  2026/AutoNAV.dll      <-- compiled against Navisworks 2026 API
  2026/AutoNAV.addin
  2027/AutoNAV.dll      <-- compiled against Navisworks 2027 API
  2027/AutoNAV.addin
```

`main.go` selects `payload/<year>/AutoNAV.dll` at install time based on which Navisworks version is found in `C:\Program Files\Autodesk\`.

## Building

### Full multi-version build (recommended)

```powershell
# From the repo root, on a Windows box with MSBuild + Go + Navisworks 2024/25/26/27 installed:
.\Distributable\Build-MultiVersion.ps1
```

This:
1. For each Navisworks version present on the build machine, invokes `MSBuild AutoNAV.csproj /p:Configuration=Release-NW<year> /p:Platform=x64` to compile a DLL specifically against that year's API DLLs.
2. Copies each built DLL into `Installer\payload\<year>\` (and mirrors into `Distributable\AutoNAV_v3.0.0\Plugin\<year>\` for the classic .bat installer).
3. Cross-compiles `AutoNAV-Installer.exe` with all four DLLs embedded.

### Quick single-DLL build (for testing)

If you only have one Navisworks version installed and want a working .exe quickly:

```powershell
.\Distributable\Build-Installer-EXE.ps1
```

This replicates a single `Distributable\AutoNAV.dll` into all four `payload\<year>\` subfolders before linking. Functional, but the same DLL will be installed regardless of which Navisworks year the target machine has.

### From Linux / macOS (CI or cross-build, payloads already staged)

```sh
cd Installer
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -ldflags="-s -w" -o ../Distributable/AutoNAV-Installer.exe .
```

## Build matrix

| Output                       | Size    | Build requirement                                       | Per-version DLLs?      |
| ---------------------------- | ------- | ------------------------------------------------------- | ---------------------- |
| `AutoNAV-Installer.exe`      | ~2 MB   | Go (link step) + MSBuild on Windows (proper per-version) | Yes (via embed)        |
| `AutoNAV-Installer.cmd`      | ~140 KB | None (pure Windows)                                     | No — single DLL only   |
| Classic `Install_AutoNAV.bat` | n/a    | None                                                    | Yes (reads `Plugin\<year>\`) |
