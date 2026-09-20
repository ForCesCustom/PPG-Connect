Connect — BepInEx edition v0.1.48
================================

Package:
https://github.com/ForCesCustom/PPG-Connect/raw/main/Releases/Connect-v0.1.48.zip

Target: People Playground 1.27.17 (Steam build 24793773),
Unity 2020.3.1f1 Mono x64, BepInEx 5 Unity.Mono-win-x64.
Protocol: 10. All players need the same Connect version and game/content.

INSTALL
1. Fully close People Playground.
2. Extract the ENTIRE archive beside People Playground.exe; merge BepInEx
   and Mods folders. The archive includes the loader and Connect.
3. Launch through Steam. F8 opens Connect; F10 opens diagnostics.
4. Host creates a lobby, invites through Steam Overlay, then starts/selects a
   map. Guests load the host map automatically. Wait for PLAYING.
Do not replace game executables/managed DLLs, add Steam DLLs or remove unrelated
plugins. Keep connect-icon.png beside Connect.BepInEx.dll.
Mods/Connect is the native Mods-menu companion; multiplayer uses BepInEx.

MENU AND CONTROLS
The adaptive Connect panel includes player cards, readable connection/map status,
scrolling and separate player/host settings. The header can be dragged.
Camera, zoom, Tab catalogue and selection are local to each player.
Use Tab for host-authoritative spawns; hold LMB to drag a registered object.
Activate/Delete and Freeze/No Collide/Weightless/Ignite request host actions.
Clear, shared host Undo, pause, slow motion and supported environment changes
are host-validated requests. Guest permissions and request limits still apply.
Copy/Save/Follow are local operations; unsupported Paste/Load/custom world
actions remain blocked. Copy does not imply that guest Paste is supported.

SHARED WORLD IN 0.1.48
Sprite sort order/layer and authored light/floodlight state are transmitted.
Wire views support complete multipart sets up to 1000 lines. An incomplete
revision retains the previous complete view, not a truncated replacement.
Cached eligible root lists, protected reliable queues and disposable snapshot
coalescing reduce repeated work. Float encoding no longer allocates per value.
Native removal now releases object/device caches immediately.
Host spawn/remove observation survives the game's ModAPI.ClearEvents call.
State layouts use catalogue-authored nodes, ignoring added effects/outlines.
Validated original catalogue keys improve existing-object discovery; map
transitions wait for old roots to be destroyed before resuming discovery.
World epochs reject old packets after map changes, including same-map reloads.
Recognisable existing catalogue objects are discovered on the host. Bounded
repair passes resend registered spawns; full presence lists remove old replicas.
Typed state includes poses, scale, supported sprite/collider state, physical
properties and selected limb/skin values. Cached part references allow existing
limbs to keep following the host after detachment.
Host wire lines are visual only; new-wire discovery may take 0.5 seconds.

LIMITS
Read KNOWN_LIMITATIONS.md. Support is bounded to 256 cached nodes per root.
Existing damaged/restructured objects may not match a fresh catalogue prefab.
Arbitrary Workshop behavior, new runtime hierarchy nodes, complete wound
textures, projectile/explosion effects and guest-created wires are not fully
replicated. Host wires are visual replicas, not guest-side electrical simulation.
The configured object cap does not intercept every native host spawn path.
There is no host migration or public lobby browser.

VERIFICATION
Use the same v0.1.48 package on two separate Steam accounts. Compare distinct
Tab items, drag, deletion, injury state, supported context actions, pause,
join-in-progress and same-map reload. Build/codec tests do not establish a live
two-account pass.
Logs: <People Playground>\BepInEx\LogOutput.log
Russian instructions: FRIEND_INSTALL_GUIDE_RU.txt
Measured checks and DLL SHA-256: SYNC_TEST_REPORT_v0.1.48.md
