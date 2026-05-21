# Navisworks Manage Plugin Requirements (2024 - 2027)

A developer-facing reference for what files a Navisworks Manage plugin needs, where each file lives on a user's machine, and what changes between Navisworks Manage 2024, 2025, 2026, and 2027. Two registration tracks are covered:

- **Track A - `AddInPlugin`** - simplest path; button is auto-placed under the built-in **Add-Ins** ribbon tab. (This is what AutoNAV uses today; see `AutoNAV/PluginMain.cs`.)
- **Track B - `CommandHandlerPlugin`** - full custom ribbon tab with XAML-defined panels, buttons, icons, and localized strings.

---

## 1. Which approach do I pick?

| Aspect                              | Track A: `AddInPlugin`                                  | Track B: `CommandHandlerPlugin`                          |
| ----------------------------------- | ------------------------------------------------------- | -------------------------------------------------------- |
| Where the button shows up           | Auto-generated **Add-Ins** ribbon tab (one button)      | Your own ribbon tab with your panels & buttons           |
| Files required                      | DLL + `.addin`                                          | DLL + `.addin` + XAML + `en-US\*.name` + `Images\*.png`  |
| Custom icon                         | No (uses default Navisworks add-in icon)                | Yes (PNG, referenced from XAML)                          |
| Multiple buttons / split buttons    | No - one entry point per plugin class                   | Yes - any number of buttons, split buttons, panels       |
| Localized display names / tooltips  | Limited to `[Plugin]` attribute properties              | Full localization via `en-US\<Name>.name` resource file  |
| Complexity                          | Lowest - 2 files                                        | Moderate - 5+ files with strict folder layout            |
| AutoNAV today                       | **Yes** - `[AddInPlugin(AddInLocation.AddIn)]`          | No                                                       |

**Rule of thumb:** start with Track A. Only move to Track B when you need a dedicated tab, multiple buttons, or branded icons.

---

## 2. Version compatibility matrix

| Attribute                              | Nw 2024                                              | Nw 2025                                              | Nw 2026                                              | Nw 2027                                              |
| -------------------------------------- | ---------------------------------------------------- | ---------------------------------------------------- | ---------------------------------------------------- | ---------------------------------------------------- |
| Released                               | Apr 2023                                             | Apr 2024                                             | Mar 2025                                             | Mar 31 2026                                          |
| Target .NET                            | .NET Framework 4.8                                   | .NET Framework 4.8                                   | .NET Framework 4.8                                   | .NET Framework 4.8                                   |
| Platform                               | x64                                                  | x64                                                  | x64                                                  | x64                                                  |
| `Autodesk.Navisworks.Api.dll` location | `C:\Program Files\Autodesk\Navisworks Manage 2024\`  | `C:\Program Files\Autodesk\Navisworks Manage 2025\`  | `C:\Program Files\Autodesk\Navisworks Manage 2026\`  | `C:\Program Files\Autodesk\Navisworks Manage 2027\`  |
| `Autodesk.Navisworks.Clash.dll`        | same folder                                          | same folder                                          | same folder                                          | same folder                                          |
| App Manager UI (Home tab)              | **Introduced** - shows load/error state per plugin   | Yes                                                  | Yes                                                  | Yes                                                  |
| Plugin model                           | `AddInPlugin` / `CommandHandlerPlugin` / `DockPanePlugin` (unchanged)                                                                                                                                                  |||
| `.addin` schema                        | `Addin Version="1.0"` (unchanged across all four versions)                                                                                                                                                              |||
| Notable behavior change                | -                                                    | -                                                    | **Lazy property model** - `PropertyCategory` / `DataProperty` access on `ModelItem` is now deferred; do not cache | **Clash Detective UI rewrite** - new Rules/Results/Reports windows; public Clash API still works |

**Plugin binary compatibility:** Each Navisworks release ships its own `Autodesk.Navisworks.Api.dll`. The high-level plugin surface (`AddInPlugin`, `CommandHandlerPlugin`, `[Plugin]`, `[AddInPlugin]`, `Execute(params string[])`) is stable across 2024 - 2027, so a single compiled DLL can serve all four versions **as long as you only touch the surface API**. If your code dives into specific subsystems (Clash internals, property model, COM interop), build one DLL per version and select via project conditionals - see `AutoNAV/AutoNAV.csproj` (look for the per-version `PropertyGroup Condition` blocks).

---

## 3. Track A - `AddInPlugin` (Add-Ins ribbon tab)

### 3a. Files

| File                        | Required | Purpose                                                                |
| --------------------------- | -------- | ---------------------------------------------------------------------- |
| `<PluginName>.dll`          | Yes      | Compiled assembly. Filename base **must equal** the parent folder name |
| `<PluginName>.addin`        | Yes      | XML manifest; tells Navisworks which class to instantiate              |
| `<PluginName>.pdb`          | Optional | Debug symbols. Useful in dev; safe to omit in production               |

Folder layout on disk (one plugin, one folder):

```
<PluginName>\
├── <PluginName>.dll
├── <PluginName>.addin
└── <PluginName>.pdb       (optional)
```

### 3b. Required C# attributes

```csharp
using Autodesk.Navisworks.Api.Plugins;

namespace MyCompany.MyPlugin
{
    [Plugin(
        "MyPluginId",          // Plugin Id (must be unique across all plugins; matches .addin FullClassName context)
        "MyDevId",             // Developer Id (your reverse-DNS or short code)
        DisplayName = "My Plugin",
        ToolTip     = "Short tooltip shown on the Add-Ins button")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class PluginMain : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            // launch UI, do work, return 0 on success, non-zero on error
            return 0;
        }
    }
}
```

Real-world example: `AutoNAV/PluginMain.cs` (`[Plugin("AutoNAV", "ACLP_VDC", ...)]` + `[AddInPlugin(AddInLocation.AddIn)]`).

### 3c. `.addin` XML manifest

```xml
<?xml version="1.0" encoding="utf-8"?>
<Addin Version="1.0">
  <CompanyName>Your Company</CompanyName>
  <AddInName>My Plugin</AddInName>
  <AddInDescription>What this plugin does, one sentence.</AddInDescription>
  <AddInVersion>1.0.0.0</AddInVersion>
  <FullClassName>MyCompany.MyPlugin.PluginMain</FullClassName>
  <AssemblyFile>MyPlugin.dll</AssemblyFile>
</Addin>
```

- `FullClassName` **must** match the namespace+class of the type decorated with `[Plugin]`.
- `AssemblyFile` is **relative** to the `.addin` file - the DLL must sit next to it.

Real-world example: `AutoNAV/AutoNAV.addin`.

### 3d. `AddInLocation` enum

| Value          | Button placement                                                              |
| -------------- | ----------------------------------------------------------------------------- |
| `AddIn`        | First "Tool Add-Ins 1" panel in the Add-Ins ribbon tab                        |
| `AddIn1`       | "Tool Add-Ins 1" panel (explicit)                                             |
| `AddIn2`       | "Tool Add-Ins 2" panel                                                        |
| `None`         | Hidden - plugin loads but has no UI entry; invoked programmatically only      |

### 3e. Install paths per version (Track A)

The recommended path for AutoNAV-style loose plugins is the **per-machine ProgramData** path. Subfolder name must match DLL base name (case-insensitive).

| Version | Primary (per-machine, recommended)                                                  | Per-user fallback                                                       | Vendor-install path (don't write here from an installer)                       |
| ------- | ----------------------------------------------------------------------------------- | ----------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| Nw 2024 | `C:\ProgramData\Autodesk\Navisworks Manage 2024\Plugins\<PluginName>\`              | `%APPDATA%\Autodesk\Navisworks Manage 2024\Plugins\<PluginName>\`       | `C:\Program Files\Autodesk\Navisworks Manage 2024\Plugins\<PluginName>\`       |
| Nw 2025 | `C:\ProgramData\Autodesk\Navisworks Manage 2025\Plugins\<PluginName>\`              | `%APPDATA%\Autodesk\Navisworks Manage 2025\Plugins\<PluginName>\`       | `C:\Program Files\Autodesk\Navisworks Manage 2025\Plugins\<PluginName>\`       |
| Nw 2026 | `C:\ProgramData\Autodesk\Navisworks Manage 2026\Plugins\<PluginName>\`              | `%APPDATA%\Autodesk\Navisworks Manage 2026\Plugins\<PluginName>\`       | `C:\Program Files\Autodesk\Navisworks Manage 2026\Plugins\<PluginName>\`       |
| Nw 2027 | `C:\ProgramData\Autodesk\Navisworks Manage 2027\Plugins\<PluginName>\`              | `%APPDATA%\Autodesk\Navisworks Manage 2027\Plugins\<PluginName>\`       | `C:\Program Files\Autodesk\Navisworks Manage 2027\Plugins\<PluginName>\`       |

> **Folder-name rule:** the `<PluginName>` subfolder name MUST match the DLL base name (case-insensitive). `Plugins\AutoNAV\AutoNAV.dll` works; `Plugins\AutoNav-Foo\AutoNAV.dll` does NOT - Navisworks will silently skip the plugin and the Add-Ins tab will not appear.

---

## 4. Track B - `CommandHandlerPlugin` (custom ribbon tab)

### 4a. Files

| File                                    | Required | Purpose                                                                                                          |
| --------------------------------------- | -------- | ---------------------------------------------------------------------------------------------------------------- |
| `<PluginName>.dll`                      | Yes      | Compiled assembly                                                                                                |
| `<PluginName>.addin`                    | Yes      | XML manifest (same format as Track A)                                                                            |
| `<PluginName>.xaml`                     | Yes      | RibbonControl/RibbonTab/RibbonPanel layout - referenced by `[RibbonLayout]`                                      |
| `en-US\<PluginName>.xaml`               | Recommended | Localized copy of the layout. Navisworks looks here first for the active culture                              |
| `en-US\<PluginName>.name`               | Recommended | Plain-text resource file mapping Tab/Panel/Command Ids to localized DisplayName / ToolTip strings                |
| `Images\<icon>.png`                     | Yes      | Button icons. PNG, 16x16 (small) and 32x32 (large). Path is relative to the plugin folder and referenced in XAML |
| `<PluginName>.pdb`                      | Optional | Debug symbols                                                                                                    |

Folder layout on disk:

```
<PluginName>\
├── <PluginName>.dll
├── <PluginName>.addin
├── <PluginName>.xaml
├── en-US\
│   ├── <PluginName>.xaml      (localized layout - same schema, localized strings)
│   └── <PluginName>.name      (or *.txt) - DisplayName / ToolTip strings
└── Images\
    ├── ButtonOne_16.png
    ├── ButtonOne_32.png
    ├── ButtonTwo_16.png
    └── ButtonTwo_32.png
```

### 4b. Required C# attributes

```csharp
using Autodesk.Navisworks.Api.Plugins;

namespace MyCompany.MyPlugin
{
    [Plugin("MyCustomTab",  "MyDevId", DisplayName = "My Plugin")]
    [RibbonLayout("MyCustomTab.xaml")]   // filename of XAML, sits next to the DLL
    [RibbonTab("MyTabId",                // must match RibbonTab Id in the XAML
               DisplayName = "My Tab",
               LoadForCanExecute = false)]
    [Command("ButtonOne", DisplayName = "Run Function 1", ToolTip = "Description")]
    [Command("ButtonTwo", DisplayName = "Run Function 2", ToolTip = "Description")]
    public class PluginMain : CommandHandlerPlugin
    {
        public override int ExecuteCommand(string commandId, params string[] parameters)
        {
            switch (commandId)
            {
                case "ButtonOne": DoFunctionOne(); return 0;
                case "ButtonTwo": DoFunctionTwo(); return 0;
                default:          return 1;
            }
        }
    }
}
```

### 4c. RibbonLayout XAML

```xml
<RibbonControl xmlns="clr-namespace:Autodesk.Windows;assembly=AdWindows"
               xmlns:nw="clr-namespace:Autodesk.Navisworks.Gui.Roamer;assembly=RoamerFreeRibbon"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <RibbonTab Id="MyTabId" Title="My Tab">

    <RibbonPanel x:Uid="Panel_Main">
      <RibbonPanelSource>
        <nw:NWRibbonButton Id="ButtonOne"
                           ShowText="True"
                           Size="Large"
                           Orientation="Vertical"
                           LargeImage="Images/ButtonOne_32.png"
                           Image="Images/ButtonOne_16.png" />
        <nw:NWRibbonButton Id="ButtonTwo"
                           ShowText="True"
                           Size="Large"
                           Orientation="Vertical"
                           LargeImage="Images/ButtonTwo_32.png"
                           Image="Images/ButtonTwo_16.png" />
      </RibbonPanelSource>
    </RibbonPanel>

    <!-- Optional: split button example -->
    <RibbonPanel x:Uid="Panel_Tools">
      <RibbonPanelSource>
        <nw:NWRibbonSplitButton Id="ToolsSplit"
                                LargeImage="Images/Tools_32.png">
          <nw:NWRibbonButton Id="ToolA" Image="Images/ToolA_16.png" />
          <nw:NWRibbonButton Id="ToolB" Image="Images/ToolB_16.png" />
        </nw:NWRibbonSplitButton>
      </RibbonPanelSource>
    </RibbonPanel>

  </RibbonTab>
</RibbonControl>
```

Key points:

- The `RibbonTab Id` in XAML **must** equal the `[RibbonTab("MyTabId")]` argument in C#.
- Each `NWRibbonButton Id` **must** equal a `[Command("ButtonId")]` argument in C#.
- Image paths are **relative to the plugin folder** (i.e. relative to where the DLL lives).

### 4d. `en-US\<PluginName>.name` localized strings file

Plain text, one key=value per line. Used when XAML doesn't carry inline `Text="..."` / `ToolTip="..."` and as the localization source:

```
# Tab
MyTabId.DisplayName       = My Tab
MyTabId.ToolTip           = Custom tools tab

# Buttons
ButtonOne.DisplayName     = Function 1
ButtonOne.ToolTip         = Run function 1
ButtonOne.Description     = Long description shown in extended tooltip

ButtonTwo.DisplayName     = Function 2
ButtonTwo.ToolTip         = Run function 2
```

**String-resolution order Navisworks uses:**

1. Inline attribute in the XAML (highest priority)
2. Localized resource file `en-US\<PluginName>.name`
3. `DisplayName` / `ToolTip` properties on the C# `[Plugin]` / `[Command]` attributes (fallback)

### 4e. Install paths per version (Track B)

Identical root locations to Track A - the difference is the **contents** of the `<PluginName>\` folder (XAML + en-US + Images, in addition to DLL + .addin).

| Version | Per-machine install                                                                    |
| ------- | -------------------------------------------------------------------------------------- |
| Nw 2024 | `C:\ProgramData\Autodesk\Navisworks Manage 2024\Plugins\<PluginName>\` + subfolders    |
| Nw 2025 | `C:\ProgramData\Autodesk\Navisworks Manage 2025\Plugins\<PluginName>\` + subfolders    |
| Nw 2026 | `C:\ProgramData\Autodesk\Navisworks Manage 2026\Plugins\<PluginName>\` + subfolders    |
| Nw 2027 | `C:\ProgramData\Autodesk\Navisworks Manage 2027\Plugins\<PluginName>\` + subfolders    |

The `en-US\` and `Images\` subfolders **must** be preserved under the plugin folder; flattening them will break button labels and icons.

---

## 5. Bundle format (`*.bundle`) - modern alternative

The bundle format works for either track and is what the Autodesk App Store delivers. It lets one package target multiple Navisworks releases.

```
<PluginName>.bundle\
├── PackageContents.xml
└── Contents\
    ├── 2024\
    │   ├── <PluginName>.dll
    │   ├── <PluginName>.addin
    │   └── (XAML / en-US / Images for Track B)
    ├── 2025\
    │   └── ...
    ├── 2026\
    │   └── ...
    └── 2027\
        └── ...
```

`PackageContents.xml` declares supported releases and which component the runtime should load per version:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0"
                    AutodeskProduct="Navisworks"
                    Name="MyPlugin"
                    AppVersion="1.0.0"
                    ProductCode="{your-guid}"
                    Description="What this does"
                    Author="Your Company"
                    HelpFile="./Contents/help.htm">
  <CompanyDetails Name="Your Company" Url="https://example.com" Email="x@example.com"/>
  <Components Description="Navisworks 2024 Plugin">
    <RuntimeRequirements OS="Win64" Platform="Navisworks" SeriesMin="Nw11" SeriesMax="Nw15"/>
    <ComponentEntry AppName="MyPlugin"
                    ModuleName="./Contents/2024/MyPlugin.dll"
                    AppDescription="..."
                    LoadOnAutoCADStartup="True"
                    LoadOnCommandInvocation="False"/>
  </Components>
  <!-- Repeat <Components> per version, with SeriesMin/SeriesMax bumped -->
</ApplicationPackage>
```

> **Series codes:** Nw 2024 = `Nw11`, Nw 2025 = `Nw12`, Nw 2026 = `Nw13`, Nw 2027 = `Nw14` (approximate - validate against the SDK headers shipped with each version).

Install the bundle to one of:

| Scope         | Path                                                |
| ------------- | --------------------------------------------------- |
| All users     | `%PROGRAMDATA%\Autodesk\ApplicationPlugins\`        |
| Current user  | `%APPDATA%\Autodesk\ApplicationPlugins\`            |

Either path is enumerated by Navisworks at startup; nothing else is needed. The 2024+ App Manager UI shows bundle-format plugins by name automatically.

---

## 6. What actually changes between Nw 2024, 2025, 2026, 2027

Most users assume each release is a separate world. It isn't. Here is the **honest** list of what differs:

| Change                          | Nw 2024                          | Nw 2025         | Nw 2026                                      | Nw 2027                                                     |
| ------------------------------- | -------------------------------- | --------------- | -------------------------------------------- | ----------------------------------------------------------- |
| .addin schema                   | Unchanged                        | Unchanged       | Unchanged                                    | Unchanged                                                   |
| Discovery paths                 | Same as above                    | Same            | Same                                         | Same                                                        |
| `[AddInPlugin]` / `[Plugin]`    | Same                             | Same            | Same                                         | Same                                                        |
| App Manager UI                  | **New** - Home tab > App Manager | Yes             | Yes                                          | Yes (refreshed icons consistent with the 2027 UI redesign)  |
| `ModelItem` property model      | Eager                            | Eager           | **Lazy** - properties fetched on demand      | Lazy                                                        |
| Clash Detective public API      | Same                             | Same            | Same                                         | Same (UI rewrite is cosmetic; `ClashTestData` unchanged)    |
| Required API DLL                | 2024 build                       | 2025 build      | 2026 build                                   | 2027 build                                                  |

**Practical impact for the AutoNAV-style plugin:**

- Same compiled DLL works in all four versions for surface-API code (selection sets, search criteria, clash test add/copy patterns).
- If you upgrade an existing plugin that cached `PropertyCategory` / `DataProperty` references, retest on 2026 - the Lazy model can return null where the eager model returned a value.
- For 2027, verify your clash group additions still display correctly in the redesigned Clash Detective windows; the underlying API path used by `ClashGrouper.cs` is unchanged.

---

## 7. Troubleshooting checklist

| Symptom                                                            | Likely cause                                                                                 | Fix                                                                                                    |
| ------------------------------------------------------------------ | -------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| **Add-Ins tab not visible**                                        | Zero plugins loaded successfully. The tab is hidden by design when empty.                    | Check App Manager (2024+); see which plugin failed and why                                             |
| Plugin DLL is in `Plugins\` but doesn't appear                     | Subfolder name doesn't match DLL base name                                                   | Rename folder to exactly match `<PluginName>` (case-insensitive)                                       |
| `FileNotFoundException: Autodesk.Navisworks.Api`                   | Wrong API version - built against 2024 API, loaded into 2026                                 | Build per-version or set references with `<Private>False</Private>` and rebuild against each version   |
| Plugin loads but Execute never fires                               | `FullClassName` in `.addin` doesn't match the actual type's namespace + class                | Fix `<FullClassName>` to exactly `Namespace.ClassName`                                                 |
| Custom ribbon tab missing (Track B)                                | `[RibbonLayout]` filename doesn't match the XAML on disk, or XAML isn't next to the DLL      | Verify XAML filename + path; XAML lives in the same folder as the DLL                                  |
| Buttons appear but with raw IDs as labels                          | `en-US\<Name>.name` missing or doesn't contain the Tab/Command Ids                           | Add the `.name` file; keys are `<Id>.DisplayName` / `<Id>.ToolTip`                                     |
| Buttons appear with no icon                                        | PNG path in XAML is wrong or `Images\` folder wasn't copied during install                   | Confirm `Images\*.png` deployed; paths in XAML are relative to plugin folder                           |
| Custom tab appears but clicking does nothing                       | `[Command("Id")]` argument doesn't match `NWRibbonButton Id` in XAML                         | Make the IDs identical                                                                                 |
| AppManager (2024+) lists plugin as "load error"                    | Common causes: x86 build, wrong .NET version, missing dependency DLL, exception in static ctor | Switch to x64, target .NET Framework 4.8, copy dependency DLLs alongside, wrap static init in try/catch |

---

## 8. Verification

How to confirm a fresh install on a target version:

1. **Deploy.** Use `Distributable\Install-AutoNAV.ps1` (or the matching `.bat`). The installer enumerates Nw 2024 - 2027 and copies the plugin payload into each detected version's `C:\ProgramData\Autodesk\Navisworks Manage <year>\Plugins\<PluginName>\`.
2. **Launch.** Start that Navisworks version.
3. **Check load state.** Home tab > App Manager. Your plugin should be listed with no error.
4. **Find the button.**
   - Track A: open the **Add-Ins** ribbon tab; your button is under "Tool Add-Ins 1".
   - Track B: your custom tab is visible at the ribbon's top level.
5. **Trigger it.** Click the button; confirm `Execute` (Track A) or `ExecuteCommand` (Track B) fires.
6. **Repeat per version.** Each installed Navisworks version is independent - test all four if you ship for all four.

---

## 9. Working in-repo examples

| Concept                                  | File in this repo                                            |
| ---------------------------------------- | ------------------------------------------------------------ |
| Minimal `[AddInPlugin]` C# class         | `AutoNAV/PluginMain.cs`                                      |
| Minimal `.addin` XML manifest            | `AutoNAV/AutoNAV.addin`                                      |
| Per-version API DLL reference resolution | `AutoNAV/AutoNAV.csproj` (PropertyGroup conditionals)        |
| Per-version supported-releases manifest  | `Distributable/AutoNAV_v3.0.0/Package.xml`                   |
| Per-version installer (PowerShell)       | `Distributable/Install-AutoNAV.ps1`                          |
| Per-version installer (batch)            | `Distributable/AutoNAV_v3.0.0/Install_AutoNAV.bat`           |
| Per-version uninstaller (batch)          | `Distributable/AutoNAV_v3.0.0/Uninstall_AutoNAV.bat`         |

There is no in-repo Track B example today; for `CommandHandlerPlugin`, use the code samples in section 4 above.
