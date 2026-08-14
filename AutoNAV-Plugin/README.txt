AutoNAV plugin -- install guide
=================================

WHAT THIS IS
This folder contains the AutoNAV plugin compiled for Navisworks Manage
2024, 2025, 2026, and 2027.  There's nothing embedded or self-extracting --
just plain DLLs that get copied into each Navisworks's Plugins folder by a
tiny .cmd that does exactly what you'd do manually in File Explorer.


HOW TO INSTALL (one click)
1. Right-click "Install.cmd" -> Run as administrator.
2. Confirm the UAC prompt.
3. Done.  Launch Navisworks -> Add-Ins ribbon tab -> AutoNAV.


HOW TO INSTALL (manually, no script)
For each Navisworks Manage 2024 / 2025 / 2026 / 2027 you have installed:
  1. Open File Explorer at
        C:\Program Files\Autodesk\Navisworks Manage <year>\Plugins\
  2. Create a folder named AutoNAV (if it doesn't already exist).
  3. Copy AutoNAV.dll and AutoNAV.addin from the V<yy>\ folder next to
     this readme into that AutoNAV folder.
  4. Restart Navisworks.

So for Navisworks Manage 2025 you'd end up with:
  C:\Program Files\Autodesk\Navisworks Manage 2025\Plugins\AutoNAV\AutoNAV.dll
  C:\Program Files\Autodesk\Navisworks Manage 2025\Plugins\AutoNAV\AutoNAV.addin


WHY NO SINGLE-FILE INSTALLER
Self-extracting installers (whether .exe or .cmd with embedded base64
payloads) reliably trigger Windows Defender and other AV heuristics
because the same patterns are used by malware droppers.  Avoiding the
self-extracting pattern entirely is what avoids the false-positive AV
flags.

This package's only "automation" is a 30-line Install.cmd that runs
`mkdir` and `copy` -- the same operations you would do manually.  No
base64 decoding, no certutil, no embedded executables.


UNINSTALL
Right-click "Uninstall.cmd" -> Run as administrator.

Or manually delete each Plugins\AutoNAV\ folder.
