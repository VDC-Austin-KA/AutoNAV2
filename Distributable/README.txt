================================================================================
                         AutoNAV Plugin Installer
                              Version 3.0.0
================================================================================

QUICK START - DO THIS FIRST:

1. Right-click on "Install-AutoNAV.bat"
2. Select "Run as administrator"
3. Follow the on-screen instructions
4. Restart Navisworks Manage
5. Look for AutoNAV in the Add-Ins ribbon tab

================================================================================

WHAT'S IN THIS PACKAGE:

- Install-AutoNAV.bat         Main installer (run this file)
- Install-AutoNAV.ps1         PowerShell installer script
- AutoNAV.dll                 The AutoNAV plugin executable
- AutoNAV.addin               Plugin manifest file
- AutoNAV.pdb                 Debug symbols (optional)
- INSTALLATION_GUIDE.md       Detailed installation instructions
- README.txt                  This file

================================================================================

SYSTEM REQUIREMENTS:

- Windows 10 or Windows 11
- Navisworks Manage 2024, 2025, 2026, or 2027
- .NET Framework 4.8 or later
- Administrator access (required for installation)
- 64-bit system

================================================================================

INSTALLATION OPTIONS:

METHOD 1 - EASIEST (Recommended):
   Right-click "Install-AutoNAV.bat" -> "Run as administrator"
   Installs to ALL detected Navisworks versions automatically.

METHOD 2 - POWERSHELL (Advanced):
   Run PowerShell as Administrator and execute:
   .\Install-AutoNAV.ps1

METHOD 3 - MANUAL (Advanced):
   Create this folder for each Navisworks version you have installed:
   C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\

   Replace 202X with your version (2024, 2025, 2026, or 2027).
   Copy AutoNAV.dll and AutoNAV.addin into that AutoNAV\ subfolder.
   Both files must be in the same folder.

================================================================================

AFTER INSTALLATION:

1. Launch Navisworks Manage
2. Find the "Add-Ins" tab in the ribbon menu
3. Click "AutoNAV" to open the plugin
4. Start using AutoNAV's features

================================================================================

WHERE FILES ARE INSTALLED:

For each detected version of Navisworks Manage:
  C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\
    AutoNAV.dll
    AutoNAV.addin
    AutoNAV.pdb

================================================================================

TROUBLESHOOTING:

Q: "Administrator privileges required" error
A: Right-click Install-AutoNAV.bat and select "Run as administrator"

Q: AutoNAV doesn't appear in Navisworks
A: - Close Navisworks completely
   - Run the installer again
   - Restart Navisworks
   - Verify files are in Plugins\AutoNAV\ (not AddIns\)

Q: Navisworks not found
A: Install Navisworks Manage 2024, 2025, 2026, or 2027 and retry.

Q: .NET Framework errors
A: Install .NET Framework 4.8 from Microsoft's download site.

For more help, see INSTALLATION_GUIDE.md

================================================================================

UNINSTALLATION:

To uninstall AutoNAV:

1. Open PowerShell as Administrator
2. Navigate to the installer folder
3. Run: .\Install-AutoNAV.ps1 -Uninstall

Or manually delete the AutoNAV\ subfolder from:
  C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\

================================================================================

VERSION INFORMATION:

Plugin Name:    AutoNAV
Version:        3.0.0
Author:         Keith Acker
Target:         Navisworks Manage 2024 / 2025 / 2026 / 2027
Framework:      .NET 4.8
Architecture:   x64 (64-bit)
Release Date:   May 2026

================================================================================
