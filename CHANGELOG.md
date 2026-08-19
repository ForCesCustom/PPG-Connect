# Changelog

## 0.1.36 — Guest map progress and observable Tab-spawn route

- Added a validated Steam Relay `ClientMapStatus` control message. The host's
  lobby cards now show the real state reported by every connected guest:
  **LOADING MAP**, **SYNCING**, **PLAYING**, or **MAP FAILED**. A status is
  rejected unless it belongs to that relay identity, peer ID and the host's
  currently selected map.
- Decoupled the exact `CatalogBehaviour.Spawn(SpawnableAsset, bool)` route from
  the unrelated world-tool compatibility flag. A compatible connected client
  now routes its own normal Tab catalog selection to the host instead of
  silently creating a local-only item because another tool patch is unavailable.
- Added event-level `[Connect][Spawn]` traces for Tab interception, host request
  validation, host creation/broadcast, duplicate/missing asset handling and
  guest instantiation. Cursor and snapshot packets remain unlogged.
- Added protocol smoke coverage for the new bounded map-status message and
  bumped the wire protocol to 5. Both players must install v0.1.36.

## 0.1.35 — Actual sandbox scene transition for guests

- Replaced the client title-menu `MapLoaderBehaviour.Load()` shortcut with the
  exact People Playground map-selection path: assign the host's installed map,
  then invoke its `SceneSwitchBehaviour.Switch()` to load the sandbox scene.
  The newly loaded scene's own `MapLoaderBehaviour` constructs the map.
- A guest cannot manually select a map tile or use the Enter/Play scene switch
  while in a Connect lobby. Only the marked host-authorised transition passes
  through those two narrow Harmony guards.
- Tightened map readiness: title-menu children no longer count as a loaded map;
  the requested map root must be active under the actual loader.

## 0.1.34 — Real map instantiation and high-rate cursors

- Fixed the client map-follow defect exposed by the two-player logs: in People
  Playground 1.27.16, `MapLoadOverride` is editor-only, so it changed the
  selected map without constructing it in a normal game build. Connect now
  assigns the host map to `MapLoaderBehaviour.CurrentMap`, calls the game's own
  loader, and confirms the session only after an instantiated map root exists.
- A host entering a map while lobby guests are present automatically broadcasts
  that map. The client logs loader identity plus child counts and never reports
  `PLAYING` solely because the title screen has a selected `CurrentMap`.
- Cursor packets now remain at 60–120 Hz while UI is open; `UI busy` is only an
  interaction flag. Added bounded 50 ms velocity prediction and expanded the
  local smoothing range for noticeably smoother remote cursor motion.

## 0.1.33 — Relay receive-path repair and transport trace

- Fixed the actual Steam relay receive-path defect: Connect now calls the
  Facepunch `SocketManager.OnConnected` base handler after accepting a guest.
  That assigns the guest connection to the socket poll group, allowing host
  `Receive()` to receive `Hello`, cursor and map packets.
- Added focused diagnostic records for relay connection identity, accept/send
  results, Hello validation, packet rejection and map-loader decisions. Cursor
  and snapshot updates remain rate-limited and are not written one-by-one.

## 0.1.32 — Relay cursor and map-follow reliability

- Render remote cursors through People Playground's actual `Global.main.camera`
  instead of relying on an optional Unity `MainCamera` tag.
- Show a clearly labelled `SYNCING` cursor when a Steam lobby member arrives;
  replace it with the real independent world-space cursor after relay handshake.
- Mirror the compact map directive in Steam Lobby metadata as a fallback to the
  reliable relay command, so guests follow an already-selected host map even
  when they complete their handshake after the host starts.

## 0.1.31 — Runtime version mismatch detection

- Added a versioned runtime marker shared between the BepInEx plugin and the
  Workshop Companion.
- The Companion now shows **CONNECT UPDATE REQUIRED** and the GitHub download
  button when it finds a Connect runtime of a different version.

## 0.1.30 — Clean Workshop publication package

- Rebuilt the Workshop publication package with corrected dialog wording and
  matching release documentation.

## 0.1.29 — Workshop companion compiler fix

- Replaced the Workshop Companion's unsupported `UnityEngine.GUI`/`GUIStyle`
  overlay with People Playground's native `DialogBoxManager` dialog.
- The missing-runtime dialog now compiles in the stock People Playground mod
  compiler and provides an **OPEN CONNECT ON GITHUB** button.

## 0.1.28 — Complete map-sync package

- Published the final plug-and-play package for the map-follow update with
  updated host/friend instructions, missing-runtime popup documentation and the
  v4 protocol metadata.

## 0.1.27 — Map follow and Workshop recovery

- Added protocol v4 `MapLoad`: after the host starts a session, all connected
  guests automatically load the host's locally installed People Playground map
  through the game's own `MapLoaderBehaviour`. Clients remain synchronising
  rather than falsely entering a session from the title menu.
- Remote cursor presence now begins immediately after the Steam Relay handshake
  and is no longer gated behind the old local-only session flag.
- Reworked the Workshop Companion missing-runtime notice into a visible popup
  with **OPEN CONNECT ON GITHUB** and **COPY LINK** actions.

## 0.1.26 — Workshop author identity

- Set the Workshop Companion `Author` to the active Steam Workshop username
  **Mercury**, removing the false author-mismatch prompt when it is uploaded
  from that account.
- Updated both the locally installed Workshop Companion and the public
  plug-and-play package.

## 0.1.25 — Public package link

- Published the complete Connect package through the public
  `ForCesCustom/PPG-Connect` repository.
- The missing-files recovery UI and the Workshop Companion now use the real,
  versioned HTTPS download URL. A blank legacy config value falls back to the
  published link instead of hiding the recovery controls.
- Added bilingual public repository documentation and release packaging notes.

## 0.1.24 — Continuous Use lease

- Added a bounded Begin / KeepAlive / End continuous-activation lease for a
  non-host player's configured `activateDirect` binding. Holding the binding
  now drives the host's ordinary continuous-Use path, which supports automatic
  vanilla firearms and other components that read `IsBeingUsedContinuously()`.
- A different player cannot silently take the same continuous-use target. The
  host releases the lease on key release, relay disconnect, object expiry or
  session cleanup. Different registered objects can still be used at once.
- Bumped the relay protocol to v3. Every player must update to this release.

## 0.1.23 — Complete package and selected-item keys

- The release archive now contains the tested BepInEx 5 x64 runtime files at
  the exact game-root layout, so a friend installs one complete ZIP by extracting
  it beside `People Playground.exe`.
- Preserved the base game's user-configurable Activate and Delete semantic input
  for a client's local right-click selection while world tools are gated. Those
  actions are sent as bounded requests and still execute only after host checks.

## 0.1.22 — Companion layout correction

- Corrected the release layout so `WorkshopCompanion/Scripts/` is retained as a
  directory in the archive instead of flattening its two source files.

## 0.1.21 — Release-package correction

- Rebuilt the release package with the full Workshop Companion source, manifest,
  README and thumbnail alongside the BepInEx runtime files. This is a packaging
  correction; the relay protocol remains version 2.

## 0.1.20 — Tab Catalog, Interactions and Icon

- Removed the duplicate **MY SPAWN MENU** card. Every player now uses their own
  normal People Playground Tab catalog; a connected client catalog spawn is
  converted to a bounded, host-authoritative spawn request with the selected
  stable key, flip state and world cursor position.
- Added host-validated vanilla direct **Use** plus context-menu **Activate** and
  **Delete** for registered Connect objects. The host range-checks the player's
  relayed cursor, enforces permissions and rate limits, and performs the action.
- Added Host Settings for guest Use/Delete and a separate guest-interaction
  budget. Protocol version is now 2 because the settings payload and message
  set changed.
- Replaced the small legacy header asset with a larger 512×512 transparent
  black/hot-pink/crimson Connect icon, so the rounded mark has no white corners
  or background artifacts in the panel header.
- This does not claim replicated projectile/damage behavior or generic Workshop
  context-menu support; these paths require dedicated state/event adapters.

## 0.1.19 — Bot World Guard

- Bots now wait for a loaded People Playground sandbox world and catalog before
  evaluating actions. Starting a Steam session from the title screen no longer
  permits a bot to instantiate a fallback prefab outside a playable map.

## 0.1.18 — Packaged Icon Fix

- Replaced the oversized package icon with the verified 512×512 black, hot-pink
  and crimson-red Connect mark. It is below the runtime's 1 MiB safety limit,
  so the panel header loads it instead of rejecting the image.

## 0.1.17 — Host-side Bot Brains

- Replaced the spawn-only bot loop with one host-authoritative brain per bot.
  Builder, Mover and Cleaner profiles vary their priorities, while every bot
  retains the same safe Spawn, Grab-and-Place and Cleanup capabilities.
- Bot grabs use the existing expiring physics lease and force controller; the
  object remains simulated and is never teleported by a bot.
- Bot cleanup is deliberately restricted to an old vanilla item that the bots
  themselves created and that has no active human or bot lease. Player-built
  objects and Workshop content are never selected for cleanup.
- Added a deterministic BotBrain smoke test that proves every profile can reach
  all three capabilities and falls back to wandering when no action is allowed.

## 0.1.16 — Workshop guide correction

- Updated the Russian friend-install guide to match the verified standard-loader behavior: Workshop Companion uses the supported People Playground notification banner, while the BepInEx runtime provides the full recovery panel.

## 0.1.15 — Workshop-safe startup detection

- Reworked Workshop Companion after a real People Playground loader test rejected `Application.dataPath` as a suspicious identifier.
- The BepInEx runtime now exposes a lightweight in-process marker. Workshop Companion checks that marker and shows recovery instructions when the external Connect runtime did not start.
- This preserves the standard loader's security boundary: no scanner patching, no filesystem bypass and no injected loader workaround.

## 0.1.14 — Recovery notice layering fix

- The missing-files recovery notice now renders above the F8 panel, so it remains readable and actionable even when Connect is already open.

## 0.1.13 — Installation recovery and Workshop Companion

- Added an in-game missing-files recovery notice to Connect. It validates the BepInEx core, Connect folder, Connect DLL and bundled icon before offering safe recovery actions.
- Added a separate standard People Playground `WorkshopCompanion` layout. It can be published to Steam Workshop and explains why the external BepInEx package is still required when Workshop alone is insufficient.
- Added `FRIEND_INSTALL_GUIDE_RU.txt` with the exact installation, verification, invite and troubleshooting flow for another player.
- The recovery UI only enables GitHub open/copy actions after a real HTTPS GitHub Releases URL is set by a release publisher; it never sends players to an invented repository.

## 0.1.12 — Black, pink and crimson Connect icon

- Replaced the cyan Connect mark with a high-contrast black, hot-pink and crimson-red rounded icon.
- Kept the connection glyph and compact in-menu presentation; the asset is loaded once and destroyed on plugin unload.

## 0.1.11 — Connect icon

- Added a polished rounded cyan Connect icon to the in-game panel header.
- The icon is packaged as `connect-icon.png` beside the plugin DLL and is
  safely loaded once, with a size limit and cleanup on plugin unload.

## 0.1.10 — Release build correction

- Rebuilt the release DLL directly against the exact local game and BepInEx
  assemblies, without the machine's incomplete MSBuild reference-pack fallback.

## 0.1.9 — Settings layout and lobby-log correction

- Expanded the Host Settings panel so all server toggles remain visible and
  clickable above the network status card.
- Corrected the lobby-created log to report the selected privacy type instead
  of always writing `Friends`.

## 0.1.8 — Host and player Settings

- Added a rounded **SET** panel with separate Player and Host tabs.
- Player settings persist locally: remote names, Steam avatars, cursor scale,
  cursor smoothing and cursor send rate.
- Host settings persist locally: Physics2D velocity/position iterations,
  authoritative snapshot rate, Connect object cap, guest spawn rate cap, bot
  spawn cap and guest spawn/grab/bot permissions.
- Physics2D iteration changes apply only while this player is the Connect host,
  and the original game values are restored during host cleanup.
- Host values are sent reliably to connected players as read-only session
  information. Added a non-disconnecting action-denied protocol response for
  rejected gameplay requests.

## 0.1.7 — Movable persistent panel

- The Connect panel can now be dragged by its header.
- Saved X/Y coordinates are clamped to the screen and persisted through BepInEx
  configuration, including after a panel close/open or game restart.

## 0.1.6 — Per-player Spawn Menu

- Replaced the ambiguous shared-looking spawn control with **MY SPAWN MENU** on
  every player's own Connect panel.
- Added local vanilla preset buttons and a local typed key field. Client item
  choices are sent as a host-validated Spawn Request and appear for every peer
  through the authoritative Spawn event.

## 0.1.5 — Bot Mode

- Added a host-only Bot Mode with one to three autonomous, coloured world-space
  cursors.
- Bots wander near the host's current world location and create only allowed
  vanilla spawnables through the host authority, capped at 36 spawns per session.
- Added a bounded reliable Bot Mode notification for relay clients and protocol
  tests for bot cursor flags and the control envelope.

## 0.1.4 — Connect

- Renamed all player-facing UI, lobby text, diagnostics, documentation and the
  release package to **Connect**.
- The BepInEx plugin GUID remains stable so the rename installs as an update,
  not a separate Steam session plugin.

## 0.1.3 — Cursor reliability and visual identity

- Added an eight-colour, high-contrast peer palette: host gold plus seven unique
  cursor colours for the supported eight-player lobby maximum.
- Reworked remote cursors into animated colour rings with centre dots, Steam
  avatar, player name, UI-busy state and grab pulse.
- Added bounded world-space cursor codec tests, stale-sequence rejection and
  disconnect cleanup for cursor state.
- Cursor state now sends a low-rate UI-busy heartbeat while a player has the
  menu open instead of disappearing to remote peers.

## 0.1.2 — Avatar orientation

- Corrected vertical Steam-avatar orientation while the RGBA data is moved into
  the Unity texture cache. The lobby and remote-cursor avatar share that upright
  texture; no per-frame flip is performed.

## 0.1.1 — Rounded multiplayer UI

- Rebuilt the F8 panel around rounded, card-based Steam-lobby controls.
- Added animated menu entry, button/card hover expansion and pulsing status dots.
- Added visual member cards, avatars, host/session badges and animated Steam
  invite slots.
- Improved spacing and contrast for lobby creation, max-player controls, spawn
  requests, network notices and leave-session actions.

## 0.1.0 — BepInEx edition

- Added a BepInEx 5 plugin using the game's existing Facepunch Steamworks context.
- Added Friends Only Steam lobby creation, metadata, member display and official
  Steam invite chooser.
- Added invite callback and safe numeric `+connect_lobby` parsing.
- Added SteamNetworkingSockets relay transport, protocol handshake and version
  rejection reasons.
- Added independent world-space cursors, simple Steam avatar cache, host grab
  leases, and root Rigidbody2D snapshot replication for registered post-start
  vanilla spawns.
- Added a narrow client-only Harmony input patch documented in PATCHES.md.
- Documented the unsupported world-sync and two-account-test boundaries.
