# Connect relay protocol v4

The BepInEx plugin sends this binary protocol through the game-supplied
Facepunch `SteamNetworkingSockets` relay connection. It never serializes CLR
objects, method names, files, paths, assemblies or arbitrary GameObjects.

`BotMode` (`18`) is a reliable host-to-client control message containing an
enabled flag and a bounded bot count (0–3). Bot movement uses the existing
unreliable `Cursor` message with a reserved bot flag. Bot decisions run only
inside the host process; bots never submit remote network actions or impersonate
a Steam identity.

`SpawnRequest` is a client-to-host reliable World message. It contains only a
bounded spawnable key, flip flag and finite world position. The selected key
comes from each player's own normal Tab catalog. The host resolves the key in
its catalog and creates the item before broadcasting the authoritative `Spawn`
event.

`HostSettings` (`19`) is a reliable host-to-client Control message. It contains
only bounded numeric server settings (Physics2D velocity/position iterations,
snapshot rate, Connect object cap, guest spawn cap, permissions and bot cap).
It is informational to clients: only the lobby owner applies or changes host
settings. `ActionDenied` (`20`) returns a bounded reason for a denied gameplay
request and never closes a valid relay connection.

`InteractionRequest` (`21`) is a reliable client-to-host World message with an
interaction enum and one registered root NetId. `Activate` and `Delete` are
one-shot actions. `ActivateBegin`, `ActivateKeepAlive` and `ActivateEnd` form a
short renewable host-side continuous-use lease for automatic weapons and other
components that use vanilla `IsBeingUsedContinuously()`. The host validates
lobby identity, permissions, rate limit, object existence and cursor proximity
before calling the equivalent vanilla action. A client never supplies a method
name or executes its own authoritative physics action.

`MapLoad` (`22`) is a reliable host-to-client Control message containing one
bounded People Playground `Map.UniqueIdentity`. The host obtains it from the
game's loaded `MapLoaderBehaviour`; the client resolves it only against maps
already installed locally and invokes the same `MapLoaderBehaviour.Load()`
path. No map files, Workshop assets or paths cross the network.

## Envelope

All fields are little-endian. The fixed header is 30 bytes.

| Offset | Bytes | Field |
|---:|---:|---|
| 0 | 4 | Magic `0x54475050` (`PPGT`, retained for wire compatibility) |
| 4 | 2 | Protocol version (`4`) |
| 6 | 1 | Message type |
| 7 | 1 | Logical channel |
| 8 | 8 | Session nonce |
| 16 | 2 | Source peer ID |
| 18 | 4 | Sequence |
| 22 | 4 | Host tick |
| 26 | 4 | Payload length |
| 30 | N | Payload |

The hard packet limit is 49,152 bytes. The reader validates the fixed header,
magic, protocol, known type/channel, exact declared length, bounded strings and
finite floats before a handler can apply the message. A stale nonce is dropped.

## Channels

- `Control` (reliable): Hello, Welcome, Reject, `MapLoad`, session start/end,
  BotMode and HostSettings.
- `World` (reliable unless an update): grab lease, spawn/despawn and bounded
  interaction requests.
- `Snapshot` (unreliable): root Rigidbody2D state.
- `Cursor` (unreliable): world-space cursor state.

## Implemented messages

- `Hello`: protocol/mod/game version, claimed Steam ID, lobby ID and nonce. The
  host compares the claim with the Steam relay connection identity and current
  lobby membership.
- `Welcome` / `Reject`: assigned peer ID or bounded readable rejection reason.
- `Cursor`: `Vector2` world position, button mask and UI-busy flag; no screen
  pixels or camera transform is sent.
- `GrabBegin`, `GrabGranted`, `GrabDenied`, `GrabUpdate`, `GrabEnd`: a host-side
  overlap test chooses the actual body and emits an expiring lease token.
- `SpawnRequest`, `Spawn`, `Despawn`: catalog key plus bounded pose, with actual
  object creation performed by the host only.
- `InteractionRequest`: one-byte action plus an eight-byte root NetId. The host
  applies only registered vanilla Activate/Delete operations after validation.
  Continuous Activate uses begin/keep-alive/end rather than a per-frame packet;
  a lease expires if the client disconnects or stops renewing it. It never
  invokes a method name supplied by a client.
- `Snapshot`: registered root network ID plus root Rigidbody2D pose/velocity.
- `MapLoad`: host-selected installed-map identity. The client displays a clear
  local-map/timeout status if that identity cannot be resolved; it never enters
  the active gameplay state while still at the title screen.
- `HostSettings`: host-only, fixed 12-byte payload: velocity iterations (1–16),
  position iterations (1–16), snapshot rate (10–30 Hz), object cap (25–1000),
  guest spawn cap (1–60/min), spawn/grab/activate/delete/bot permission flags
  and bot spawn cap (0–100).
- `ActionDenied`: bounded status text for a rejected spawn or other gameplay
  action; unlike `Reject`, it never terminates the session connection.

## Explicitly absent

There is no type deserialization, client `SetObjectPosition`, file transfer,
asset-bundle transfer, remote code execution, arbitrary activation RPC, or
initial-world object graph transfer in v0.1.0.
