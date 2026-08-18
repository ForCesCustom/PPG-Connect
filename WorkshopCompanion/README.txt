CONNECT WORKSHOP COMPANION
==========================

This is the Steam Workshop-facing companion for Connect.

People Playground Workshop cannot reliably install BepInEx loader files beside
People Playground.exe. The companion therefore checks whether the external
Connect BepInEx runtime has actually started. If it has not, it displays a
recovery notice with the likely causes and a BepInEx guide. The standard mod
scanner rejects file-path inspection, so the main BepInEx runtime performs the
exact file-name check when it is available.

The current People Playground standard loader blocks `Application.OpenURL` and
does not expose the GUI module to source mods. The companion therefore uses the
game's supported `ModAPI.Notify` banner with the BepInEx guide and the real
direct download URL. It does not bypass the scanner. The full BepInEx Connect
recovery notice can both open and copy that same real URL.

The actual multiplayer implementation remains the external package. Its complete
ZIP includes BepInEx 5 x64 and must be extracted into the People Playground game
root, preserving the following runtime location:

  <People Playground>\BepInEx\plugins\Connect\

Do not place Steam DLLs or steam_appid.txt in that folder.
