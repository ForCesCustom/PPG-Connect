using System;
using Steamworks;
using Steamworks.Data;
using UnityEngine;
using Color = UnityEngine.Color;

namespace PPGTogether.BepInEx
{
    // The menu uses one logical coordinate system; scaling and drag hit boxes
    // share the same transform, including on short or narrow displays.
    public sealed partial class PPGTogetherPlugin
    {
        private const float ConnectMenuWidth = 680f;
        private Vector2 connectContentScroll;
        private Vector2 connectStatusScroll;
        private string connectPreviousStatus;
        private bool connectPreviousSettings;
        private bool connectPreviousLobby;
        private SettingsPage connectPreviousPage;
        private static readonly Color ConnectInk = new Color(0.040f, 0.051f, 0.078f, 0.985f);
        private static readonly Color ConnectCard = new Color(0.073f, 0.091f, 0.133f, 1f);
        private static readonly Color ConnectRaised = new Color(0.112f, 0.140f, 0.191f, 1f);
        private static readonly Color ConnectAccent = new Color(0.27f, 0.91f, 0.86f, 1f);
        private static readonly Color ConnectViolet = new Color(0.34f, 0.29f, 0.56f, 1f);

        private float ConnectMenuHeight()
        {
            if (menuSettingsVisible) return 648f;
            return lobby.HasValue ? 630f : 584f;
        }

        private float ConnectMenuScale()
        {
            return Mathf.Min(1f, Mathf.Min(Mathf.Max(1f, Screen.width - 24f) / ConnectMenuWidth,
                Mathf.Max(1f, Screen.height - 24f) / ConnectMenuHeight()));
        }

        private void DrawConnectMenu()
        {
            if (ui == null) ui = new RoundedUiTheme();
            float height = ConnectMenuHeight();
            float scale = ConnectMenuScale();
            ClampMenuPosition(ConnectMenuWidth * scale, height * scale);
            HandleConnectMenuDrag(scale);
            if (connectPreviousSettings != menuSettingsVisible || connectPreviousLobby != lobby.HasValue || connectPreviousPage != settingsPage)
            {
                connectContentScroll = Vector2.zero;
                connectPreviousSettings = menuSettingsVisible;
                connectPreviousLobby = lobby.HasValue;
                connectPreviousPage = settingsPage;
            }
            Color previousColor = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(menuPosition.x, menuPosition.y + (1f - menuReveal) * 12f, 0f), Quaternion.identity, Vector3.one * scale);
            GUI.color = new Color(1f, 1f, 1f, menuReveal);
            ui.Panel(new Rect(3f, 6f, ConnectMenuWidth, height), new Color(0f, 0f, 0f, 0.28f));
            ui.Panel(new Rect(0f, 0f, ConnectMenuWidth, height), ConnectInk);
            ui.Card(new Rect(1f, 1f, ConnectMenuWidth - 2f, 84f), ConnectCard);
            ConnectHeader();

            Rect viewport = new Rect(24f, 102f, 632f, height - 238f);
            float hostSettingsHeight = !lobby.HasValue || IsHost ? 704f : (remoteHostSettings.Received ? 550f : 222f);
            float contentHeight = menuSettingsVisible ? (settingsPage == SettingsPage.Host ? hostSettingsHeight : 364f)
                : (!lobby.HasValue ? 346f : ConnectLobbyContentHeight());
            connectContentScroll = GUI.BeginScrollView(viewport, connectContentScroll, new Rect(0f, 0f, 610f, Mathf.Max(viewport.height, contentHeight)), false, false);
            if (menuSettingsVisible) ConnectSettings();
            else if (!lobby.HasValue) ConnectSetup();
            else ConnectLobby();
            GUI.EndScrollView();
            ConnectStatus(new Rect(24f, height - 120f, 632f, 72f));
            GUI.Label(new Rect(26f, height - 37f, 430f, 20f), "F8  Close   ·   F10  Diagnostics   ·   Drag header to move", ui.Small);
            GUI.Label(new Rect(493f, height - 37f, 160f, 20f), "v" + PluginVersion + "  ·  PPG " + LocalGameVersion, ui.Small);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private void HandleConnectMenuDrag(float scale)
        {
            Event input = Event.current;
            if (input == null) return;
            if (!menuVisible)
            {
                if (menuDragging) { menuDragging = false; SaveMenuPosition(); }
                return;
            }
            if (menuReveal < 0.92f) return;
            Rect header = new Rect(menuPosition.x, menuPosition.y, 422f * scale, 83f * scale);
            if (input.type == EventType.MouseDown && input.button == 0 && header.Contains(input.mousePosition))
            {
                menuDragging = true;
                menuDragOffset = input.mousePosition - menuPosition;
                input.Use();
            }
            else if (menuDragging && input.type == EventType.MouseDrag && input.button == 0)
            {
                menuPosition = input.mousePosition - menuDragOffset;
                ClampMenuPosition(ConnectMenuWidth * scale, ConnectMenuHeight() * scale);
                input.Use();
            }
            else if (menuDragging && input.type == EventType.MouseUp && input.button == 0)
            {
                menuDragging = false;
                SaveMenuPosition();
                input.Use();
            }
        }

        private void ConnectHeader()
        {
            if (modIcon != null) GUI.DrawTexture(new Rect(24f, 18f, 47f, 47f), modIcon, ScaleMode.ScaleToFit, true);
            else ui.Card(new Rect(24f, 18f, 47f, 47f), ConnectViolet);
            GUI.Label(new Rect(84f, 14f, 220f, 33f), "CONNECT", ui.Heading);
            GUI.Label(new Rect(86f, 48f, 275f, 17f), "PEOPLE PLAYGROUND  /  MULTIPLAYER", ui.Eyebrow);
            ui.CursorDot(new Rect(404f, 29f, 8f, 8f), SteamReady() ? ConnectAccent : new Color(1f, 0.63f, 0.35f));
            GUI.Label(new Rect(420f, 22f, 142f, 21f), SteamReady() ? Truncate(SafeName(SteamClient.Name), 19) : "Steam offline", ui.Label);
            GUI.Label(new Rect(420f, 43f, 142f, 17f), lobby.HasValue ? (IsHost ? "SESSION HOST" : "PLAYER") : "READY TO CONNECT", ui.Small);
            if (ConnectButton("cn-settings", new Rect(563f, 24f, 60f, 32f), menuSettingsVisible ? "Back" : "Settings", false)) menuSettingsVisible = !menuSettingsVisible;
            if (ConnectButton("cn-close", new Rect(629f, 24f, 29f, 32f), "×", false)) menuVisible = false;
        }

        private bool ConnectButton(string id, Rect rect, string label, bool primary)
        {
            return DrawButton(id, rect, label, primary ? ConnectAccent : ConnectRaised,
                primary ? new Color(0.56f, 1f, 0.94f, 1f) : new Color(0.20f, 0.25f, 0.34f, 1f), primary ? ui.ButtonText : ui.Center);
        }

        private void ConnectSetup()
        {
            GUI.Label(new Rect(0f, 0f, 610f, 32f), "Your next shared sandbox.", ui.Heading);
            GUI.Label(new Rect(1f, 39f, 610f, 36f), "Create a Steam lobby, invite your friends and load a map together.", ui.MutedBody);
            ui.Card(new Rect(0f, 84f, 610f, 190f), ConnectCard);
            GUI.Label(new Rect(18f, 96f, 280f, 22f), "Who can join?", ui.Label);
            GUI.Label(new Rect(18f, 121f, 568f, 20f), "Choose how your lobby is discovered.", ui.MutedBody);
            ConnectPrivacy(new Rect(18f, 151f, 184f, 38f), LobbyPrivacy.Private, "Private");
            ConnectPrivacy(new Rect(212f, 151f, 184f, 38f), LobbyPrivacy.FriendsOnly, "Friends only");
            ConnectPrivacy(new Rect(406f, 151f, 184f, 38f), LobbyPrivacy.Public, "Public");
            GUI.Label(new Rect(18f, 208f, 330f, 23f), "Player limit", ui.Label);
            GUI.Label(new Rect(18f, 233f, 330f, 20f), "Including you  ·  2–8 players", ui.MutedBody);
            int changed = ConnectStepper("cn-capacity", new Rect(424f, 220f, 168f, 34f), maxPlayers, 2, 8, "");
            if (changed != maxPlayers) { maxPlayers = changed; hostDefaultMaxPlayersSetting.Value = changed; SaveSettings(); }
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && SteamReady();
            if (ConnectButton("cn-create", new Rect(0f, 288f, 610f, 46f), "Create Steam lobby   +", true)) CreateLobbyAsync();
            GUI.enabled = previousEnabled;
        }

        private void ConnectPrivacy(Rect rect, LobbyPrivacy value, string label)
        {
            bool selected = privacy == value;
            if (DrawButton("cn-privacy-" + value, rect, selected ? label + "  ·" : label,
                selected ? ConnectViolet : ConnectRaised, new Color(0.43f, 0.37f, 0.65f, 1f), ui.Center))
            {
                privacy = value;
                hostDefaultPrivacySetting.Value = value.ToString();
                SaveSettings();
            }
        }

        private float ConnectLobbyContentHeight()
        {
            if (!lobby.HasValue) return 0f;
            int rows = (lobby.Value.MemberCount + 1) / 2;
            return 169f + rows * 75f + (IsHost && sessionActive ? 111f : 0f) + (IsHost && !sessionActive ? 60f : 0f) + 46f;
        }

        private void ConnectLobby()
        {
            Lobby current = lobby.Value;
            GUI.Label(new Rect(0f, 0f, 350f, 32f), sessionActive ? "Together in the sandbox." : "Your lobby is ready.", ui.Heading);
            string state = sessionActive ? "PLAYING" : (hostStartAwaitingMap || ClientMapTransitionInProgress ? "SYNCING MAP" : "IN LOBBY");
            ui.Pill(new Rect(461f, 3f, 149f, 29f), sessionActive ? new Color(0.10f, 0.30f, 0.26f, 1f) : ConnectViolet);
            GUI.Label(new Rect(461f, 3f, 149f, 29f), state, ui.Center);
            ConnectMetric(new Rect(0f, 46f, 196f, 72f), "PLAYERS", current.MemberCount + " / " + current.MaxMembers);
            ConnectMetric(new Rect(207f, 46f, 196f, 72f), "TRACKED OBJECTS", registry.Count.ToString());
            ConnectMetric(new Rect(414f, 46f, 196f, 72f), "CONNECTION", "Steam Relay");
            GUI.Label(new Rect(0f, 134f, 240f, 20f), "IN YOUR LOBBY", ui.Eyebrow);
            if (ConnectButton("cn-invite", new Rect(434f, 128f, 176f, 31f), "Invite friends   +", false)) InviteFriends();
            float y = 169f;
            int index = 0;
            foreach (Friend member in current.Members)
            {
                ConnectMember(current, member, new Rect((index % 2) * 311f, y + (index / 2) * 75f, 299f, 64f));
                index++;
            }
            y += ((index + 1) / 2) * 75f;
            if (IsHost && !sessionActive)
            {
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && !hostStartAwaitingMap;
                if (ConnectButton("cn-start", new Rect(0f, y, 610f, 46f), hostStartAwaitingMap ? "Loading shared map…" : "Start session & sync map", true)) StartSession();
                GUI.enabled = previousEnabled;
                y += 60f;
            }
            if (IsHost && sessionActive) { ConnectBots(y); y += 111f; }
            if (DrawButton("cn-leave", new Rect(0f, y, 610f, 36f), "Leave lobby", new Color(0.21f, 0.105f, 0.15f, 1f), new Color(0.37f, 0.15f, 0.21f, 1f), ui.Center)) LeaveLobby();
        }

        private void ConnectMetric(Rect rect, string label, string value)
        {
            ui.Card(rect, ConnectCard);
            GUI.Label(new Rect(rect.x + 15f, rect.y + 9f, rect.width - 30f, 18f), label, ui.Eyebrow);
            GUI.Label(new Rect(rect.x + 15f, rect.y + 30f, rect.width - 30f, 30f), value, label == "CONNECTION" ? ui.Label : ui.Stat);
        }

        private void ConnectMember(Lobby current, Friend member, Rect rect)
        {
            ulong id = (ulong)member.Id;
            bool host = member.Id == current.Owner.Id;
            Peer peer = null;
            if (IsHost && !host) peers.TryGetValue(id, out peer);
            avatars.Request(id);
            Texture2D avatar = avatars.Get(id);
            ui.Card(rect, ConnectCard);
            ui.Pill(new Rect(rect.x + 11f, rect.y + 12f, 40f, 40f), host ? ConnectViolet : ConnectRaised);
            if (avatar != null) GUI.DrawTexture(new Rect(rect.x + 13f, rect.y + 14f, 36f, 36f), avatar, ScaleMode.ScaleToFit, true);
            else GUI.Label(new Rect(rect.x + 13f, rect.y + 14f, 36f, 36f), host ? "H" : "P", ui.Center);
            GUI.Label(new Rect(rect.x + 62f, rect.y + 10f, 223f, 22f), Truncate(SafeName(member.Name), 27), ui.Label);
            string detail;
            if (host) detail = "HOST  ·  " + (sessionActive ? "SIMULATING WORLD" : "IN LOBBY");
            else if (IsHost) detail = PeerMapStatusLabel(peer == null ? PeerMapStatus.InLobby : peer.MapStatus);
            else if (member.Id == SteamClient.SteamId) detail = sessionActive ? "YOU  ·  PLAYING" : "YOU  ·  WAITING FOR HOST";
            else detail = "LOBBY MEMBER";
            GUI.Label(new Rect(rect.x + 62f, rect.y + 34f, 220f, 19f), detail, ui.Small);
        }

        private void ConnectBots(float y)
        {
            ui.Card(new Rect(0f, y, 610f, 96f), ConnectCard);
            GUI.Label(new Rect(17f, y + 12f, 270f, 23f), "Sandbox assistants", ui.Label);
            GUI.Label(new Rect(17f, y + 40f, 312f, 41f), "Host-controlled bots that spawn, move and clean up their own objects.", ui.MutedBody);
            int before = botCount;
            botCount = ConnectStepper("cn-bot-count", new Rect(358f, y + 14f, 145f, 31f), botCount, 1, MaximumBots, "");
            if (botCount != before && botsEnabled) { BuildBots(); BroadcastBotMode(true, botCount); }
            if (ConnectButton("cn-bots", new Rect(512f, y + 14f, 81f, 31f), botsEnabled ? "On" : "Off", botsEnabled)) SetBotMode(!botsEnabled);
            GUI.Label(new Rect(359f, y + 58f, 235f, 20f), botsEnabled ? "Assistants are active" : "Enable when you need a hand", ui.Small);
        }

        private void ConnectStatus(Rect rect)
        {
            string message = status ?? string.Empty;
            if (!string.Equals(connectPreviousStatus, message, StringComparison.Ordinal))
            {
                connectPreviousStatus = message;
                connectStatusScroll = Vector2.zero;
            }
            bool warning = message.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0;
            ui.Card(rect, warning ? new Color(0.23f, 0.145f, 0.13f, 1f) : new Color(0.075f, 0.135f, 0.16f, 1f));
            ui.CursorDot(new Rect(rect.x + 14f, rect.y + 16f, 8f, 8f), warning ? new Color(1f, 0.65f, 0.38f, 1f) : ConnectAccent);
            GUI.Label(new Rect(rect.x + 30f, rect.y + 8f, 420f, 19f), warning ? "CONNECTION NOTICE" : "SESSION ACTIVITY", ui.Eyebrow);
            Rect view = new Rect(rect.x + 30f, rect.y + 30f, rect.width - 43f, 34f);
            float textHeight = ui.Body.CalcHeight(new GUIContent(message), view.width - 20f);
            connectStatusScroll = GUI.BeginScrollView(view, connectStatusScroll, new Rect(0f, 0f, view.width - 20f, Mathf.Max(34f, textHeight)), false, false);
            GUI.Label(new Rect(0f, 0f, view.width - 20f, Mathf.Max(34f, textHeight)), message, ui.Body);
            GUI.EndScrollView();
        }

        private int ConnectStepper(string id, Rect rect, int value, int minimum, int maximum, string suffix)
        {
            if (ConnectButton(id + "-", new Rect(rect.x, rect.y, 31f, rect.height), "−", false)) value = Mathf.Max(minimum, value - 1);
            ui.Pill(new Rect(rect.x + 37f, rect.y, rect.width - 74f, rect.height), ConnectRaised);
            GUI.Label(new Rect(rect.x + 37f, rect.y, rect.width - 74f, rect.height), value + suffix, ui.Center);
            if (ConnectButton(id + "+", new Rect(rect.xMax - 31f, rect.y, 31f, rect.height), "+", false)) value = Mathf.Min(maximum, value + 1);
            return value;
        }

        private int ConnectSettingNumber(string id, float y, string label, int value, int minimum, int maximum, string suffix)
        {
            GUI.Label(new Rect(18f, y, 367f, 32f), label, ui.Label);
            return ConnectStepper(id, new Rect(420f, y, 172f, 30f), value, minimum, maximum, suffix);
        }

        private bool ConnectSettingToggle(string id, float y, string label, bool enabled)
        {
            GUI.Label(new Rect(18f, y, 472f, 30f), label, ui.Label);
            return ConnectButton(id, new Rect(515f, y, 77f, 29f), enabled ? "On" : "Off", enabled);
        }

        private void ConnectSettings()
        {
            GUI.Label(new Rect(0f, 0f, 350f, 32f), "Make it your sandbox.", ui.Heading);
            if (ConnectButton("cn-player-tab", new Rect(0f, 46f, 300f, 35f), "Your experience", settingsPage == SettingsPage.Player)) settingsPage = SettingsPage.Player;
            if (ConnectButton("cn-host-tab", new Rect(310f, 46f, 300f, 35f), "Host & permissions", settingsPage == SettingsPage.Host)) settingsPage = SettingsPage.Host;
            if (settingsPage == SettingsPage.Player) ConnectPlayerSettings(); else ConnectHostSettings();
        }

        private void ConnectPlayerSettings()
        {
            GUI.Label(new Rect(1f, 98f, 610f, 35f), "Saved on this PC. Adjust how other players appear in your game.", ui.MutedBody);
            ui.Card(new Rect(0f, 143f, 610f, 220f), ConnectCard);
            if (ConnectSettingToggle("cn-names", 159f, "Show player names", playerShowRemoteNamesSetting.Value)) { playerShowRemoteNamesSetting.Value = !playerShowRemoteNamesSetting.Value; SaveSettings(); }
            if (ConnectSettingToggle("cn-avatars", 199f, "Show Steam avatars", playerShowRemoteAvatarsSetting.Value)) { playerShowRemoteAvatarsSetting.Value = !playerShowRemoteAvatarsSetting.Value; SaveSettings(); }
            int scale = Mathf.RoundToInt(CursorScale() * 100f);
            int next = ConnectSettingNumber("cn-scale", 239f, "Cursor size", scale, 60, 180, "%");
            if (next != scale) { playerCursorScaleSetting.Value = next / 100f; SaveSettings(); }
            int smoothing = Mathf.RoundToInt(CursorSmoothing());
            next = ConnectSettingNumber("cn-smooth", 279f, "Cursor smoothing", smoothing, 8, 48, "");
            if (next != smoothing) { playerCursorSmoothingSetting.Value = next; SaveSettings(); }
            int rate = CursorSendRateHz();
            next = ConnectSettingNumber("cn-cursor-rate", 319f, "Cursor updates per second", rate, 60, 120, " Hz");
            if (next != rate) { playerCursorSendRateSetting.Value = next; SaveSettings(); }
        }

        private void ConnectHostSettings()
        {
            bool editable = !lobby.HasValue || IsHost;
            GUI.Label(new Rect(1f, 98f, 610f, 36f), editable ? "Your host profile. Changes apply to the shared session and are sent to players." : "The host controls these settings. Received values are shown below.", ui.MutedBody);
            if (!editable) { ConnectRemoteSettings(); return; }
            ui.Card(new Rect(0f, 143f, 610f, 298f), ConnectCard);
            int value = PhysicsVelocityIterations();
            int next = ConnectSettingNumber("cn-velocity", 158f, "Physics velocity iterations", value, 1, 16, "");
            if (next != value) { hostVelocityIterationsSetting.Value = next; HostSettingsChanged(); }
            value = PhysicsPositionIterations(); next = ConnectSettingNumber("cn-position", 198f, "Physics position iterations", value, 1, 16, "");
            if (next != value) { hostPositionIterationsSetting.Value = next; HostSettingsChanged(); }
            value = SnapshotRateHz(); next = ConnectSettingNumber("cn-snapshots", 238f, "World updates per second", value, 10, 30, " Hz");
            if (next != value) { hostSnapshotRateSetting.Value = next; HostSettingsChanged(); }
            value = MaximumNetworkObjects(); next = ConnectSettingNumber("cn-objects", 278f, "Network object limit", value, 25, 1000, "");
            if (next != value) { hostMaxNetworkObjectsSetting.Value = next; HostSettingsChanged(); }
            value = GuestSpawnLimitPerMinute(); next = ConnectSettingNumber("cn-spawns", 318f, "Guest spawns per minute", value, 1, 60, "");
            if (next != value) { hostGuestSpawnLimitSetting.Value = next; HostSettingsChanged(); }
            value = GuestInteractionLimitPerMinute(); next = ConnectSettingNumber("cn-interactions", 358f, "Guest actions per minute", value, 5, 120, "");
            if (next != value) { hostGuestInteractionLimitSetting.Value = next; HostSettingsChanged(); }
            value = BotSpawnLimit(); next = ConnectSettingNumber("cn-bot-limit", 398f, "Bot spawns per session", value, 0, 100, "");
            if (next != value) { hostBotSpawnLimitSetting.Value = next; HostSettingsChanged(); }
            GUI.Label(new Rect(1f, 450f, 610f, 24f), "PLAYER PERMISSIONS", ui.Eyebrow);
            ui.Card(new Rect(0f, 481f, 610f, 216f), ConnectCard);
            if (ConnectSettingToggle("cn-allow-spawn", 494f, "Players can spawn", hostGuestsCanSpawnSetting.Value)) { hostGuestsCanSpawnSetting.Value = !hostGuestsCanSpawnSetting.Value; HostSettingsChanged(); }
            if (ConnectSettingToggle("cn-allow-grab", 534f, "Players can grab", hostGuestsCanGrabSetting.Value)) { hostGuestsCanGrabSetting.Value = !hostGuestsCanGrabSetting.Value; HostSettingsChanged(); }
            if (ConnectSettingToggle("cn-allow-use", 574f, "Players can activate objects", hostGuestsCanActivateSetting.Value)) { hostGuestsCanActivateSetting.Value = !hostGuestsCanActivateSetting.Value; HostSettingsChanged(); }
            if (ConnectSettingToggle("cn-allow-delete", 614f, "Players can delete objects", hostGuestsCanDeleteSetting.Value)) { hostGuestsCanDeleteSetting.Value = !hostGuestsCanDeleteSetting.Value; HostSettingsChanged(); }
            if (ConnectSettingToggle("cn-allow-bots", 654f, "Allow sandbox assistants", hostBotsAllowedSetting.Value)) { hostBotsAllowedSetting.Value = !hostBotsAllowedSetting.Value; HostSettingsChanged(); }
        }

        private void ConnectRemoteSettings()
        {
            if (!remoteHostSettings.Received)
            {
                GUI.Label(new Rect(18f, 157f, 574f, 52f), "Waiting for the host profile. It arrives after the Steam Relay handshake.", ui.Body);
                return;
            }
            ui.Card(new Rect(0f, 143f, 610f, 403f), ConnectCard);
            ConnectReadOnly(158f, "Physics velocity / position", remoteHostSettings.VelocityIterations + " / " + remoteHostSettings.PositionIterations);
            ConnectReadOnly(198f, "World updates per second", remoteHostSettings.SnapshotRate + " Hz");
            ConnectReadOnly(238f, "Network object limit", remoteHostSettings.MaximumObjects.ToString());
            ConnectReadOnly(278f, "Guest spawns per minute", remoteHostSettings.GuestSpawnsPerMinute.ToString());
            ConnectReadOnly(318f, "Players can spawn", remoteHostSettings.GuestsCanSpawn ? "Allowed" : "Off");
            ConnectReadOnly(358f, "Players can grab", remoteHostSettings.GuestsCanGrab ? "Allowed" : "Off");
            ConnectReadOnly(398f, "Players can activate objects", remoteHostSettings.GuestsCanActivate ? "Allowed" : "Off");
            ConnectReadOnly(438f, "Players can delete objects", remoteHostSettings.GuestsCanDelete ? "Allowed" : "Off");
            ConnectReadOnly(478f, "Bot spawns per session", remoteHostSettings.BotSpawnLimit.ToString());
        }

        private void ConnectReadOnly(float y, string label, string value)
        {
            GUI.Label(new Rect(18f, y, 407f, 31f), label, ui.Label);
            ui.Pill(new Rect(437f, y, 155f, 31f), ConnectRaised);
            GUI.Label(new Rect(437f, y, 155f, 31f), value, ui.Center);
        }
    }
}
