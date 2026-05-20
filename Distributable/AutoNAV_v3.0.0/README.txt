================================================================================
                    AutoNAV v3.0.0 - Installation Guide
          Automated Design Coordination Plugin for Navisworks Manage
================================================================================

OVERVIEW
--------
AutoNAV is a Navisworks plugin that automates design coordination tasks
including search set generation, property-based organization, and clash test
automation for multi-discipline design coordination workflows.

FEATURES
--------
  Function 1: Automated Discipline Search Set Creation
  Function 2: Element Property-Based Search Sets
  Function 3: Custom Search Sets from Property Values
  Function 4: Automated Clash Test Generation
  Function 5: Intelligent Clash Result Grouping
  Function 6: Walls/Floors Clash Grouping by Discipline

SYSTEM REQUIREMENTS
-------------------
  Autodesk Navisworks Manage 2024, 2025, 2026, or 2027
  .NET Framework 4.8
  Windows 10 or later (64-bit)
  4 GB RAM (8 GB recommended)
  Administrator privileges for installation

INSTALLATION INSTRUCTIONS
-------------------------

METHOD 1: Automatic Installation (Recommended)
1. Right-click "Install_AutoNAV.bat" and select "Run as administrator"
2. Installer detects all installed Navisworks versions (2024-2027)
3. Installs AutoNAV to each version found
4. Restart Navisworks Manage

METHOD 2: Manual Installation
For each version of Navisworks Manage you have installed:

1. Create the plugin folder (replace 202X with your version):
   C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\

   Examples:
     C:\ProgramData\Autodesk\Navisworks Manage 2025\Plugins\AutoNAV\
     C:\ProgramData\Autodesk\Navisworks Manage 2026\Plugins\AutoNAV\

2. Copy these files from the Plugin subfolder into that AutoNAV\ subfolder:
   - AutoNAV.dll
   - AutoNAV.addin

   IMPORTANT: Both files must be in the same AutoNAV\ subfolder.
   The .addin file references AutoNAV.dll by relative path.

3. Restart Navisworks Manage

VERIFICATION
-----------
After installation:
1. Open Navisworks Manage
2. Look for "AutoNAV" in the Add-ins tab
3. Click AutoNAV to open the plugin panel

USAGE
-----
1. Open a multi-discipline Navisworks file
2. Go to Add-ins -> AutoNAV
3. Use the tabbed interface:
   - Search Sets (Func 1-3): Create organized search sets
   - Clash Tests (Func 4-6): Generate and manage clash tests

UNINSTALLATION
--------------
1. Right-click "Uninstall_AutoNAV.bat" and select "Run as administrator"
   OR
2. Delete the AutoNAV\ subfolder from each version's Plugins folder:
   C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\

TROUBLESHOOTING
---------------
Problem: Plugin not appearing in Navisworks
Solution:
  - Verify both AutoNAV.dll and AutoNAV.addin are in the same AutoNAV\ subfolder
  - Check: C:\ProgramData\Autodesk\Navisworks Manage 202X\Plugins\AutoNAV\
  - Restart Navisworks
  - Check that .NET Framework 4.8 is installed

Problem: "Plugin failed to load" error
Solution:
  - Verify AutoNAV.addin is in the same folder as AutoNAV.dll
  - Try reinstalling the plugin

Problem: Features not working as expected
Solution:
  - Ensure you have a multi-discipline Navisworks file open
  - Verify the file has search sets or clash tests available
  - See FEATURE_GUIDE.txt for detailed usage instructions

VERSION INFORMATION
-------------------
AutoNAV Version: 3.0.0
Build Date: 2026-05-20
Compatible Versions:
  - Navisworks Manage 2024
  - Navisworks Manage 2025
  - Navisworks Manage 2026
  - Navisworks Manage 2027

SUPPORT
-------
Developer: Keith Acker
License: Internal Use Only

================================================================================
