Connect — BepInEx edition v0.1.42
===============================

Connect adds a host-authoritative Steam Relay session to People Playground.
Every player needs the same People Playground build and the same complete
Connect release ZIP, which already includes BepInEx 5 x64 and Connect.BepInEx.dll.

Confirmed local target
----------------------

- People Playground 1.27.16 (Steam build 24782494)
- Unity 2020.3.1f1 Mono x64
- Facepunch.Steamworks.Win64 supplied by the game
- BepInEx 5.4.23.5 Unity.Mono-win-x64

Installation
------------

1. Fully close People Playground.
2. Download the complete package from
   `https://github.com/ForCesCustom/PPG-Connect/raw/main/Releases/Connect-v0.1.42.zip`.
   Extract the entire **Connect-v0.1.42.zip** directly into the folder that
   contains `People Playground.exe`, and allow Windows to merge the supplied
   `BepInEx` folder. The release already contains BepInEx 5 Unity.Mono-win-x64,
   `winhttp.dll`, `doorstop_config.ini`, the Connect DLL and its icon.
3. Start the game through Steam. Do not add `steam_appid.txt` and do not copy
   any Steam DLL into the plugin folder.
4. Press F8 at the main menu or in a map.

If BepInEx is already installed for another mod, merging this package preserves
the existing plugins. Do not replace People Playground.exe or remove unrelated
files from the game folder.

The Connect panel can be dragged by its header. Its position is saved in the
BepInEx configuration and is restored after closing, reopening or restarting
the game.

The rounded Connect icon is included beside the plugin DLL as
`connect-icon.png`; it appears in the Connect panel header. Keep this file in
the same folder as `Connect.BepInEx.dll`.

Steam Workshop and missing-file recovery
----------------------------------------

Connect uses BepInEx, whose loader files must sit beside `People Playground.exe`.
Steam Workshop cannot reliably place these external loader files there. The
included native `Mods\\Connect` companion is a standard People Playground mod.
It detects whether the external Connect runtime actually started with the
matching Connect version and prints an explicit recovery notice when it is
missing or outdated.
The standard loader blocks file-path inspection, so exact file names are shown
by the BepInEx runtime after it starts; the Companion safely covers the case
where that runtime is absent entirely.

The BepInEx plugin has the same runtime health check and maintains the direct
package URL. Use `FRIEND_INSTALL_GUIDE_RU.txt` for a Russian installation guide.

Settings
--------

Press **SET** in the Connect panel header to open Settings. The two tabs have
different authority:

- **PLAYER**: local visual and input preferences for that player only: remote
  name/avatar visibility, remote-cursor scale and smoothing, and cursor send
  rate. They are saved in that player's BepInEx config and never modify the
  host's world.
- **HOST**: the host's saved server profile: Physics2D velocity and position
  solver iterations, physics snapshot rate, Connect object limit, per-guest
  spawn and interaction limits, bot spawn limit, and guest spawn/grab/use/delete
  permissions. A client sees this tab read-only after the Steam Relay handshake.

Physics iteration settings are applied only by the active Connect host and are
restored to the game's previous values when the host leaves the lobby. Raising
them can improve difficult stacks at a CPU cost; start with 8 velocity / 3
position. The object cap applies to Connect spawn requests and bots, not to
items the host manually places with the unmodified vanilla catalog.

Host a session
--------------

1. In F8 panel choose privacy (Friends Only is default) and max players.
2. Press CREATE LOBBY.
3. Press a [ + ] Invite Friend row. This opens Steam's own lobby-invite chooser.
4. Once a friend is connected, press **START & SYNC MAP**. If you are still in
   the title lobby, choose a People Playground map next; connected guests load
   that same installed map automatically through People Playground's own scene
   transition. The host entering a map with guests also starts this map-follow
   path. A connected non-host cannot select or enter a divergent local map.
   While the map changes, the host's member card reports each guest separately:
   **LOADING MAP**, **SYNCING**, **PLAYING**, or **MAP FAILED**. The status is
   sent by that guest through the authenticated Steam Relay, not guessed from
   Steam lobby membership.
5. The session host is the physics authority.

Join a friend
-------------

1. Install the same DLL and launch through Steam.
2. Accept the Steam invite. If People Playground is already running, the Steam
   lobby callback joins it. If Steam launches the game with +connect_lobby,
   Connect accepts only that numeric argument and joins after Steam is ready.
3. Wait for the panel to say that the Steam Relay handshake is complete.

Controls during a started session
---------------------------------

- F8: panel; F10: diagnostics.
- Local and remote cursors are transmitted in world coordinates. Cameras remain
  local and are never synchronized.
- Left mouse: host-authoritative physics drag. The host validates the hit and
  issues a temporary object lease.
- Press **Tab** to open People Playground's normal catalog. Each player keeps
  their own catalog search, category and item choice. On a connected client,
  the normal catalog Spawn call is intercepted: only its stable vanilla key,
  flip state and world cursor position are sent to the host. The host validates,
  creates and broadcasts the one authoritative object, so it appears for the
  host and every client. The old mini Spawn Menu has been removed.
- Direct vanilla **Use** and the standard context-menu **Activate** / **Delete**
  actions work for registered Connect objects. Client actions are requested from
  the host, range-checked at the player's network cursor, rate-limited and then
  applied by the host. Use Host Settings to allow or deny these permissions.
- The player's configured **Activate** binding (the base game action named
  `activateDirect`, including the default F binding when configured that way)
  also works for that player's local right-click selection. The player does not
  need the host's selection or context menu. Holding the binding renews one
  short host-side continuous-use lease instead of sending a packet every frame;
  this lets automatic vanilla firearms and continuous-use components run through
  the host's normal `IsBeingUsedContinuously()` path.
- Host Bot Mode: after START SESSION, use the BOT MODE card to enable one to
  three autonomous bots. Each has a coloured world-space cursor and the same
  host-side brain capabilities: it can safely spawn a vanilla item, pick up
  and place a bot-created item through a normal grab lease, and later clean up
  only an old bot-created item that nobody is holding.
  Bots wait for a loaded sandbox map; they never instantiate fallback objects
  from the title screen.
- Host permissions are checked on the host. Disallowed or rate-limited spawn
  requests show an action-denied notice without disconnecting the player.

Current implemented functionality
---------------------------------

- Steam Friends/Private/Public lobby metadata, member list and official invite UI.
- Steam lobby join callback and safe +connect_lobby parsing.
- SteamNetworkingSockets relay listener/client connection and validated handshake.
- Steam names and asynchronously cached Steam medium avatars (Unity texture work
  stays on the Unity main thread).
- Independent world-space cursors, host-authoritative mass-aware grab leases,
  replicated post-start vanilla spawns/despawns and root Rigidbody2D snapshots.
- Host-only Bot Mode: one to three coloured autonomous cursor agents. Every
  bot owns a Builder, Mover or Cleaner personality profile, but every profile
  retains the same spawn, grab/place and safe-cleanup capabilities. Their
  physical interaction and replicated spawns stay host-authoritative.
- The standard Tab catalog is each player's own spawn UI. All client catalog
  spawns remain host-authoritative and are replicated through the Spawn event.
  v0.1.36 records each critical spawn stage in `LogOutput.log`: Tab interception,
  request receipt, host broadcast and guest instantiation. This is event-level
  logging, not a per-frame network trace.
  When a guest completes its host-map load, v0.1.37 also sends that guest a
  reliable baseline of all registered post-start objects before normal physics
  snapshots continue. This covers a late map load or relay reconnect without
  placing world data in Steam Lobby metadata.
- Host-validated vanilla direct Use plus context Activate/Delete for registered
  Connect objects, including renewable continuous Use for automatic weapons and
  continuous-use components, with per-player permissions and rate limits.
- Host/server and player settings with persistent BepInEx storage, safe host
  Physics2D iteration apply/restore, relayed read-only host settings, spawn-rate
  limiting and host-enforced guest permissions.
- A narrow Harmony patch that blocks only vanilla world tools for a connected
  non-host client; camera, UI, Escape and Steam Overlay remain local.

Important limitations
---------------------

See KNOWN_LIMITATIONS.md before playing. In particular, the first release does
not recreate objects that already existed before START SESSION, does not support
human limb topology, joints, wires, projectile/damage synchronization, arbitrary
custom context-menu actions, freeze/rotate/undo, mod-set comparison or host
migration. It is a real lobby/relay/drag vertical slice, not universal People
Playground synchronization.

Connect does not make People Playground deterministic. The host is
authoritative and clients receive synchronized state. Complex Workshop objects
currently have no promised compatibility; only simple post-start vanilla
spawnables have root-Rigidbody snapshot support.

Diagnostics and support
-----------------------

Logs: <People Playground>\BepInEx\LogOutput.log
Look for [Connect] lines. Do not include Steam credentials in a report.
Include game build, plugin version, whether host or client, and relevant log
lines. Two different Steam accounts and two devices are required for a full
online test; do not attempt to run two peers under one Steam identity.
