# Patches — Connect BepInEx edition v0.1.26

## ClientWorldInputPatch

- Target type: `ToolControllerBehaviour`
- Target method: `HandleTools`
- Game tested: People Playground `1.27.16`, Unity `2020.3.1f1`
- Patch type: Harmony prefix
- Reason: a connected non-host must not simultaneously apply vanilla local world
  interactions while it sends host-authoritative multiplayer drag requests.
- Behaviour: returns `false` only while the plugin has a live started client
  session. Before suppressing the unsafe local world action, it preserves the
  semantic Activate/Delete key actions for that client's local selection by
  sending bounded interaction requests to the host. Holding the configured
  Activate binding renews a bounded host-side continuous-use lease. Host/single-player
  behaviour is untouched. UI, camera navigation, pause, Escape and Steam
  Overlay are not hooked.
- Signature/version guard: Harmony locates the method by exact type and name at
  plugin startup. If it cannot patch, `patchApplied` remains false and the plugin
  does not claim client world-input suppression. It logs the failure.

No patches target Steam, Steam callbacks, physics simulation, mod-loader
security, the game loader, OS mouse input, or anti-cheat/security components.

## ClientCatalogSpawnPatch

- Target type: `CatalogBehaviour`
- Target method: `Spawn(SpawnableAsset, bool)`
- Game tested: People Playground `1.27.16`, Unity `2020.3.1f1`
- Patch type: Harmony prefix
- Reason: preserve every player's own normal Tab catalog while preventing a
  connected client from creating an unauthoritative local object.
- Behaviour: only in a live non-host Connect session, sends the selected stable
  catalog key, flip flag and world cursor position to the host and skips the
  local base-game spawn. Host and single-player catalog behavior is unchanged.
- Signature/version guard: exact overload signature. If patching fails,
  `patchApplied` remains false and no client input suppression is claimed.

## ClientContextMenuSelectionPatch

- Target type: `ToolControllerBehaviour`
- Target method: `HandleContextMenu`
- Game tested: People Playground `1.27.16`, Unity `2020.3.1f1`
- Patch type: Harmony prefix
- Reason: after the broad tool handler is gated for a client, right-click still
  needs a local selection for the normal context menu.
- Behaviour: on the context binding, selects only the physical object currently
  under the local cursor when it has a registered Connect identity. It never
  changes world physics or a remote player's selection.
- Signature/version guard: exact target name; a failure leaves vanilla behavior
  untouched because Connect will not declare its client world gate active.

## ClientContextActivatePatch / ClientContextDeletePatch

- Target type: `ContextMenuBehaviour`
- Target methods: `ActivateAction` and `DeleteAction`
- Game tested: People Playground `1.27.16`, Unity `2020.3.1f1`
- Patch type: Harmony prefixes
- Reason: route the two bounded vanilla actions through the host rather than
  mutating a non-host client's local simulation.
- Behaviour: in a live non-host session, transforms selected registered roots
  into reliable `InteractionRequest` messages, closes the local menu and skips
  local execution. The host validates identity, cursor range, permissions and
  rate before applying the equivalent vanilla action. All other context buttons
  remain unsupported rather than being guessed or remotely invoked.
- Signature/version guard: exact target method names; failure leaves the base
  game action local and Connect reports no safe client authority gate.

## ClientDirectActivationPatch

- Target type: `ToolControllerBehaviour`
- Target method: `HandleIndirectInteraction`
- Game tested: People Playground `1.27.16`, Unity `2020.3.1f1`
- Patch type: Harmony prefix
- Reason: route the standard direct `Use` binding through the host.
- Behaviour: only during a live non-host session, consumes the direct-use input
  and begins a renewable continuous-use lease for the registered object beneath
  the local cursor. No method name or component target crosses the network.
- Signature/version guard: exact target method name; if not applied, Connect
  keeps its safe fallback and does not pretend to synchronize this action.
