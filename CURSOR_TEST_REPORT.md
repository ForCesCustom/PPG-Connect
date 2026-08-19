# Connect cursor, Tab Catalog, interactions, Bot Mode, movable panel and Settings verification — 0.1.33

Executed on 2026-08-19:

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

The live route is host-authoritative: clients submit their own world-space
cursor; the host validates the Steam transport identity and relays it with the
source peer ID.  Peer ID 0 is explicitly accepted on clients, so the host's
gold cursor is not discarded.

Not executed here: a real two-account Steam relay session.  That requires a
second Steam account and separately isolated game instance.  Therefore this
report does not claim that a remote Unity cursor has been visually observed in
a live two-player session.
