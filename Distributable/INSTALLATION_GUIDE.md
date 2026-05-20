# AutoNAV Plugin Installation Guide

**Version:** 3.0.0  
**Compatible with:** Navisworks Manage 2024, 2025, 2026, 2027  
**System Requirements:** Windows 10/11, .NET Framework 4.8+

---

## Quick Start (Easiest Method)

1. **Right-click** on `Install-AutoNAV.bat`
2. Select **"Run as administrator"**
3. AutoNAV installs to **all Navisworks versions found** on your machine
4. Launch **Navisworks Manage**
5. AutoNAV will appear in the **Add-Ins** ribbon tab

---

## Installation Methods

### Method 1: Batch File Installer (RECOMMENDED)

**Steps:**

1. Right-click `Install-AutoNAV.bat`
2. Select "Run as administrator"
3. Wait for the installation to complete
4. Press any key when done

**What it does:**
- Automatically detects every installed version of Navisworks Manage (2024-2027)
- Creates the `Plugins\AutoNAV\` folder for each version if needed
- Backs up any existing AutoNAV installation
- Closes Navisworks if it's running
- Installs `AutoNAV.dll` and `AutoNAV.addin` to each version simultaneously

---

### Method 2: PowerShell Script (Advanced Users)

1. Open PowerShell as Administrator
2. Navigate to the installer folder:
   ```powershell
   cd "C:\path\to\installer"
   ```
3. Run the installer script:
   ```powershell
   .\Install-AutoNAV.ps1
   ```

**Additional Options:**

- **Uninstall AutoNAV** (removes from all versions):
  ```powershell
  .\Install-AutoNAV.ps1 -Uninstall
  ```

---

### Method 3: Manual Installation (Advanced Users)

For each version of Navisworks Manage you have installed:

1. **Create the plugin folder** (if it doesn't exist):
   ```
   C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\
   ```
   Replace `202X` with your version number (2024, 2025, 2026, or 2027).

2. **Copy these files into that `AutoNAV\` subfolder:**
   - `AutoNAV.dll` - The main plugin file
   - `AutoNAV.addin` - The plugin manifest
   - `AutoNAV.pdb` (optional) - Debug symbols

   > **Important:** Both `AutoNAV.dll` and `AutoNAV.addin` must be in the same `AutoNAV\` subfolder. The `.addin` file references the DLL by relative path.

3. **Launch Navisworks Manage**
4. AutoNAV will appear in the Add-Ins ribbon

---

## Verification Steps

After installation, verify AutoNAV is properly installed:

1. **Launch Navisworks Manage**
2. **Look at the Ribbon menu** at the top of the window
3. **Find the "Add-Ins" tab**
4. **AutoNAV** button should be visible
5. **Click AutoNAV** to open the plugin interface

If AutoNAV doesn't appear:
- Ensure Navisworks was fully closed during installation
- Verify files are in `C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\`
- Confirm both `AutoNAV.dll` and `AutoNAV.addin` are in the same `AutoNAV\` subfolder
- Try restarting your computer

---

## Uninstallation

### Using PowerShell:

```powershell
.\Install-AutoNAV.ps1 -Uninstall
```

Removes AutoNAV from all detected Navisworks versions.

### Manual Uninstallation:

For each Navisworks version installed:

1. Close Navisworks Manage completely
2. Delete the folder:
   ```
   C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\
   ```
3. Restart Navisworks

---

## File Locations

After successful installation, files are located at:

```
C:\ProgramData\Autodesk\Navisworks Manage 2024\Plugins\AutoNAV\
  AutoNAV.dll
  AutoNAV.addin
  AutoNAV.pdb

C:\ProgramData\Autodesk\Navisworks Manage 2025\Plugins\AutoNAV\
  AutoNAV.dll
  AutoNAV.addin
  AutoNAV.pdb

(and so on for each version found)
```

**Backup location** (if a previous version existed):
```
C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\
  Backup_YYYYMMDD_HHMMSS\
    AutoNAV.dll
    AutoNAV.addin
```

---

## Troubleshooting

### Issue: "Administrator privileges required"

**Solution:**
- Right-click the installer file
- Select "Run as administrator"
- Click "Yes" when prompted

### Issue: Navisworks not detected

**Possible causes:**
- Navisworks is not installed
- Installed in non-standard location

**Solutions:**
1. Install Navisworks Manage 2024, 2025, 2026, or 2027
2. Try the manual installation method

### Issue: AutoNAV doesn't appear in Navisworks

**Steps to resolve:**
1. Close Navisworks completely
2. Run the installer again
3. Restart Navisworks
4. Check the Add-Ins tab

**If still not working:**
- Verify both files are in `C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\`
- Confirm `AutoNAV.addin` and `AutoNAV.dll` are in the same `AutoNAV\` subfolder
- Check that `AutoNAV.addin` has correct read permissions

### Issue: "AutoNAV.dll failed to load" message

**Possible causes:**
- .NET Framework 4.8 not installed
- DLL compatibility issues

**Solutions:**
1. Install or update to .NET Framework 4.8
2. Restart your computer
3. Reinstall AutoNAV

---

## System Requirements

- **Operating System:** Windows 10 or Windows 11
- **Navisworks Version:** 2024, 2025, 2026, or 2027
- **.NET Framework:** 4.8 or later
- **Processor:** x64 (64-bit)
- **Admin Rights:** Required for installation

---

**Version History:**
- v3.0.0 - Multi-version support (2024/2025/2026/2027), correct Plugins\AutoNAV\ install path
