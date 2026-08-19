# Known limitations — Connect BepInEx edition v0.1.38

- Every player must extract the same complete Connect ZIP into the game root.
  It already contains BepInEx 5 x64, but Connect remains a non-standard loader
  with Harmony patches rather than a normal People Playground source mod.
- This environment cannot run a new two-account Steam test. The prior manual
  test established relay cursor movement and automatic guest map entry; the
  v0.1.36/0.1.37 host-status, Tab-spawn and post-start baseline changes are compile/protocol tested but still
  require the next host-and-friend runtime test. The new `[Connect][Spawn]`
  records identify the exact failed stage without logging cursor/snapshot spam.
- Join-in-progress does not reconstruct pre-existing map objects. Start with an
  empty map, create the lobby, press **START & SYNC MAP** and, if needed, choose
  the host map; then use the normal Tab catalog for objects expected to
  replicate.
- A joining or delayed guest now receives a reliable baseline of objects created
  after session start once it reports `PLAYING`, but objects present before the
  session started are still intentionally outside this first world-transfer
  implementation.
- Replication is limited to post-start vanilla spawnables with a root Rigidbody2D
  and their root pose/velocity. Existing objects, ragdoll limbs, dismemberment,
  joints, wires, custom components, explosions, projectile/damage state,
  freeze, rotate, undo and save/load are not supported. Map selection and map
  changes now follow the host by installed `Map.UniqueIdentity` through the
  game's normal sandbox scene transition, but the map itself must exist locally
  and its pre-existing objects still are not rebuilt.
  Direct
  vanilla Use (including host-side continuous Use for automatic weapons) plus
  context Activate/Delete are supported only for a registered Connect root;
  arbitrary context buttons from the game or Workshop are not.
- A remote player can request a configured vanilla spawnable by its stable
  catalog name. There is no mod-set manifest comparison or Workshop download;
  use vanilla content for v0.1.0.
- Public lobbies use Steam's lobby visibility only; no lobby browser, text chat,
  kick UI, Rich Presence or host migration is implemented.
- Bot Mode is host-only and intentionally limited to three bots. Its vanilla
  spawn cap is configurable by the host from 0 to 100 per session. Bots do not
  join the Steam lobby or use a Steam avatar. Bots can spawn, use the same
  host-authoritative lease to grab/place, and clean up only their own old
  unleased vanilla items; they never touch player-built items, Workshop
  content, activation, wires or arbitrary delete targets.
- The normal Tab catalog is now the only spawn UI. The session must use the
  same catalog content: there is no mod-set manifest comparison or Workshop
  download, and unknown/custom spawnables are not guaranteed to resolve.
- The host validates relay identity, lobby membership, protocol/game/mod version,
  nonce, bounded packet length, finite coordinates, and a host-side overlap test.
  This is not a claim of complete anti-cheat coverage.
- The Host Settings panel covers Connect's currently implemented spawn/grab/use/
  delete/bot rules, solver iterations and snapshot budget. It does not imply
  support for arbitrary custom actions, wires or a full Steam-server
  browser.
- If the Harmony target changes in a future game build, client vanilla world tools
  remain enabled rather than applying a broad input patch. Do not use this build
  on a different People Playground version without validating PATCHES.md.
