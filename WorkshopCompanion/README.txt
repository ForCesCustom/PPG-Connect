CONNECT WORKSHOP COMPANION
==========================

This is the Steam Workshop-facing companion for Connect.

People Playground Workshop cannot reliably install BepInEx loader files beside
People Playground.exe. The companion therefore checks whether the external
Connect BepInEx runtime has actually started. If it has not, it displays a
visible recovery popup with the likely causes, **OPEN CONNECT ON GITHUB** and
**COPY LINK** buttons. The standard mod
scanner rejects file-path inspection, so the main BepInEx runtime performs the
exact file-name check when it is available.

The companion opens only the versioned HTTPS URL embedded by the Connect
publisher and can copy that same URL. It does not accept a remote URL or bypass
the scanner. The full BepInEx Connect recovery notice can also open/copy the
same direct package URL.

The actual multiplayer implementation remains the external package. Its complete
ZIP includes BepInEx 5 x64 and must be extracted into the People Playground game
root, preserving the following runtime location:

  <People Playground>\BepInEx\plugins\Connect\

Do not place Steam DLLs or steam_appid.txt in that folder.
