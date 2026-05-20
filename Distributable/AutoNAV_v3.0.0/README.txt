================================================================================
                    AutoNAV v3.0.0 - Installation Guide
          Automated Design Coordination Plugin for Navisworks Manage
================================================================================

OVERVIEW
--------
AutoNAV is a powerful Navisworks plugin that automates design coordination tasks
including search set generation, property-based organization, and clash test
automation for multi-discipline design coordination workflows.

FEATURES
--------
• Function 1: Automated Discipline Search Set Creation
• Function 2: Element Property-Based Search Sets
• Function 3: Custom Search Sets from Property Values
• Function 4: Automated Clash Test Generation
• Function 5: Intelligent Clash Result Grouping
• Function 6: Clash Test Batch Updates

SYSTEM REQUIREMENTS
-------------------
• Autodesk Navisworks Manage 2024, 2025, or 2026
• .NET Framework 4.8
• Windows 10 or later
• 4GB RAM (8GB recommended)
• Administrator privileges for installation

INSTALLATION INSTRUCTIONS
-------------------------

METHOD 1: Automatic Installation (Recommended)
1. Extract this folder to a known location
2. Right-click "Install_AutoNAV.bat" and select "Run as administrator"
3. Follow the prompts to complete installation
4. Restart Navisworks Manage

METHOD 2: Manual Installation
1. Locate your Navisworks Plugins folder:
   - For Navisworks 2025:
     C:\ProgramData\Autodesk\Navisworks Manage 2025\Plugins
   - For Navisworks 2024:
     C:\ProgramData\Autodesk\Navisworks Manage 2024\Plugins
   
2. Copy the following files from the Plugin subfolder to your Plugins folder:
   - AutoNAV.dll
   - AutoNAV.addin

3. Restart Navisworks Manage

METHOD 3: User-Level Installation (No Admin Required)
1. Locate your user plugin folder:
   %APPDATA%\Autodesk\Navisworks Manage\Plugins\

2. Copy these files there:
   - AutoNAV.dll
   - AutoNAV.addin

3. Restart Navisworks Manage

VERIFICATION
-----------
After installation, verify that AutoNAV is loaded:
1. Open Navisworks Manage
2. Look for "AutoNAV" in the Add-ins tab or toolbar
3. The plugin should be available in the Add-ins menu

USAGE
-----
1. Open a multi-discipline Navisworks file
2. Go to Add-ins → AutoNAV
3. Use the tabbed interface to access different functions:
   - Search Sets (Func 1-3): Create organized search sets
   - Clash Tests (Func 4-6): Generate and manage clash tests

For detailed usage instructions, refer to the user documentation or
run the AutoNAV help within the plugin.

UNINSTALLATION
---------------
To remove AutoNAV:
1. Right-click "Uninstall_AutoNAV.bat" and select "Run as administrator"
   OR
2. Manually delete AutoNAV.dll and AutoNAV.addin from your Plugins folder

TROUBLESHOOTING
---------------
Problem: Plugin not appearing in Navisworks
Solution: 
  - Ensure files are in the correct Plugins folder
  - Restart Navisworks
  - Check that .NET Framework 4.8 is installed

Problem: "Plugin failed to load" error
Solution:
  - Verify AutoNAV.addin file is in the same folder as AutoNAV.dll
  - Check that the .addin file is not corrupted
  - Try reinstalling the plugin

Problem: Features not working as expected
Solution:
  - Ensure you have a multi-discipline Navisworks file open
  - Verify the file has search sets or clash tests available
  - Check for any error messages in Navisworks

VERSION INFORMATION
-------------------
AutoNAV Version: 3.0.0
Build Date: 2026-05-20
Compatible Versions:
  - Navisworks 2026
  - Navisworks 2025
  - Navisworks 2024

SUPPORT & FEEDBACK
------------------
For issues, feature requests, or feedback, please contact:
Developer: Keith Acker
License: Internal Use Only

================================================================================
