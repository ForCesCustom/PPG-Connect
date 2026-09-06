# Connect relay protocol v8

Version 0.1.46 uses the game-supplied Facepunch SteamNetworkingSockets context.
All peers must agree on protocol, Connect version and game version. Payloads
are typed bounded records; files, CLR object graphs, method names and remote
save deserialization are absent.

## Envelope and authority

All fields are little-endian. The fixed header is 30 bytes.

| Offset | Bytes | Field |
|---:|---:|---|
| 0 | 4 | Magic 0x54475050 |
| 4 | 2 | Protocol version 8 |
| 6 | 1 | Message type |
| 7 | 1 | Logical channel |
| 8 | 8 | Session nonce |
| 16 | 2 | Peer ID |
| 18 | 4 | Sequence |
| 22 | 4 | Tick |
| 26 | 4 | Payload length |
| 30 | N | Payload |

Maximum packet size is 49,152 bytes; strings are bounded to 256 UTF-8 bytes.
The decoder rejects a message on the wrong designated channel. Handlers
validate exact lengths, finite/bounded values and authenticated sender role.

Every **World** and **Snapshot** payload starts with a uint32 world epoch.
The receiver removes and validates this prefix before decoding the message.
Late packets from previous maps, including a reload of the same map, are
discarded. Steam lobby metadata carries only map/session directives, not world
objects.

## Channels

- Control: reliable Hello/Welcome/Reject, SessionStarted/Ending, MapLoad,
  ClientMapStatus, BotMode and HostSettings.
- World: spawn/despawn, grab leases, InteractionRequest, ActionDenied,
  WorldManifest and WorldCommand. State-changing events are reliable; grab
  movement uses disposable updates.
- Snapshot: ObjectState, GlobalState, WireVisual and WoundState. Legacy Snapshot and
  RigSnapshot IDs remain recognised. Disposable queues retain recent state.
- Cursor: independent world-space cursor data; camera and menu state stay local.

## World and map records

- MapLoad (22): uint32 epoch followed by installed map identity string.
  Clients resolve only locally installed maps and verify the actual loaded
  root. Repeated directives for the same epoch are ignored.
- ClientMapStatus (23): uint32 epoch, status byte and map identity string.
  Host validates both epoch and map before accepting PLAYING and scheduling
  a reliable baseline.
- SpawnRequest (11): catalogue key, world X/Y and flip flag. The host validates
  availability/permission/rate and creates the requested asset.
- Spawn (12): root ID, catalogue key, position, rotation and local scale.
  Each guest instantiates its local matching asset and primes its initial
  node layout before state application. Despawn (13) carries a root ID.
- WorldManifest (26): epoch, uint64 high-water ID, uint16 count and up to
  1000 unique nonzero live root IDs. This inner epoch is additional to the
  World-channel prefix. Only a complete valid manifest removes absent replicas
  at or below its high-water ID; newer spawns survive an older manifest.
- Reliable spawn baselines run in bounded passes after PLAYING and repeat
  after completion with an idle interval. An active pass is not reset by
  periodic repair, so large worlds can finish.

## Typed state

ObjectState (25) carries a registered root ID, initial layout fingerprint,
bounded total/offset and a chunk of typed node state. Layouts are limited to
256 nodes per root. Initial node references remain cached when host limbs
detach; received paths are not resolved against a reordered hierarchy.

Supported fields cover poses, scale, active/renderer/collider state and the
explicit physical, limb and skin values encoded by ReplicatedObjectState.
Unknown schemas/layouts, invalid counts and nonfinite fields are rejected.
A layout mismatch is reported instead of applying values to unrelated nodes.
Per-root/per-chunk tick tracking rejects older updates.

GlobalState (27) carries bounded pause/slow-motion and supported environmental
settings. WireVisual (28) carries up to 128 bounded line records for a guest
visual representation; it does not authorize guest wire creation or electrical
simulation. Large object-state batches resume across ticks instead of always
restarting from their first parts.

## Player requests

WoundState (30) carries root ID, cached layout fingerprint, uint16 node index,
track-age boolean and up to 128 skin points. Each point has local X/Y,
intensity, fixed damage kind (0–5), and bounded age translated to local game
time before the native skin Sync method. Empty records clear healed wounds.
Packets are capped at 2304 bytes and sequenced per (root, node). Periodic
bounded round-robin replacement also repairs loss and joining clients.

GrabBegin names the root and world point. The host checks the sender's cursor,
a collider belonging to that root and a bounded 1.35-unit delay tolerance,
then issues a short lease renewed by GrabUpdate and ended by GrabEnd.

InteractionRequest (21) uses a fixed action enum plus registered root ID.
Activate/Delete, continuous activation begin/renew/end, Freeze, NoCollide,
Weightless and Ignite are handled by explicit host code after permission,
range and rate validation.

WorldCommand (29) uses fixed command bytes: Clear Everything/Living/Debris,
pause, slow motion, host-history Undo and whitelisted environment fields.
Clear/Undo use delete permission; pause/environment and other applicable
controls use activation permission. No peer supplies a method name.

HostSettings (19) communicates the bounded host policy; guests cannot change
it. ActionDenied (20) explains a denied request without closing the relay.

## Scope

There is no arbitrary Workshop synchronization, asset/file transfer, automatic
mod download, complete wound/particle/projectile replication, dynamic object
graph reconstruction or host migration. Camera and selection remain local.
See KNOWN_LIMITATIONS.md for the tested scope and runtime limitations.
