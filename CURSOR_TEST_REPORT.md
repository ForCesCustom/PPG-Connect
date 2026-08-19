# Connect cursor, Tab Catalog, interactions, Bot Mode, movable panel and Settings verification — 0.1.39

Executed on 2026-08-19:

- Full BepInEx project rebuild after the automatic title-menu-to-`Main`
  sandbox transition and pre-ready world-packet gate: passed.
- The local build settings were checked: the available game scenes are `Menu`,
  `Main`, and `Map Editor`; `Main` is the scene reached by the confirmed normal
  `SceneSwitchBehaviour` map route. The fallback therefore never consumes a
  scene name from Steam or from a client payload.
- A post-fix two-account Steam run is still required. The local environment
  has one Steam account, so this report does not claim that the v0.1.39 guest
  transition or guest Tab-spawn route was runtime-verified with a second user.

- Full plugin compile with the locally installed People Playground and BepInEx 5
  assemblies: passed.
- Stand-alone protocol smoke tests: passed (exit code 0).
- Packet fuzz loop: 10,000 arbitrary payloads passed through the envelope and
  cursor decoders without an exception.
- Cursor payload round trip: passed for world position, velocity, button state
  and UI-busy state.
- Truncated payload rejection, NaN float rejection and sequence-wrap behaviour:
  passed.
- Palette verification: peer IDs 0 through 7 resolve to eight distinct colours.
- Bot cursor flag and reliable Bot Mode envelope: passed.
- BotBrain smoke test: passed. Each Builder, Mover and Cleaner profile can
  choose Spawn, Grab-and-Place and Cleanup when safe targets are available;
  every profile falls back to Wander when no action is allowed.
- Bounded reliable HostSettings and non-disconnecting ActionDenied envelopes:
  passed.
- Protocol v3 interaction envelope and the expanded fixed 12-byte HostSettings
  envelope: covered by the protocol smoke test.
- Continuous Use lease unit coverage: passed. It accepts the owning peer,
  rejects a second peer on the same object, renews only the owner's lease,
  drives one fixed-step callback and releases cleanly on End.
- Full BepInEx project rebuild against the local game assemblies: passed. The
  only build message is the pre-existing MSBuild warning that .NET 4.0 reference
  assemblies are not installed; the project resolves its game references and
  emits Connect.BepInEx.dll successfully.
- Release package validation: passed. The ZIP has the expected game-root
  layout, includes the built plugin and icon, and excludes game/Steam DLLs,
  debug symbols and development output.
- `MapLoad` protocol v4 envelope/string round-trip: passed. The implementation
  was compiled against the local public `MapLoaderBehaviour.Load()` and
  `MapLoaderBehaviour.CurrentMap` signatures.
- The host-map scene path was inspected against the local `MapViewBehaviour.Select`
  and `SceneSwitchBehaviour.Switch` method bodies. It now uses the same async
  scene transition as the normal People Playground map UI; a post-fix
  two-account visual run is still required.

The live route is host-authoritative: clients submit their own world-space
cursor; the host validates the Steam transport identity and relays it with the
source peer ID.  Peer ID 0 is explicitly accepted on clients, so the host's
gold cursor is not discarded.

`ClientMapStatus` protocol v5 envelope/string round-trip is covered by the
protocol smoke test. The live host card updates only after the host validates
the relay identity, peer ID and host-selected map. The client Tab-spawn route
now has event-level logs for interception, host receipt/broadcast and guest
instantiation; a new two-account spawn test remains required.

Not executed here: a new real two-account Steam relay session. That requires a
second Steam account and separately isolated game instance. Earlier manual
testing reported working remote cursor movement and guest map entry, but this
  report does not claim that the v0.1.37 Tab-spawn/baseline change has been
  verified on two accounts.
