# Connect 0.1.47 verification — 2026-09-07

## Confirmed failure and correction

Inspected the installed People Playground 1.27.17 assembly and the user's
previous 0.1.46 host log. Native host spawns were not observed immediately,
although guest requests were registered through their separate path.
`CatalogBehaviour.Populate -> ModificationManager.InvokeMain ->
ModAPI.ClearEvents` clears the ordinary spawn/remove delegates. Connect now
patches the stable InvokeItemSpawned/InvokeItemRemoved methods directly.

The old fallback also compared whole live hierarchies: a Human with native
effects had 120/188 nodes versus its 26-node prefab. Layouts now use local
catalogue-authored nodes and component schemas. Runtime effect children do
not change layout slots or hashes. Explicit validated original asset markers
improve lifecycle discovery; post-map discovery waits for outgoing destruction.

## Build and executable checks

Production build: PASS, .NET Framework compiler, x64, installed game managed
assemblies and BepInEx 5.4.23.5. No game executable or managed DLL was patched.

Reproduce: `Dev/BuildAndTest.ps1 -GameRoot <verified game folder>`.
All seven suites passed: BotBrain, Protocol (including 10,000 malformed
packets and channel/type checks), InstallationHealth, ObjectState (147),
WorldLifecycle, SharedWorld (1198), WoundState (48).
Unused-field warnings are from isolated test builds, not production errors.

Production DLL SHA-256 (build, release archive and local installation):
`0BE2017444DA98C4172F63C92115C25C1C3904D9B59C75525F84ED27BACD49F0`

Verified local game: `D:\SteamLibrary\steamapps\common\People Playground`.
Installed plugin: `BepInEx\plugins\Connect\Connect.BepInEx.dll` under that root.

## Real-engine regression results

Launched through Steam in a fresh explicitly opted-in test process. All three
developer harnesses passed, **1035 assertions** total:

- Object/wound state: **790** assertions on real Human, Android and Metal Cube
  fixtures: poses, scales, physical/limb/sprite state, detached/missing parts,
  wound points/age/clearing, malformed state rejection. Delayed registration
  after native Start plus 300 added effects produced raw counts 406/390/306
  but stable authored counts 26/20/1; fresh replicas accepted the state.
- Actual production `SpawnAsset`: **196** assertions over six calls with
  different selected/requested assets, both flip directions, requested pose,
  visible sprite identity and restored local catalogue selection.
- Actual native host `CatalogBehaviour.Spawn`: **49** assertions in a real
  private one-member Steam lobby. Human/Android/Metal Cube registered
  immediately even after `ModAPI.ClearEvents`; exact payload ID/key and
  primed layouts matched. Duplicate events did not duplicate identity;
  removal remained observable. A deliberately throwing owned event subscriber
  did not prevent registration and the Harmony finalizer cleared spawn scope.

The first harness run exposed a test assumption: native Human Awake randomizes
height, so exact prefab scale magnitude is not invariant. The test now checks
the requested flip sign and the actual native random size range. Destructive
fixture checks also disable only their own simulation callbacks after Start;
otherwise native actors access limbs deliberately removed by the harness.
These harness changes did not suppress production exceptions.

Build harnesses with `Dev/BuildRuntimeSmoke.ps1 -GameRoot <verified folder>`.
Temporarily install its two DLLs separately from Connect and launch a fresh
Steam game process with `-connect-replication-smoke -connect-host-spawn-smoke`.
The driver loads Default, waits for fixture checks, creates/leaves only its
own private lobby, then exits. It refuses an existing lobby or another member.
Check all three PASSED markers in Player.log/BepInEx log, not only the driver's
result. Remove developer test plugins afterwards; they are NOT in the ZIP.

## Limits and unrelated diagnostics

This is **not a two-account end-to-end Steam test**. No remote player or
transport was emulated. Real relay delivery, latency/loss, late join and full
guest interaction still need the friend with this exact 0.1.47/protocol 9 build.
Universal Workshop/dynamic-object synchronization is not claimed.

The game's own compiler reported a missing `ppgModCompiler/config.json`; one
test shutdown also logged Process.Kill from CompilerServerBehaviour. These
are separate game-compiler diagnostics, not Connect assertion failures.
No original game files were changed to suppress them.

## Two-PC acceptance

1. Both install the entire Connect-v0.1.47.zip and restart; check F8 version.
2. Create a new lobby, start Default and wait for guest PLAYING.
3. Host spawns Human, Android and Metal Cube; guest must see all three. Then
   reverse roles with different Tab selections and both flip directions.
4. Compare poses/size and drag bodies/detached limbs with held LMB; test release,
   deletion, supported context actions, wounds, clear and same-map reload.
5. Rejoin a populated host world; check both logs for missing IDs/layout errors.
