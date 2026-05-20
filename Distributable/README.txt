================================================================================
                         AutoNAV Plugin Installer
                              Version 3.0.0
================================================================================

QUICK START - DO THIS FIRST:

1. Right-click on "Install-AutoNAV.bat"
2. Select "Run as administrator"
3. Follow the on-screen instructions
4. Restart Navisworks Manage 2025
5. Look for AutoNAV in the Add-Ins ribbon tab

================================================================================

WHAT'S IN THIS PACKAGE:

- Install-AutoNAV.bat         Main installer (run this file)
- Install-AutoNAV.ps1         PowerShell installer script
- AutoNAV.dll                 The AutoNAV plugin executable
- AutoNAV.addin               Plugin configuration file
- AutoNAV.pdb                 Debug symbols (optional)
- INSTALLATION_GUIDE.md       Detailed installation instructions
- README.txt                  This file

================================================================================

SYSTEM REQUIREMENTS:

- Windows 10 or Windows 11
- Navisworks Manage 2025 (or 2024)
- .NET Framework 4.8 or later
- Administrator access (required for installation)
- 64-bit system

================================================================================

INSTALLATION OPTIONS:

METHOD 1 - EASIEST (Recommended):
   Right-click "Install-AutoNAV.bat" → "Run as administrator"

METHOD 2 - POWERSHELL (Advanced):
   Run PowerShell as Administrator and execute:
   .\Install-AutoNAV.ps1

METHOD 3 - MANUAL (Advanced):
   Copy AutoNAV.dll and AutoNAV.addin to:
   C:\Program Files\Autodesk\Navisworks Manage 2025\AddIns\

================================================================================

AFTER INSTALLATION:

1. Launch Navisworks Manage 2025
2. Find the "Add-Ins" tab in the ribbon menu
3. Click "AutoNAV" to open the plugin
4. Start using AutoNAV's features

================================================================================

TROUBLESHOOTING:

Q: "Administrator privileges required" error
A: Right-click Install-AutoNAV.bat and select "Run as administrator"

Q: AutoNAV doesn't appear in Navisworks
A: 
   - Close Navisworks completely
   - Run the installer again
   - Restart Navisworks

Q: Navisworks not found
A:
   - Install Navisworks Manage 2025
   - Or use manual installation method

Q: .NET Framework errors
A:
   - Install .NET Framework 4.8 from:
     https://dotnet.microsoft.com/en-us/download/dotnet-framework

For more help, see INSTALLATION_GUIDE.md

================================================================================

UNINSTALLATION:

To uninstall AutoNAV:

1. Open PowerShell as Administrator
2. Navigate to the installer folder
3. Run: .\Install-AutoNAV.ps1 -Uninstall

Or manually delete these files from:
C:\Program Files\Autodesk\Navisworks Manage 2025\AddIns\
- AutoNAV.dll
- AutoNAV.addin
- AutoNAV.pdb (if present)

================================================================================

SUPPORT:

If you encounter issues:
1. Check INSTALLATION_GUIDE.md for detailed troubleshooting
2. Verify Navisworks version is 2024 or 2025
3. Ensure .NET Framework 4.8 is installed
4. Contact your IT department

================================================================================

VERSION INFORMATION:

Plugin Name:    AutoNAV
Version:        3.0.0
Author:         Keith Acker
Target:         Navisworks Manage 2025/2024
Framework:      .NET 4.8
Architecture:   x64 (64-bit)

Release Date:   May 2026
Status:         Production Ready

================================================================================

NEXT STEPS:

1. Double-click Install-AutoNAV.bat
2. Let the installer finish
3. Launch Navisworks Manage 2025
4. Enjoy AutoNAV!

For detailed information, see INSTALLATION_GUIDE.md

================================================================================
