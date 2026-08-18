# Local research — BepInEx edition

## Target installation

- Game: `C:\Program Files (x86)\Steam\steamapps\common\People Playground`
- Version: `1.27.16`; Steam build ID `24782494`
- Unity: `2020.3.1f1` Mono x64
- Managed Steam wrapper: `People Playground_Data\Managed\Facepunch.Steamworks.Win64.dll`
- Steamworks.NET was not selected or bundled.

## Confirmed Facepunch API surface

The game initializes Steam. The plugin only observes that existing context and
never calls `SteamClient.Init`, `SteamAPI.Init`, `SteamClient.Shutdown` or any
native Steam shutdown function.

- Identity/readiness: `SteamClient.IsValid`, `SteamClient.IsLoggedOn`,
  `SteamClient.SteamId`, `SteamClient.Name`
- Lobby: `SteamMatchmaking.CreateLobbyAsync`, `JoinLobbyAsync`, `Lobby.Members`,
  `Lobby.Owner`, `SetData`, `GetData`, visibility setters, `Leave`
- Invitations: `SteamFriends.OnGameLobbyJoinRequested`,
  `SteamFriends.OpenGameInviteOverlay`
- Avatars: `SteamFriends.GetMediumAvatarAsync`, `Image.Data`
- Launch parameter: `SteamApps.CommandLine`
- Relay: `SteamNetworkingSockets.CreateRelaySocket<T>`, `ConnectRelay<T>`,
  `SocketManager`, `ConnectionManager`, `Connection.SendMessage`

## Game integration points

- Plugin lifecycle: BepInEx `BaseUnityPlugin`.
- Narrow input target: `ToolControllerBehaviour.HandleTools`.
- Cursor: `Global.main.MousePosition`, with `Camera.main.ScreenToWorldPoint`
  as a fallback.
- Spawn events: `ModAPI.OnItemSpawned` / `OnItemRemoved`; validated catalog lookup
  through `ModAPI.FindSpawnable` and normal catalog spawning helpers.
- Simple object physics: `PhysicalBehaviour.rigidbody` / `Rigidbody2D`.

The stock source-mod compiler did not expose the Facepunch assembly as a source
mod reference. The user explicitly selected the BepInEx + Harmony alternative,
which is why this edition is a precompiled plugin rather than a `Mods` source mod.

## Local runtime evidence (2026-08-18)

- BepInEx 5.4.23.5 loaded the plugin successfully; the current player-facing
  name is `Connect`.
- Harmony reported `ToolControllerBehaviour.HandleTools patch applied`.
- The plugin requested Steam relay network access without any Steam re-init.
- A Friends Only lobby was created successfully and Steam displayed its invite
  chooser after pressing an empty `[ + ]` slot.

No second Steam account/device was available, so connection, remote handshake,
drag, spawn and snapshot tests remain unexecuted at runtime.
