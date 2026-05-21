# Installer payload (per Navisworks version)

Each `<year>/` subfolder holds the AutoNAV plugin built against that
year's Navisworks API. The installer (`../main.go`) embeds these files
at compile time via `//go:embed` and copies the correct subfolder's
files into the matching `C:\ProgramData\Autodesk\Navisworks Manage <year>\Plugins\AutoNAV\`.

```
payload/
  2024/AutoNAV.dll      <-- built referencing C:\Program Files\Autodesk\Navisworks Manage 2024\Autodesk.Navisworks.Api.dll
  2024/AutoNAV.addin
  2025/AutoNAV.dll      <-- built referencing the 2025 API DLL
  2025/AutoNAV.addin
  2026/AutoNAV.dll      <-- built referencing the 2026 API DLL
  2026/AutoNAV.addin
  2027/AutoNAV.dll      <-- built referencing the 2027 API DLL
  2027/AutoNAV.addin
```

## How the per-version DLLs get here

Run `Distributable\Build-MultiVersion.ps1` on a Windows box that has
MSBuild + the Navisworks versions you want to target installed. For
each year present, the script invokes:

```
msbuild AutoNAV\AutoNAV.csproj /p:Configuration=Release-NW<year> /p:Platform=x64
```

and copies `AutoNAV\bin\Release-NW<year>\AutoNAV.dll` (plus the shared
`AutoNAV.addin`) into the matching `payload\<year>\` subfolder.

## "But the DLLs in each subfolder are identical!"

If you've never run the multi-version build, the four `AutoNAV.dll`
files are byte-identical copies of whatever single-version build was
last placed in `Distributable\AutoNAV.dll`. That's only a placeholder so
`go build` (which requires non-empty embed targets) succeeds. It will
load in most Navisworks versions due to Autodesk's binary compatibility
across some releases, but to be officially correct, rebuild each
per-version DLL with `Build-MultiVersion.ps1` before shipping.
