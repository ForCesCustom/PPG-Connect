# Connect 0.1.46 verification — 2026-09-06

## Build and automated checks

Built all production Source files with .NET Framework 4 compiler, x64, using
installed People Playground 1.27.17 / Unity 2020.3.1f1 and BepInEx 5.4.23.5.
Reproduce: `Dev/BuildAndTest.ps1 -GameRoot <verified game folder>`.

All seven smoke suites passed: BotBrain, Protocol (including 10,000 malformed
packets and all message/channel combinations), InstallationHealth,
ObjectState (137 checks), WorldLifecycle, SharedWorld (1198 checks), and
WoundState (48 checks). Standalone tests emit unused-field compiler warnings;
the production build completed without errors.

Final production DLL SHA-256:
`B56975FC01F48E61144607EAB0A271C651F26D2D662FE0DC0C76F25B2CEB2C21`

## Actual engine and menu verification

Launched the installed game through Steam. BepInEx loaded Connect 0.1.46 and
reported successful Harmony patch installation. Opened the new menu with F8,
loaded Default, created a one-member lobby, and started the host session.
Inspected setup, lobby, player settings, host settings and its scrollable final
permission rows at 1920x1080. No menu overlap was observed. Scrolling also
affected native camera zoom; a narrow ZoomCamera guard was subsequently added
and compiled, but that final guard was not visually retested.

The opt-in RuntimeReplicationSmoke harness passed **246 assertions on three
real Unity fixtures**: Human, Android and Metal Cube. It checked encode/apply
of cached poses, scale, physical/limb/sprite state, detached/missing parts and
invalid-state rejection. It cleaned only its own fixtures.

That engine pass predates the final wound extension and detached-grab helper
integration. The extended harness and a separate production SpawnAsset-path
harness were compiled but not rerun in-engine. They are developer-only and
are not shipped in the release ZIP.

## What is not established

This is NOT a two-account Steam multiplayer pass. No second account was
simulated. Network latency/loss, late joining and full guest action behavior
still require host-and-friend verification with this exact package.
See KNOWN_LIMITATIONS.md for unsupported dynamic objects, tools and effects.
Universal or instantaneous pixel-identical synchronization is not claimed.

## Two-PC acceptance checklist

1. Both players install the complete 0.1.46 ZIP; check the F8 version footer.
2. Host starts a fresh Default session; guest reaches PLAYING on that map.
3. Host selects Human while guest requests Android, then Metal Cube; reverse
   roles. Confirm matching object types/counts, mirrored spawn and positions.
4. Drag bodies and detached limbs with DragTool and held LMB. Release, open
   Connect, and change tool: the lease must stop, not remain stuck.
5. Damage/burn a human on the host; compare poses, missing limbs, skin marks,
   health and scale. Try guest Activate/Delete/Freeze/NoCollide/Weightless/Ignite.
6. Check pause/slow motion/environment, clear and host-history Undo. Confirm
   permission denials do not create client-only changes.
7. Rejoin a populated world and reload the same map. Look for leftovers,
   missing objects, layout warnings and send failures in both Connect logs.
