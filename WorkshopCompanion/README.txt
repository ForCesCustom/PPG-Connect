CONNECT WORKSHOP COMPANION
==========================

This is the native People Playground Mods-menu companion for Connect.

People Playground Workshop cannot reliably install BepInEx loader files beside
People Playground.exe. The companion therefore checks whether the external
Connect BepInEx runtime has actually started with the matching Connect version.
If it is missing, or the two Connect versions differ, it displays a clear
standard People Playground notification with the direct recovery URL. The
standard mod scanner rejects file-path inspection, so the main BepInEx runtime
performs the exact file-name check when it is available.

The companion uses only the versioned HTTPS URL embedded by the Connect
publisher. It does not accept a remote URL or bypass the scanner.

The actual multiplayer implementation remains the external package. Its complete
ZIP includes BepInEx 5 x64 and must be extracted into the People Playground game
root, preserving the following runtime location:

  <People Playground>\BepInEx\plugins\Connect\

Do not place Steam DLLs or steam_appid.txt in that folder.
