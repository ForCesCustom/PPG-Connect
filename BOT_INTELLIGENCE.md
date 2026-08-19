# Connect bot intelligence

Connect Bot Mode keeps its existing UI, count selector, cursor visuals, peer IDs
and host-only lifecycle. Version 0.1.40 replaces only the internal decisions of
those existing virtual cursors.

## Runtime path

`PPGTogetherPlugin.UpdateBots` calls `BotWorldKnowledge.Refresh` at most once
per 1.35 seconds. Each refresh examines at most 180 `PhysicalBehaviour` roots,
refreshes registered Connect roots immediately, classifies them from their
catalog/object names and rebuilds a compact spatial grid. The scan is never run
once per bot or once per rendered frame.

Each bot receives a `BotPerception` containing nearby entities, risk, debris,
living count and unexplored interest-map frontiers. `BotMind` combines:

- a distinct Builder/Mover/Cleaner personality profile;
- mood (curiosity, confidence, stress and satisfaction);
- session memory of outcomes, recently inspected objects, spawn cooldowns and
  repeated goals;
- utility-scored candidate goals with a small deterministic variation;
- a bounded multi-step action plan and an expiry/hysteresis rule;
- `BotCoordinationBoard` leases so two cursors do not choose the same target.

The selected intent is still executed by the existing host code. Spawn goes
through `SpawnAndReplicate`; grab uses `HostGrabController`; activation uses the
same host `Use` path as clients; cleanup only deletes an old item in that bot's
own creation record. No bot changes camera or OS mouse state.

## Catalog policy

`BotSpawnCatalog` reads already registered catalog collections when the local
game exposes them and supplements them with safe standard-key lookups. It never
adds a spawnable, downloads an asset or writes to the catalog. The fallback is
non-living content; `Human`/other living entries may be intentionally chosen
only as rare content when the local scene has no living subject, never as the
old default behaviour.

## Safety and cleanup

All object-changing bot actions require an existing Connect network identity.
Bots may observe non-networked map objects but do not grab, activate or delete
them. A target missing, a host lease denial, map transition, disabled Bot Mode,
session leave or timeout releases the claim and records an outcome. Map/session
cleanup clears bot memory, catalog cache, world knowledge and active bot leases.

## Diagnostics and checks

F10 adds knowledge and discovered-catalog counters to Connect diagnostics.
`[Connect][Bots]` debug entries record decisions, selected rationale and utility
only when a new plan is made; cursor and snapshot traffic remains unlogged.

`Dev/BotBrainSmokeTests.cs` is deterministic and covers safe non-Human fallback
selection, claims, failure recovery and danger priority without Unity/Steam.
The main project still compiles against the actual local People Playground
assemblies; runtime behaviour requiring a map is not claimed as a headless test.
