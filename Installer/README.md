# Installer (Go source)

Go module that produces **`Distributable/AutoNAV-Installer.exe`** — the single-file Windows installer handed to coworkers.

## What this builds

A ~2 MB Windows console `.exe` (PE32+ x86-64) that:

1. Self-elevates via UAC on launch.
2. Closes Navisworks if running.
3. Detects each installed Navisworks Manage 2024 / 2025 / 2026 / 2027.
4. Writes the embedded `AutoNAV.dll` + `AutoNAV.addin` into each version's `C:\ProgramData\Autodesk\Navisworks Manage <year>\Plugins\AutoNAV\` (backing up any existing files first).
5. Prints a summary and pauses.

The plugin payload (`AutoNAV.dll` + `AutoNAV.addin`) is embedded into the `.exe` at compile time via Go's [`embed`](https://pkg.go.dev/embed) directive, so the .exe is fully self-contained.

## Building

### From Windows (recommended for the developer's normal workflow)

```powershell
# From the repo root:
.\Distributable\Build-Installer-EXE.ps1
```

The script copies `Distributable\AutoNAV.dll` and `Distributable\AutoNAV.addin` into `Installer\payload\`, then runs `go build` with `GOOS=windows GOARCH=amd64` to produce `Distributable\AutoNAV-Installer.exe`.

Requires Go (https://go.dev/dl/). If Go isn't available, fall back to `Build-Installer.ps1` which produces the `.cmd` equivalent with no build tooling at all.

### From Linux / macOS (CI or cross-build)

```sh
cp Distributable/AutoNAV.dll   Installer/payload/AutoNAV.dll
cp Distributable/AutoNAV.addin Installer/payload/AutoNAV.addin
cd Installer
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -ldflags="-s -w" -o ../Distributable/AutoNAV-Installer.exe .
```

## Why Go?

Three single-file installer formats are available in this repo; choose the one that fits the distribution channel:

| Format                       | Size    | Build requirement        | When to use                                                                      |
| ---------------------------- | ------- | ------------------------ | -------------------------------------------------------------------------------- |
| `AutoNAV-Installer.exe`      | ~2 MB   | Go on the build machine  | Polished single .exe for GitHub Releases / sharing with non-technical coworkers |
| `AutoNAV-Installer.cmd`      | ~140 KB | None (pure Windows)      | When Go isn't available or for the smallest possible download                    |
| Classic multi-file installer | n/a     | None                     | IT-admin / scripted deployments where each file is wanted separately             |

All three install the same payload into the same locations.
