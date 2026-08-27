# Known limitations — Connect BepInEx edition v0.1.45

- Every player must extract the same complete Connect ZIP into the game root.
  It already contains BepInEx 5 x64, but Connect remains a non-standard loader
  with Harmony patches rather than a normal People Playground source mod.
- This environment cannot run an automated two-account Steam test. The manual
  relay, handshake, map-load and spawn-request test established that current
  People Playground can expose temporary catalog keys such as `0` or `zzzzz`.
  v0.1.44 also scopes People Playground's local `SelectedItem` during the
  networked instantiation path, so each peer creates the catalog asset carried
  by the authoritative Spawn rather than its own locally selected Tab item.
  v0.1.45 additionally blocks unsupported guest Context Menu and Clear actions,
  gates input during a host map transition, and bounds snapshot traffic. A real
  two-account run is still required to confirm representative Tab spawns.
- Join-in-progress does not reconstruct pre-existing map objects. Start with an
  empty map, create the lobby, press **START & SYNC MAP** and, if needed, choose
  the host map; then use the normal Tab catalog for objects expected to
  replicate.
- A joining or delayed guest receives two bounded reliable reconciliation passes
  of objects created after session start once it reports `PLAYING`, but objects
  present before the session started are still intentionally outside this first
  world-transfer implementation.
- Replication covers post-start vanilla spawnables with root and nested
  Rigidbody2D host poses, including compound-object grab motion. It does not
  serialize all component state; biology/dismemberment topology, joints, wires,
  damage and explosions remain outside the protocol.
  Existing objects, ragdoll biology/dismemberment, joints, wires, custom components,
  explosions, projectile/damage state,
  freeze, rotate, undo and save/load are not supported. Map selection and map
  changes now follow the host by installed `Map.UniqueIdentity` through the
  game's normal sandbox scene transition, but the map itself must exist locally
  and its pre-existing objects still are not rebuilt.
  Direct
  vanilla Use (including host-side continuous Use for automatic weapons) plus
  context Activate/Delete are supported only for a registered Connect root;
  arbitrary context buttons from the game or Workshop are not. On a connected
  guest, unsupported context buttons and Clear Everything/Clear Living/Clear
  Debris are deliberately blocked rather than executed locally.
- A remote player can request a configured vanilla spawnable by its stable
  catalog name. There is no mod-set manifest comparison or Workshop download;
  use vanilla content for v0.1.0.
- Public lobbies use Steam's lobby visibility only; no lobby browser, text chat,
  kick UI, Rich Presence or host migration is implemented.
- Bot Mode is host-only and intentionally limited to three bots. Its vanilla
  spawn cap is configurable by the host from 0 to 100 per session. Bots do not
  join the Steam lobby or use a Steam avatar. They build a bounded local model
  of the map and can classify installed catalog content, but full simulation of
  arbitrary Workshop component semantics is not claimed. They may activate
  compatible registered Connect roots through the host's vanilla Use path,
  grab/place registered roots through the same lease system as players, and
  clean only their own old unleased creations. They never delete player-built
  items, emit arbitrary context actions, create wires or manipulate files.
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
