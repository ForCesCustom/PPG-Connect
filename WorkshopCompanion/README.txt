CONNECT WORKSHOP COMPANION
==========================

This is the Steam Workshop-facing companion for Connect.

People Playground Workshop cannot reliably install BepInEx loader files beside
People Playground.exe. The companion therefore checks whether the external
Connect BepInEx runtime has actually started with the matching Connect version.
If it is missing, or the two Connect versions differ, it displays a native
People Playground recovery dialog with the likely causes and an **OPEN CONNECT
ON GITHUB** button. The direct URL is also included in the standard in-game
notification. The standard mod scanner rejects file-path inspection, so the
main BepInEx runtime performs the exact file-name check when it is available.

The companion opens only the versioned HTTPS URL embedded by the Connect
publisher. It does not accept a remote URL or bypass the scanner. The full
BepInEx Connect recovery notice can open the same direct package URL.

The actual multiplayer implementation remains the external package. Its complete
ZIP includes BepInEx 5 x64 and must be extracted into the People Playground game
root, preserving the following runtime location:

  <People Playground>\BepInEx\plugins\Connect\

Do not place Steam DLLs or steam_appid.txt in that folder.
