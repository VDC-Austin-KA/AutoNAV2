# AutoNAV Plugin Installation Guide

**Version:** 3.0.0  
**Compatible with:** Navisworks Manage 2025, 2024  
**System Requirements:** Windows 10/11, .NET Framework 4.8+

---

## Quick Start (Easiest Method)

### For Windows Users:

1. **Download the installer package** to your computer
2. **Right-click** on `Install-AutoNAV.bat`
3. Select **"Run as administrator"**
4. Follow the on-screen prompts
5. Launch **Navisworks Manage 2025**
6. AutoNAV will appear in the **Add-Ins** ribbon tab

---

## Installation Methods

### Method 1: Batch File Installer (RECOMMENDED)

**Best for:** Most users who want a simple point-and-click installation

**Steps:**

1. Extract the installer package to any folder
2. Right-click `Install-AutoNAV.bat`
3. Select "Run as administrator"
4. Wait for the installation to complete
5. Click OK when done

**What it does:**
- Automatically detects your Navisworks installation
- Creates an AddIns folder if needed
- Backs up any existing AutoNAV installation
- Closes Navisworks if it's running
- Installs all necessary files

---

### Method 2: PowerShell Script (Advanced Users)

**Best for:** Users who want more control or scripted deployments

**Steps:**

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

- **Silent installation** (no prompts):
  ```powershell
  .\Install-AutoNAV.ps1 -Silent
  ```

- **Uninstall AutoNAV**:
  ```powershell
  .\Install-AutoNAV.ps1 -Uninstall
  ```

---

### Method 3: Manual Installation (Advanced Users)

**For users who prefer manual file placement:**

1. **Locate your Navisworks AddIns folder:**
   - Typical path: `C:\Program Files\Autodesk\Navisworks Manage 2025\AddIns`
   - Or: `C:\Program Files (x86)\Autodesk\Navisworks Manage 2025\AddIns`

2. **Create the AddIns folder** if it doesn't exist:
   - Right-click in Navisworks installation folder
   - Select New → Folder
   - Name it "AddIns"

3. **Copy these files to the AddIns folder:**
   - `AutoNAV.dll` - The main plugin file
   - `AutoNAV.addin` - The plugin manifest
   - `AutoNAV.pdb` (optional) - Debug symbols

4. **Launch Navisworks Manage 2025**
5. AutoNAV will appear in the Add-Ins ribbon

---

## Verification Steps

After installation, verify that AutoNAV is properly installed:

1. **Launch Navisworks Manage 2025**
2. **Look at the Ribbon menu** at the top of the window
3. **Find the "Add-Ins" tab**
4. **AutoNAV** button should be visible
5. **Click AutoNAV** to open the plugin interface

If AutoNAV doesn't appear:
- Ensure Navisworks was fully closed during installation
- Check that files were copied to the correct AddIns folder
- Verify Navisworks version (must be 2024 or 2025)
- Try restarting your computer

---

## Uninstallation

### Using PowerShell:

```powershell
.\Install-AutoNAV.ps1 -Uninstall
```

### Manual Uninstallation:

1. Close Navisworks Manage completely
2. Navigate to: `C:\Program Files\Autodesk\Navisworks Manage 2025\AddIns`
3. Delete these files:
   - `AutoNAV.dll`
   - `AutoNAV.addin`
   - `AutoNAV.pdb` (if present)
4. Restart Navisworks

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
1. Install Navisworks Manage 2025
2. Or update your installation path in the installer
3. Try manual installation method

### Issue: AutoNAV doesn't appear in Navisworks

**Steps to resolve:**
1. Close Navisworks completely
2. Run the installer again
3. Restart Navisworks
4. Check the Add-Ins tab

**If still not working:**
- Verify files are in: `C:\Program Files\Autodesk\Navisworks Manage 2025\AddIns`
- Check that `AutoNAV.addin` has correct permissions
- Try manual file copy method

### Issue: Installation fails with permission errors

**Solutions:**
- Run Command Prompt as Administrator
- Ensure Navisworks is completely closed
- Disable antivirus temporarily (if safe)
- Check disk space availability

### Issue: "AutoNAV.dll failed to load" message

**Possible causes:**
- .NET Framework 4.8 not installed
- DLL compatibility issues

**Solutions:**
1. Install or update to .NET Framework 4.8:
   - Download from: https://dotnet.microsoft.com/en-us/download/dotnet-framework
2. Restart your computer
3. Reinstall AutoNAV

---

## File Locations

After successful installation, you should find:

**Windows 10/11 (Standard Installation):**
```
C:\Program Files\Autodesk\Navisworks Manage 2025\AddIns\
├── AutoNAV.dll
├── AutoNAV.addin
└── AutoNAV.pdb (optional)
```

**Backup locations (if you have previous versions):**
```
C:\Program Files\Autodesk\Navisworks Manage 2025\AddIns\
└── AutoNAV_Backup_[YYYYMMDD_HHMMSS]\
    ├── AutoNAV.dll
    └── AutoNAV.addin
```

---

## System Requirements

- **Operating System:** Windows 10 or Windows 11
- **Navisworks Version:** 2024 or 2025
- **.NET Framework:** 4.8 or later
- **Processor:** x64 (64-bit)
- **RAM:** 8 GB minimum
- **Admin Rights:** Required for installation

---

## Support & Contact

If you encounter issues:

1. **Check this guide** for troubleshooting steps
2. **Verify your Navisworks version** is compatible
3. **Ensure .NET Framework 4.8** is installed
4. **Contact your IT department** or the plugin developer

---

## What's Installed

The AutoNAV plugin includes:

- **Automated clash detection** and grouping
- **Design coordination** tools
- **Ribbon UI integration** with Navisworks
- **Real-time clash analysis**
- **Report generation** capabilities

For detailed feature information, see the AutoNAV documentation.

---

**Version History:**
- v3.0.0 - Initial release with Navisworks 2025 support
- Full installer automation
- Silent installation support
- Backup and restore functionality
