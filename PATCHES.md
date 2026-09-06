# Patches — Connect v0.1.46

Targets are based on People Playground 1.27.17, Unity 2020.3.1f1.
These are gameplay Harmony hooks, not changes to the game's executable or
managed assemblies. Read the startup log for patch failures after an update.

## Input and catalogue

- `ToolControllerBehaviour.HandleTools`: gates guest world tools while a
  session/map transition is active; preserves supported selected-object
  Activate/Delete intents. The Connect panel also captures world-tool input.
- `CatalogBehaviour.Spawn(SpawnableAsset, bool)`: guests request their own
  catalogue key/position/flip from the host. The host observes its catalogue
  boundary. Network instantiation scopes `CatalogBehaviour.SelectedItem`
  so another player's selected Tab item cannot replace the requested asset.
- `HandleContextMenu` preserves local selection of a registered root.
  `HandleIndirectInteraction` routes direct/continuous Use through host leases.

## Context and shared controls

- `ContextMenuBehaviour.ActivateAction/DeleteAction` route through host
  identity, range, rate and permission validation.
- Other zero-argument context `*Action` methods are locally enumerated:
  Freeze, NoCollide, Weightless and Ignite map to fixed network enums.
  Copy, Save and Follow remain local. Unsupported world mutations are blocked.
- `ContextMenuBehaviour.CreateDynamicButtons` is suppressed for active guests:
  arbitrary mod callbacks are not a network interface.
- `ClearButtonBehaviour.ClearEverything`, `ClearLivingBehaviour.Clear`,
  `ClearDebrisBehaviour.Clear` and `UndoControllerBehaviour.Undo` become guest
  requests against the shared host world/history.
- `Global.TogglePaused/ToggleSlowmotion` and
  `EnvironmentSettingsController.SetValue` route supported guest changes to
  the host. Applying a received host state uses a scoped recursion guard.

## Map lifecycle

- `MapLoaderBehaviour.Load` postfix observes actual load completion. Host loads
  advance the world epoch even if the map identity is unchanged.
- `MapViewBehaviour.Select` and `SceneSwitchBehaviour.Switch` block independent
  guest map changes while allowing Connect's scoped host-directed transition.
- Guests need an actual load callback and instantiated requested map root after
  a forced epoch reload; an old same-map scene is not sufficient evidence.
  Client status includes the epoch, which the host validates.

## Replica simulation and state

The replication module suppresses selected simulation callbacks only for
components indexed as Connect guest replicas, so those replicas display host
state instead of independently simulating the same actors. Consult
`ReplicatedObjectState.cs` for the concrete supported component/method list.
Host and ordinary local objects are outside that replica set.

World lifecycle also uses the game's spawn/remove events and a Connect identity
destruction callback. Periodic discovery uses local catalogue keys and excludes
map-loader fixtures. The protocol accepts typed data and fixed action enums;
it does not accept arbitrary component types, method names or serialized saves.

These hooks do not establish universal Workshop compatibility. See
KNOWN_LIMITATIONS.md and verify actual runtime logs and two-account behavior.
