# Connect 0.1.48 verification — 2026-09-20

## Delivered scope

Protocol 10 extends host presentation to sprite sorting order/layer, closed
catalogue-authored LightSprite brightness and SingleFloodlight activation,
and atomic complete wire views up to 1000 lines. No arbitrary component
serialization, remote code, asset download or replay of device Use callbacks.
Guest wire creation/electrical simulation and arbitrary Workshop effects
remain unsupported; see KNOWN_LIMITATIONS.md.

Network work now uses membership-cached sorted roots and separate eligible
skin/device lists. No ready matching-map receivers means no state capture.
Float encoding avoids an allocated byte array per scalar. Independently
replaceable queued Object/Wound/Device states coalesce by full identity,
preserving fairness and rejecting invalid/stale replacements. Reliable
receive backpressure prevents silently losing Spawn/Despawn at queue capacity.
Native item removal and host deletion explicitly release replication caches.

## Build and ten executable suites

Production x64 build passed against installed People Playground 1.27.17,
Steam build 24793773, Unity 2020.3.1f1 and BepInEx 5.4.23.5.
Reproduce: `Dev/BuildAndTest.ps1 -GameRoot <verified game root>`.

- BotBrain: PASS.
- Protocol: PASS, including 10,000 malformed packets and message/channel tests.
- WireWriter: 10,011 IEEE byte-equivalence cases, PASS.
- InstallationHealth: PASS with the exact v0.1.48 download link.
- ObjectState: 156 checks, including signed sprite layer/order, PASS.
- WorldLifecycle: PASS.
- SharedWorld: 29,032 checks, including 1000-wire multipart snapshots,
  loss/reorder/duplicate/stale/revision-wrap/empty-clear handling, PASS.
- WoundState: 48 checks, PASS.
- DeviceState: 10,051 checks, PASS.
- ReplicationQueue: 11,615 checks against actual production source, PASS.

Only isolated test builds emit unused-field warnings; production compiles.

## Measured optimization (not a game FPS benchmark)

The reproducible writer microbenchmark encodes 20,000 packets of 64 floats,
5,120,000 output bytes, after warming both paths. On this PC the old
per-float-byte-array path took 14–15ms and 8 Gen0 collections; the stack-bit
path took 6ms and 1 Gen0 collection. Output bytes are equal, including special
IEEE values in the separate correctness test. This removes 1,280,000 temporary
four-byte arrays in that benchmark. It does not establish a corresponding
whole-game FPS gain, lower two-PC latency or higher Steam throughput.

Queue tests also demonstrated 10,000 updates of one pending object/chunk
occupying one slot with the latest state, without merging distinct limbs,
epochs, sessions or senders. Multipart wire pages remain separate FIFO entries.

## Actual engine: 1377 assertions passed

Launched a fresh opted-in process through Steam with the final production DLL.
Five harnesses ran against the actual installed game, not a simulated peer:

- Object/wound state: **846**, real Human/Android/Metal Cube, including sprite
  layering, cached detached/missing parts, wounds and delayed registration
  with 300 added effect nodes per fixture.
- Production SpawnAsset: **196**, six requested/selected-asset and flip cases.
- Native host spawn: **52**, real private one-member Steam lobby, events after
  ClearEvents, exact registration/payload, duplicate/removal behavior, released
  cached layouts and exception-safe catalogue scope.
- Production wire receive: **270**, 130 real LineRenderers, multipart atomic
  visibility, dropped/reordered/stale pages, endpoints/colors and complete clear.
- Device capture/apply: **13**, the actual Flashlight catalogue prefab; host
  authority, activation parity, invalid layout and unchanged prefab checks.

The first wire assertion incorrectly compared native color readback with an
unquantized input. Unity returns green 0.3019608 for input 0.3. The final test
compares Connect exactly to a separate native LineRenderer assigned identical
values; their width and colors match. No production tolerance was loosened.

No authored LightSprite fixture was found in this installed catalogue. Its
brightness codec is covered, but this run does NOT establish in-engine
brightness parity for a custom authored LightSprite. Runtime-created lights
remain outside the canonical node schema.

Build developer plugins with `Dev/BuildRuntimeSmoke.ps1 -GameRoot <game root>`.
Temporary plugins: ConnectSmoke.dll, ConnectDeviceSmoke.dll and
Connect.HostSpawnRegression.dll. Use a fresh isolated Steam process with
`-connect-replication-smoke -connect-host-spawn-smoke`. The driver loads Default,
creates/leaves only its private test lobby and exits. Check all five PASSED
markers in Player.log, then remove the test plugins. None is in the release ZIP.

## Installation and hash

Game: `D:\SteamLibrary\steamapps\common\People Playground`.
Plugin: `BepInEx\plugins\Connect\Connect.BepInEx.dll` beneath that root.

SHA-256 of production build, release-contained DLL and installed DLL:
`017EA0F57894D0CAF8AFA4823EE849B343F80558AD0DEB88B26CFBE2CB6FF442`

Original game EXE/Assembly-CSharp.dll and the user's DirectControl.dll were
left unchanged. Old Connect files and original logs were backed up; only
test-owned plugins/config were moved out after testing. The game's own
missing ppgModCompiler/config.json diagnostic persists; no game files were
changed to suppress it.

## Two-PC acceptance still required

Both players must install the complete v0.1.48 ZIP and restart. Create a new
lobby, wait for PLAYING, then compare host/guest spawn types, body poses and
dragging; change host sprite layers, activate Flashlight, draw over 128 wires,
delete/clear/reload and rejoin the populated world. Test with matching content.

There was no second Steam account, relay-loss experiment, complete Workshop
compatibility pass or whole-game FPS measurement. Universal instantaneous
pixel-identical synchronization is not claimed.
