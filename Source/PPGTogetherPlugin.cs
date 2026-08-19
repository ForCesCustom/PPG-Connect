using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using Color = UnityEngine.Color;
using UColor = UnityEngine.Color;

namespace PPGTogether.BepInEx
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class PPGTogetherPlugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "local.ppgtogether.steam";
        // Keep the GUID stable so this is a seamless update for existing users.
        internal const string PluginName = "Connect";
        internal const string PluginVersion = "0.1.40";
        internal const string ExpectedGameVersion = "1.27.16";
        internal const string RuntimeMarkerName = "Connect.RuntimeMarker";
        internal const string RuntimeVersionMarkerName = "Connect.RuntimeVersion." + PluginVersion;

        private const ushort BotPeerBase = 60000;
        private const int MaximumBots = 3;
        private const int DefaultMaximumBotSpawnsPerSession = 36;
        private const float BotMinimumCleanupAge = 18f;
        private const float BotActionTimeout = 6f;
        private const float BotReachDistance = 0.42f;
        private const byte BotCursorFlag = 0x80;
        // Confirmed in the local People Playground build settings: Menu,
        // Main and Map Editor are the only bundled scenes. "Main" is the
        // sandbox scene selected by the normal map tile, so it is safe to use
        // as the deterministic fallback when a guest is still at the title
        // menu and therefore has no MapViewBehaviour to click.
        private const string SandboxSceneName = "Main";
        // The shipped rounded Connect icon is a local PNG and currently a
        // little over 2 MiB. It is loaded once on the Unity main thread, so a
        // 4 MiB cap remains bounded while avoiding a false "invalid size"
        // warning and a missing panel icon.
        private const int MaximumConnectIconBytes = 4 * 1024 * 1024;

        internal static PPGTogetherPlugin Instance;

        private readonly WorldRegistry registry = new WorldRegistry();
        private readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();
        private readonly Dictionary<ushort, RemoteCursor> cursors = new Dictionary<ushort, RemoteCursor>();
        private readonly List<BotAgent> bots = new List<BotAgent>();
        private readonly List<BotSpawnRecord> botSpawnedItems = new List<BotSpawnRecord>();
        private readonly BotWorldKnowledge botWorld = new BotWorldKnowledge();
        private readonly BotSpawnCatalog botCatalog = new BotSpawnCatalog();
        private readonly BotCoordinationBoard botCoordination = new BotCoordinationBoard();
        private readonly HostGrabController grabs;
        private readonly HostActivationController continuousActivations = new HostActivationController();
        private readonly SteamAvatarCache avatars = new SteamAvatarCache();
        private readonly Dictionary<string, float> uiHover = new Dictionary<string, float>();
        private SteamRelayTransport transport;
        private RoundedUiTheme ui;
        private Texture2D modIcon;
        private GameObject runtimeMarker;
        private GameObject runtimeVersionMarker;
        private ConfigEntry<float> menuXSetting;
        private ConfigEntry<float> menuYSetting;
        private ConfigEntry<int> hostDefaultMaxPlayersSetting;
        private ConfigEntry<string> hostDefaultPrivacySetting;
        private ConfigEntry<int> hostVelocityIterationsSetting;
        private ConfigEntry<int> hostPositionIterationsSetting;
        private ConfigEntry<int> hostSnapshotRateSetting;
        private ConfigEntry<int> hostMaxNetworkObjectsSetting;
        private ConfigEntry<int> hostGuestSpawnLimitSetting;
        private ConfigEntry<bool> hostGuestsCanSpawnSetting;
        private ConfigEntry<bool> hostGuestsCanGrabSetting;
        private ConfigEntry<bool> hostGuestsCanActivateSetting;
        private ConfigEntry<bool> hostGuestsCanDeleteSetting;
        private ConfigEntry<int> hostGuestInteractionLimitSetting;
        private ConfigEntry<bool> hostBotsAllowedSetting;
        private ConfigEntry<int> hostBotSpawnLimitSetting;
        private ConfigEntry<bool> hostBotInteractionsSetting;
        private ConfigEntry<bool> hostBotCleanupSetting;
        private ConfigEntry<bool> playerShowRemoteNamesSetting;
        private ConfigEntry<bool> playerShowRemoteAvatarsSetting;
        private ConfigEntry<float> playerCursorScaleSetting;
        private ConfigEntry<float> playerCursorSmoothingSetting;
        private ConfigEntry<int> playerCursorSendRateSetting;
        private ConfigEntry<string> supportGithubReleaseUrlSetting;
        private Lobby? lobby;
        private ulong nonce;
        private ushort nextPeerId = 1;
        private ushort clientPeerId;
        private bool sessionActive;
        private bool hostStartAwaitingMap;
        private bool clientSessionStartReceived;
        private bool clientMapLoadPending;
        private bool clientMapLoadIssued;
        private bool clientMapSceneTransitionPending;
        private bool clientConnectSceneSwitchCall;
        // MapLoaderBehaviour.CurrentMap records a selected map even on the
        // title screen.  Keep separate evidence that a map prefab was really
        // instantiated before telling a client that it is playing.
        private bool clientMapInstanceLoaded;
        private bool menuVisible;
        private bool menuDragging;
        private bool menuSettingsVisible;
        private bool debugVisible;
        private bool patchApplied;
        private bool relayAccessInitialised;
        private bool launchArgumentChecked;
        private bool installNoticeDismissed;
        private float menuReveal;
        private float nextInstallHealthAt;
        private Vector2 menuPosition = new Vector2(20f, 34f);
        private Vector2 menuDragOffset;
        private float nextCursorAt;
        private float nextSnapshotAt;
        private float nextGrabAt;
        private float nextClientActivationHeartbeatAt;
        private float clientMapLoadDeadline;
        private float clientMapReadyAt;
        private float nextClientMapProbeAt;
        private float nextClientMapLoadAttemptAt;
        private Vector2 previousLocalCursor;
        private float previousLocalCursorAt;
        private bool hasPreviousLocalCursor;
        private bool botsEnabled;
        private int botCount = 2;
        private int botSpawnCount;
        private int originalVelocityIterations;
        private int originalPositionIterations;
        private bool hostPhysicsApplied;
        private uint sequence;
        private uint hostTick;
        private string status = "Waiting for Steam context supplied by People Playground.";
        private string activeMapIdentity = string.Empty;
        private string clientRequestedMapIdentity = string.Empty;
        private string clientRequestedSceneName = string.Empty;
        private int maxPlayers = 4;
        private LobbyPrivacy privacy = LobbyPrivacy.FriendsOnly;
        private ulong clientGrabId;
        private uint clientGrabToken;
        private SettingsPage settingsPage;
        private HostSettingsView remoteHostSettings;
        private InstallationHealth installationHealth;
        private readonly Dictionary<ulong, SpawnRateWindow> guestSpawnWindows = new Dictionary<ulong, SpawnRateWindow>();
        private readonly Dictionary<ulong, SpawnRateWindow> guestInteractionWindows = new Dictionary<ulong, SpawnRateWindow>();
        private readonly HashSet<ulong> clientHeldActivationRoots = new HashSet<ulong>();

        private enum LobbyPrivacy
        {
            Private,
            FriendsOnly,
            Public
        }

        private enum SettingsPage
        {
            Player,
            Host
        }

        private enum NetworkInteraction : byte
        {
            Activate = 1,
            Delete = 2,
            ActivateBegin = 3,
            ActivateKeepAlive = 4,
            ActivateEnd = 5
        }

        // Reported by each guest over the existing authenticated relay. This
        // lets the host see actual map-load progress instead of treating every
        // lobby member as PLAYING when only the host has entered the map.
        private enum PeerMapStatus : byte
        {
            InLobby = 0,
            LoadingMap = 1,
            Synchronising = 2,
            Playing = 3,
            Failed = 4
        }

        internal PPGTogetherPlugin()
        {
            grabs = new HostGrabController(registry);
        }

        private void Awake()
        {
            Instance = this;
            EnsureRuntimeMarker();
            menuXSetting = Config.Bind("Interface", "Menu X", 20f, "Saved horizontal position of the Connect panel.");
            menuYSetting = Config.Bind("Interface", "Menu Y", 34f, "Saved vertical position of the Connect panel.");
            hostDefaultMaxPlayersSetting = Config.Bind("Host", "Default Lobby Capacity", 4, "Default Steam lobby capacity. Valid range: 2 to 8.");
            hostDefaultPrivacySetting = Config.Bind("Host", "Default Lobby Privacy", "FriendsOnly", "Private, FriendsOnly or Public.");
            hostVelocityIterationsSetting = Config.Bind("Host", "Physics Velocity Iterations", 8, "Host Physics2D velocity solver iterations. Valid range: 1 to 16. Applied only while hosting Connect.");
            hostPositionIterationsSetting = Config.Bind("Host", "Physics Position Iterations", 3, "Host Physics2D position solver iterations. Valid range: 1 to 16. Applied only while hosting Connect.");
            hostSnapshotRateSetting = Config.Bind("Host", "Snapshot Rate Hz", 20, "Authoritative Rigidbody snapshot rate. Valid range: 10 to 30 Hz.");
            hostMaxNetworkObjectsSetting = Config.Bind("Host", "Maximum Connect Objects", 500, "Maximum objects created through Connect spawn requests and bots. Valid range: 25 to 1000.");
            hostGuestSpawnLimitSetting = Config.Bind("Host", "Guest Spawns Per Minute", 20, "Per-player limit for host-validated Connect spawn requests. Valid range: 1 to 60.");
            hostGuestsCanSpawnSetting = Config.Bind("Host", "Guests Can Spawn", true, "Allow connected players to request vanilla item spawns.");
            hostGuestsCanGrabSetting = Config.Bind("Host", "Guests Can Grab", true, "Allow connected players to request host-authoritative object grabs.");
            hostGuestsCanActivateSetting = Config.Bind("Host", "Guests Can Activate", true, "Allow connected players to request host-authoritative vanilla Use actions from direct interaction and the context menu.");
            hostGuestsCanDeleteSetting = Config.Bind("Host", "Guests Can Delete", true, "Allow connected players to request host-authoritative deletion of a registered Connect object through the context menu.");
            hostGuestInteractionLimitSetting = Config.Bind("Host", "Guest Interactions Per Minute", 45, "Per-player host-validated activation/delete request limit. Valid range: 5 to 120.");
            hostBotsAllowedSetting = Config.Bind("Host", "Bots Allowed", true, "Allow the session host to enable Connect Bot Mode.");
            hostBotSpawnLimitSetting = Config.Bind("Host", "Bot Spawns Per Session", DefaultMaximumBotSpawnsPerSession, "Maximum vanilla items all bots may create in one session. Valid range: 0 to 100.");
            hostBotInteractionsSetting = Config.Bind("Host", "Bots Can Grab And Place", true, "Allow bots to use the host-authoritative physical grab controller on their own spawned items.");
            hostBotCleanupSetting = Config.Bind("Host", "Bots Can Clean Up", true, "Allow bots to delete only their own old, unleased spawned items.");
            playerShowRemoteNamesSetting = Config.Bind("Player", "Show Remote Names", true, "Show names beside remote player and bot cursors.");
            playerShowRemoteAvatarsSetting = Config.Bind("Player", "Show Remote Avatars", true, "Show cached Steam avatars beside remote player cursors.");
            playerCursorScaleSetting = Config.Bind("Player", "Remote Cursor Scale", 1f, "Local visual scale for remote cursors. Valid range: 0.60 to 1.80.");
            playerCursorSmoothingSetting = Config.Bind("Player", "Remote Cursor Smoothing", 22f, "Local remote cursor smoothing. Valid range: 8 to 48.");
            playerCursorSendRateSetting = Config.Bind("Player", "Cursor Send Rate Hz", 60, "Local cursor update rate. Valid range: 60 to 120 Hz.");
            supportGithubReleaseUrlSetting = Config.Bind("Support", "GitHub Download URL", ConnectSupportLinks.PublishedReleaseUrl, "Official HTTPS direct download used by the missing-files recovery notice. An empty or invalid saved value falls back to the published Connect package.");
            // Migrate older releases, which deliberately limited cursor
            // traffic to 45 Hz.  Cursor positions are compact unreliable
            // packets, so 60 Hz is still a small bandwidth cost but removes
            // visible stepping on high-refresh displays.
            if (playerCursorSendRateSetting.Value < 60) playerCursorSendRateSetting.Value = 60;
            if (playerCursorSmoothingSetting.Value < 8f) playerCursorSmoothingSetting.Value = 8f;
            menuPosition = new Vector2(menuXSetting.Value, menuYSetting.Value);
            maxPlayers = ClampHostCapacity(hostDefaultMaxPlayersSetting.Value);
            privacy = ReadPrivacy(hostDefaultPrivacySetting.Value);
            transport = new SteamRelayTransport(this);
            SubscribeSteam();
            TryInstallPatch();
            ModAPI.OnItemSpawned += OnItemSpawned;
            ModAPI.OnItemRemoved += OnItemRemoved;
            LoadModIcon();
            RefreshInstallationHealth();
            Logger.LogInfo("[Connect][Core] BepInEx plugin loaded. Steam is never initialised or shut down by this plugin.");
        }

        private void Start()
        {
            Invoke("ProcessStartupLobbyArgument", 2f);
        }

        private void Update()
        {
            hostTick++;
            if (Input.GetKeyDown(KeyCode.F8)) menuVisible = !menuVisible;
            if (Input.GetKeyDown(KeyCode.F10)) debugVisible = !debugVisible;
            menuReveal = Mathf.MoveTowards(menuReveal, menuVisible ? 1f : 0f, Time.unscaledDeltaTime * 7f);
            if (Time.unscaledTime >= nextInstallHealthAt) RefreshInstallationHealth();
            if (!SteamReady()) return;
            if (!relayAccessInitialised)
            {
                SteamNetworkingUtils.InitRelayNetworkAccess();
                relayAccessInitialised = true;
                Logger.LogInfo("[Connect][Transport] Requested Steam relay network access.");
            }
            transport.Pump();
            avatars.Pump();
            ProcessReceivedPackets();
            UpdateMapSynchronisation();
            if (!launchArgumentChecked && Time.unscaledTime > 2f) ProcessStartupLobbyArgument();
            if (HasCursorRelay()) UpdateCursorNetwork();
            if (sessionActive)
            {
                UpdateBots();
                UpdateInteractionInput();
                if (IsHost && Time.unscaledTime >= nextSnapshotAt)
                {
                    nextSnapshotAt = Time.unscaledTime + (1f / SnapshotRateHz());
                    BroadcastSnapshots();
                }
            }
            UpdateCursorInterpolation();
        }

        private void FixedUpdate()
        {
            if (IsHost && sessionActive)
            {
                grabs.FixedUpdate(hostTick);
                continuousActivations.FixedUpdate(hostTick, HostContinuousActivate);
            }
        }

        private void OnGUI()
        {
            HandleMenuDrag();
            if (menuReveal > 0.001f) DrawMenu();
            DrawInstallRecoveryNotice();
            DrawRemoteCursors();
            if (debugVisible) DrawDebug();
        }

        private void OnDestroy()
        {
            Cleanup(false);
            if (ui != null) ui.Dispose();
            if (modIcon != null) Destroy(modIcon);
            if (runtimeMarker != null) Destroy(runtimeMarker);
            if (runtimeVersionMarker != null) Destroy(runtimeVersionMarker);
        }

        private void EnsureRuntimeMarker()
        {
            runtimeMarker = GameObject.Find(RuntimeMarkerName);
            if (runtimeMarker == null)
            {
                runtimeMarker = new GameObject(RuntimeMarkerName);
                DontDestroyOnLoad(runtimeMarker);
            }
            runtimeVersionMarker = GameObject.Find(RuntimeVersionMarkerName);
            if (runtimeVersionMarker == null)
            {
                runtimeVersionMarker = new GameObject(RuntimeVersionMarkerName);
                DontDestroyOnLoad(runtimeVersionMarker);
            }
        }

        private void LoadModIcon()
        {
            try
            {
                string pluginDirectory = Path.GetDirectoryName(Info.Location);
                string iconPath = Path.Combine(pluginDirectory ?? string.Empty, "connect-icon.png");
                if (!File.Exists(iconPath)) return;
                byte[] data = File.ReadAllBytes(iconPath);
                if (data.Length == 0 || data.Length > MaximumConnectIconBytes)
                {
                    Logger.LogWarning("[Connect][UI] Ignoring invalid Connect icon size.");
                    return;
                }
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, data, true))
                {
                    Destroy(texture);
                    Logger.LogWarning("[Connect][UI] Could not decode connect-icon.png.");
                    return;
                }
                texture.name = "ConnectIcon";
                texture.filterMode = FilterMode.Bilinear;
                texture.hideFlags = HideFlags.HideAndDontSave;
                modIcon = texture;
            }
            catch (Exception exception)
            {
                Logger.LogWarning("[Connect][UI] Could not load connect-icon.png: " + exception.Message);
            }
        }

        private void RefreshInstallationHealth()
        {
            string pluginDirectory = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            string gameRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            installationHealth = InstallationHealth.Check(gameRoot, pluginDirectory, Info.Location);
            nextInstallHealthAt = Time.unscaledTime + 2f;
        }

        private string GitHubReleaseUrl
        {
            get
            {
                string configured = supportGithubReleaseUrlSetting == null ? ConnectSupportLinks.PublishedReleaseUrl : supportGithubReleaseUrlSetting.Value;
                if (ConnectSupportLinks.IsSafeGitHubUrl(configured)) return configured;
                return ConnectSupportLinks.IsSafeGitHubUrl(ConnectSupportLinks.PublishedReleaseUrl) ? ConnectSupportLinks.PublishedReleaseUrl : string.Empty;
            }
        }

        private void DrawInstallRecoveryNotice()
        {
            if (installNoticeDismissed || installationHealth == null || !installationHealth.RequiresRecovery) return;
            if (ui == null) ui = new RoundedUiTheme();
            List<string> missing = installationHealth.MissingParts();
            float width = Mathf.Min(570f, Screen.width - 36f);
            float height = 270f;
            Rect panel = new Rect((Screen.width - width) * 0.5f, Mathf.Max(30f, (Screen.height - height) * 0.33f), width, height);
            ui.Panel(panel, new Color(0.105f, 0.035f, 0.065f, 0.99f));
            ui.Card(new Rect(panel.x + 2f, panel.y + 2f, panel.width - 4f, 53f), new Color(0.20f, 0.035f, 0.09f, 0.98f));
            GUI.Label(new Rect(panel.x + 21f, panel.y + 15f, panel.width - 42f, 26f), "CONNECT SETUP REQUIRED", ui.Title);
            GUI.Label(new Rect(panel.x + 21f, panel.y + 63f, panel.width - 42f, 34f), "Some required Connect files are missing or incomplete. Close the game, restore the listed files, then start it through Steam.", ui.Small);
            float row = panel.y + 102f;
            for (int i = 0; i < missing.Count && i < 4; i++)
            {
                ui.Pill(new Rect(panel.x + 22f, row + 4f, 7f, 7f), new Color(1f, 0.28f, 0.48f, 1f));
                GUI.Label(new Rect(panel.x + 39f, row, panel.width - 61f, 19f), missing[i], ui.Label);
                row += 23f;
            }
            GUI.Label(new Rect(panel.x + 21f, panel.y + 197f, panel.width - 42f, 27f), "Required path:  People Playground\\BepInEx\\plugins\\Connect", ui.Small);
            if (DrawButton("install-guide", new Rect(panel.x + 21f, panel.y + height - 39f, 150f, 27f), "OPEN INSTALL GUIDE", new Color(0.35f, 0.11f, 0.20f, 1f), new Color(0.60f, 0.16f, 0.32f, 1f), ui.ButtonSmall)) Application.OpenURL(ConnectSupportLinks.BepInExInstallGuideUrl);
            string releaseUrl = GitHubReleaseUrl;
            if (!string.IsNullOrEmpty(releaseUrl))
            {
                if (DrawButton("open-github-release", new Rect(panel.x + 178f, panel.y + height - 39f, 127f, 27f), "OPEN CONNECT", new Color(0.40f, 0.07f, 0.20f, 1f), new Color(0.70f, 0.12f, 0.37f, 1f), ui.ButtonSmall)) Application.OpenURL(releaseUrl);
                if (DrawButton("copy-github-release", new Rect(panel.x + 312f, panel.y + height - 39f, 98f, 27f), "COPY LINK", new Color(0.30f, 0.09f, 0.17f, 1f), new Color(0.54f, 0.16f, 0.29f, 1f), ui.ButtonSmall)) { GUIUtility.systemCopyBuffer = releaseUrl; SetStatus("Connect GitHub release link copied."); }
            }
            else GUI.Label(new Rect(panel.x + 179f, panel.y + height - 34f, 225f, 18f), "Connect download link is unavailable.", ui.Small);
            if (DrawButton("dismiss-install-notice", new Rect(panel.x + panel.width - 103f, panel.y + height - 39f, 82f, 27f), "CLOSE", new Color(0.17f, 0.12f, 0.17f, 1f), new Color(0.32f, 0.16f, 0.24f, 1f), ui.ButtonSmall)) installNoticeDismissed = true;
        }

        private void SubscribeSteam()
        {
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
            SteamMatchmaking.OnLobbyDataChanged += OnLobbyDataChanged;
            SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeave;
            SteamUtils.OnSteamShutdown += OnSteamShutdown;
        }

        private void UnsubscribeSteam()
        {
            SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
            SteamMatchmaking.OnLobbyDataChanged -= OnLobbyDataChanged;
            SteamMatchmaking.OnLobbyMemberJoined -= OnLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeave;
            SteamUtils.OnSteamShutdown -= OnSteamShutdown;
        }

        private bool SteamReady()
        {
            return SteamClient.IsValid && SteamClient.IsLoggedOn;
        }

        private async void CreateLobbyAsync()
        {
            if (!SteamReady()) { SetStatus("Steam is not available. Launch People Playground through Steam."); return; }
            if (lobby.HasValue) { SetStatus("Leave the current lobby before creating another."); return; }
            try
            {
                Lobby? created = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);
                if (!created.HasValue) { SetStatus("Steam did not create a lobby."); return; }
                lobby = created.Value;
                ApplyLobbyPrivacy(lobby.Value);
                nonce = MakeNonce();
                WriteLobbyMetadata();
                transport.StartHost();
                ApplyHostPhysicsSettings();
                SetStatus("Lobby created. Invite friends with +.");
                Logger.LogInfo("[Connect][Lobby] Created " + (privacy == LobbyPrivacy.FriendsOnly ? "Friends Only" : privacy.ToString()) + " lobby " + (ulong)lobby.Value.Id);
            }
            catch (Exception exception)
            {
                SetStatus("Lobby create failed: " + exception.Message);
                Logger.LogError("[Connect][Lobby] " + exception);
            }
        }

        private async void JoinLobbyAsync(SteamId lobbyId)
        {
            if (!SteamReady()) { SetStatus("Steam is not available."); return; }
            if (lobby.HasValue && lobby.Value.Id != lobbyId) LeaveLobby();
            try
            {
                Lobby? joined = await SteamMatchmaking.JoinLobbyAsync(lobbyId);
                if (!joined.HasValue) { SetStatus("Steam refused the lobby join."); return; }
                lobby = joined.Value;
                EnsureLobbyPresenceCursors();
                string rawNonce = lobby.Value.GetData("ppgt_session_nonce");
                ulong parsed;
                if (!ulong.TryParse(rawNonce, out parsed) || parsed == 0)
                {
                    SetStatus("This lobby is not an active Connect lobby.");
                    return;
                }
                nonce = parsed;
                SteamId host = lobby.Value.Owner.Id;
                if (host == SteamClient.SteamId)
                {
                    transport.StartHost();
                    ApplyHostPhysicsSettings();
                    SetStatus("You own this Connect lobby.");
                }
                else
                {
                    ApplyLobbyMapDirective(lobby.Value);
                    transport.ConnectToHost(host);
                    SetStatus("Joined lobby; connecting to host through Steam Relay.");
                }
                Logger.LogInfo("[Connect][Lobby] Joined lobby " + (ulong)lobby.Value.Id);
            }
            catch (Exception exception)
            {
                SetStatus("Lobby join failed: " + exception.Message);
                Logger.LogError("[Connect][Lobby] " + exception);
            }
        }

        private void OnGameLobbyJoinRequested(Lobby requested, SteamId friend)
        {
            JoinLobbyAsync(requested.Id);
        }

        private void OnLobbyDataChanged(Lobby changed)
        {
            if (!lobby.HasValue || changed.Id != lobby.Value.Id) return;
            lobby = changed;
            string state = changed.GetData("ppgt_state");
            if (!IsHost) ApplyLobbyMapDirective(changed);
            if (!IsHost && state == "playing" && transport.Connected)
                SetStatus("Host session is running; awaiting welcome.");
            else if (!IsHost && state == "loading")
                SetStatus("Host is choosing or loading a map. You will follow automatically.");
        }

        private void OnLobbyMemberJoined(Lobby changed, Friend member)
        {
            if (lobby.HasValue && changed.Id == lobby.Value.Id)
            {
                EnsureLobbyPresenceCursor(member);
                SetStatus(SafeName(member.Name) + " joined the lobby.");
            }
        }

        private void OnLobbyMemberLeave(Lobby changed, Friend member)
        {
            if (lobby.HasValue && changed.Id == lobby.Value.Id)
            {
                RemoveCursorsForSteam((ulong)member.Id);
                SetStatus(SafeName(member.Name) + " left the lobby.");
            }
        }

        private void OnSteamShutdown()
        {
            SetStatus("Steam disconnected. Session closed.");
            Cleanup(false);
        }

        internal bool IsLobbyMember(SteamId steamId)
        {
            if (!lobby.HasValue || steamId == 0) return false;
            foreach (Friend friend in lobby.Value.Members)
                if (friend.Id == steamId) return true;
            return false;
        }

        internal void OnRelayClientConnected(ulong hostSteamId)
        {
            Logger.LogInfo("[Connect][Transport] Validating relay host identity " + hostSteamId + " against lobby owner.");
            if (!lobby.HasValue || (ulong)lobby.Value.Owner.Id != hostSteamId)
            {
                SetStatus("Relay identity does not match current lobby owner.");
                Logger.LogWarning("[Connect][Transport] Relay host identity validation failed. Lobby owner=" + (lobby.HasValue ? ((ulong)lobby.Value.Owner.Id).ToString() : "none") + ", relay host=" + hostSteamId + ".");
                transport.Close();
                return;
            }
            SendHello();
        }

        internal void OnHostTransportDisconnected()
        {
            SetStatus("Host left the session. No host migration is available.");
            sessionActive = false;
            hostStartAwaitingMap = false;
            clientSessionStartReceived = false;
            clientMapLoadPending = false;
            clientMapLoadIssued = false;
            clientMapLoadDeadline = 0f;
            clientMapReadyAt = 0f;
            clientRequestedMapIdentity = string.Empty;
            activeMapIdentity = string.Empty;
            clientGrabId = 0;
            clientGrabToken = 0;
            cursors.Clear();
        }

        internal void OnTransportDisconnected(ulong steamId)
        {
            Peer peer;
            if (peers.TryGetValue(steamId, out peer))
            {
                grabs.ReleasePeer(peer.PeerId);
                continuousActivations.ReleasePeer(peer.PeerId);
                peers.Remove(steamId);
                guestSpawnWindows.Remove(steamId);
                guestInteractionWindows.Remove(steamId);
                cursors.Remove(peer.PeerId);
                SetStatus("A player disconnected from Steam Relay.");
            }
        }

        internal void LogTransport(string message)
        {
            Logger.LogInfo("[Connect][Transport] " + message);
        }

        private void SendHello()
        {
            if (!lobby.HasValue)
            {
                Logger.LogWarning("[Connect][Transport] Cannot send Hello: no active Steam lobby.");
                return;
            }
            Writer writer = new Writer(128);
            writer.UShort(Wire.ProtocolVersion);
            writer.String(PluginVersion);
            writer.String(ExpectedGameVersion);
            writer.ULong((ulong)SteamClient.SteamId);
            writer.ULong((ulong)lobby.Value.Id);
            writer.ULong(nonce);
            SendToHost(WireMessage.Hello, WireChannel.Control, writer.ToArray(), true);
            Logger.LogInfo("[Connect][Transport] Sent Hello: local=" + (ulong)SteamClient.SteamId + ", lobby=" + (ulong)lobby.Value.Id + ", nonce=" + nonce + ", protocol=" + Wire.ProtocolVersion + ".");
        }

        private void ProcessReceivedPackets()
        {
            ReceivedPacket packet;
            int processed = 0;
            while (processed++ < 64 && transport.TryDequeue(out packet))
            {
                Envelope envelope;
                if (!Wire.TryUnpack(packet.Data, out envelope))
                {
                    Logger.LogWarning("[Connect][Protocol] Dropped invalid relay packet: sender=" + packet.SteamId + ", connection=" + packet.Connection.Id + ", bytes=" + (packet.Data == null ? 0 : packet.Data.Length) + ".");
                    continue;
                }
                if (envelope.Nonce != nonce)
                {
                    Logger.LogWarning("[Connect][Protocol] Dropped stale relay packet " + envelope.Type + ": sender=" + packet.SteamId + ", packet nonce=" + envelope.Nonce + ", active nonce=" + nonce + ".");
                    continue;
                }
                if (envelope.Type != WireMessage.Cursor && envelope.Type != WireMessage.Snapshot && envelope.Type != WireMessage.GrabUpdate)
                    Logger.LogInfo("[Connect][Protocol] Received " + envelope.Type + " from " + packet.SteamId + " on connection " + packet.Connection.Id + ", peer=" + envelope.PeerId + ", bytes=" + envelope.Payload.Length + ".");
                HandlePacket(packet, envelope);
            }
        }

        private void HandlePacket(ReceivedPacket packet, Envelope envelope)
        {
            if (envelope.Type == WireMessage.Hello && IsHost) { HandleHello(packet, envelope); return; }
            if (envelope.Type == WireMessage.Welcome && !IsHost) { HandleWelcome(envelope); return; }
            if (envelope.Type == WireMessage.Reject && !IsHost) { HandleReject(envelope); return; }
            if (envelope.Type == WireMessage.SessionStarted) { HandleSessionStarted(); return; }
            if (envelope.Type == WireMessage.SessionEnding) { sessionActive = false; ClearBotCursors(); SetStatus("Host ended the session."); return; }
            if (envelope.Type == WireMessage.MapLoad && !IsHost) { HandleMapLoad(envelope); return; }
            if (envelope.Type == WireMessage.ClientMapStatus && IsHost) { HandleClientMapStatus(packet, envelope); return; }
            if (envelope.Type == WireMessage.BotMode && !IsHost) { HandleBotMode(envelope); return; }
            if (envelope.Type == WireMessage.HostSettings && !IsHost) { HandleHostSettings(envelope); return; }
            if (envelope.Type == WireMessage.ActionDenied && !IsHost) { HandleActionDenied(envelope); return; }
            if (envelope.Type == WireMessage.Cursor) { HandleCursor(packet, envelope); return; }
            if (envelope.Type == WireMessage.GrabBegin && IsHost) { HandleGrabBegin(packet, envelope); return; }
            if (envelope.Type == WireMessage.GrabGranted && !IsHost) { HandleGrabGranted(envelope); return; }
            if (envelope.Type == WireMessage.GrabDenied && !IsHost) { HandleGrabDenied(envelope); return; }
            if (envelope.Type == WireMessage.GrabUpdate && IsHost) { HandleGrabUpdate(packet, envelope); return; }
            if (envelope.Type == WireMessage.GrabEnd && IsHost) { HandleGrabEnd(packet, envelope); return; }
            if (envelope.Type == WireMessage.Snapshot && !IsHost) { if (sessionActive) HandleSnapshot(envelope); return; }
            if (envelope.Type == WireMessage.SpawnRequest && IsHost) { HandleSpawnRequest(packet, envelope); return; }
            // World packets received while a guest is still in the title
            // scene must not instantiate objects there. The host sends a
            // reliable registered-world baseline immediately after this guest
            // reports PLAYING, so dropping the pre-ready copy is deliberate.
            if (envelope.Type == WireMessage.Spawn && !IsHost) { if (sessionActive) HandleSpawn(envelope); else Logger.LogInfo("[Connect][Spawn] Deferred Spawn until this client reports PLAYING; host baseline will resend it."); return; }
            if (envelope.Type == WireMessage.Despawn && !IsHost) { if (sessionActive) HandleDespawn(envelope); return; }
            if (envelope.Type == WireMessage.InteractionRequest && IsHost) { HandleInteractionRequest(packet, envelope); return; }
            Logger.LogWarning("[Connect][Protocol] Ignored " + envelope.Type + " for role " + (IsHost ? "HOST" : "CLIENT") + ".");
        }

        private void HandleHello(ReceivedPacket packet, Envelope envelope)
        {
            Reader reader = new Reader(envelope.Payload);
            ushort protocol; string modVersion; string gameVersion; ulong claimedSteam; ulong lobbyId; ulong suppliedNonce;
            if (!reader.UShort(out protocol) || !reader.String(out modVersion) || !reader.String(out gameVersion) || !reader.ULong(out claimedSteam) || !reader.ULong(out lobbyId) || !reader.ULong(out suppliedNonce) || reader.Remaining != 0)
            {
                Logger.LogWarning("[Connect][Transport] Rejected Hello from " + packet.SteamId + ": malformed payload.");
                SendReject(packet.Connection, "Invalid handshake payload"); return;
            }
            if (protocol != Wire.ProtocolVersion) { Logger.LogWarning("[Connect][Transport] Rejected client with protocol " + protocol + "; host requires " + Wire.ProtocolVersion + "."); SendReject(packet.Connection, "Wrong protocol version"); return; }
            if (modVersion != PluginVersion) { Logger.LogWarning("[Connect][Transport] Rejected client with Connect " + SafeName(modVersion) + "; host requires " + PluginVersion + "."); SendReject(packet.Connection, "Wrong Connect version"); return; }
            if (gameVersion != ExpectedGameVersion) { Logger.LogWarning("[Connect][Transport] Rejected client with People Playground " + SafeName(gameVersion) + "."); SendReject(packet.Connection, "Wrong People Playground version"); return; }
            if (!lobby.HasValue || claimedSteam != packet.SteamId || lobbyId != (ulong)lobby.Value.Id || suppliedNonce != nonce || !IsLobbyMember((SteamId)claimedSteam))
            {
                Logger.LogWarning("[Connect][Transport] Rejected Hello identity: transport=" + packet.SteamId + ", claimed=" + claimedSteam + ", lobby=" + lobbyId + ", activeLobby=" + (lobby.HasValue ? ((ulong)lobby.Value.Id).ToString() : "none") + ", nonce=" + suppliedNonce + ".");
                SendReject(packet.Connection, "Steam identity is not a member of this lobby"); return;
            }
            Peer peer;
            if (!peers.TryGetValue(packet.SteamId, out peer))
            {
                if (peers.Count + 1 >= maxPlayers) { Logger.LogWarning("[Connect][Transport] Rejected Hello from " + packet.SteamId + ": Connect lobby capacity reached."); SendReject(packet.Connection, "Lobby is full"); return; }
                peer = new Peer { SteamId = packet.SteamId, PeerId = nextPeerId++, Connection = packet.Connection, Name = SafeName(new Friend((SteamId)packet.SteamId).Name), MapStatus = sessionActive ? PeerMapStatus.LoadingMap : PeerMapStatus.InLobby };
                peers.Add(packet.SteamId, peer);
            }
            else peer.Connection = packet.Connection;
            RemoveCursorsForSteam(packet.SteamId);
            Writer response = new Writer(16);
            response.UShort(peer.PeerId);
            response.ULong((ulong)SteamClient.SteamId);
            response.Bool(sessionActive);
            SendToConnection(packet.Connection, WireMessage.Welcome, WireChannel.Control, peer.PeerId, response.ToArray(), true);
            EnsureCursor(peer.PeerId, peer.SteamId, peer.Name, GetWorldCursor(), false);
            SendCursorToConnection(packet.Connection, 0, (ulong)SteamClient.SteamId, GetWorldCursor(), Vector2.zero, false, true);
            if (sessionActive)
            {
                string mapIdentity;
                if (TryGetCurrentMapIdentity(out mapIdentity)) SendMapLoad(packet.Connection, peer.PeerId, mapIdentity);
                SendToConnection(packet.Connection, WireMessage.SessionStarted, WireChannel.Control, peer.PeerId, new byte[0], true);
            }
            if (sessionActive && botsEnabled) SendBotMode(packet.Connection, peer.PeerId, true, botCount);
            SendHostSettings(packet.Connection, peer.PeerId);
            Logger.LogInfo("[Connect][Transport] Relay handshake complete for " + peer.Name + " (peer " + peer.PeerId + ").");
            SetStatus(peer.Name + " connected through Steam Relay.");
        }

        private void HandleWelcome(Envelope envelope)
        {
            Reader reader = new Reader(envelope.Payload);
            ulong host; bool active;
            if (!reader.UShort(out clientPeerId) || !reader.ULong(out host) || !reader.Bool(out active) || reader.Remaining != 0) { SetStatus("Invalid host welcome."); return; }
            sessionActive = false;
            clientSessionStartReceived = active;
            EnsureLobbyPresenceCursors();
            SetStatus(active ? "Connected; waiting for the host map command." : "Connected; waiting for host to start session.");
            SendImmediateCursor();
            Logger.LogInfo("[Connect][Transport] Relay welcome received as peer " + clientPeerId + ".");
        }

        private void HandleSessionStarted()
        {
            if (IsHost)
            {
                sessionActive = true;
                return;
            }
            clientSessionStartReceived = true;
            TryActivateClientSession();
        }

        private void HandleMapLoad(Envelope envelope)
        {
            Reader reader = new Reader(envelope.Payload);
            string identity;
            if (!reader.String(out identity) || reader.Remaining != 0 || !IsValidMapIdentity(identity))
            {
                SetStatus("Host sent an invalid map identity.");
                return;
            }

            QueueClientMapLoad(identity, "Steam Relay");
        }

        private void HandleClientMapStatus(ReceivedPacket packet, Envelope envelope)
        {
            Peer peer;
            if (!peers.TryGetValue(packet.SteamId, out peer) || peer == null)
            {
                Logger.LogWarning("[Connect][Sync] Ignored map status from an unknown relay identity " + packet.SteamId + ".");
                return;
            }
            Reader reader = new Reader(envelope.Payload);
            byte rawStatus;
            string mapIdentity;
            if (envelope.PeerId != peer.PeerId || !reader.Byte(out rawStatus) || !reader.String(out mapIdentity) || reader.Remaining != 0 ||
                rawStatus < (byte)PeerMapStatus.LoadingMap || rawStatus > (byte)PeerMapStatus.Failed || !IsValidMapIdentity(mapIdentity))
            {
                Logger.LogWarning("[Connect][Sync] Ignored malformed map status from " + peer.Name + ".");
                return;
            }
            if (!sessionActive || !string.Equals(activeMapIdentity, mapIdentity, StringComparison.Ordinal))
            {
                Logger.LogWarning("[Connect][Sync] Ignored stale map status from " + peer.Name + ": " + mapIdentity + ".");
                return;
            }
            PeerMapStatus previousStatus = peer.MapStatus;
            peer.MapStatus = (PeerMapStatus)rawStatus;
            peer.MapIdentity = mapIdentity;
            Logger.LogInfo("[Connect][Sync] " + peer.Name + " reported " + PeerMapStatusLabel(peer.MapStatus) + " for host map " + mapIdentity + ".");
            if (peer.MapStatus == PeerMapStatus.Playing && previousStatus != PeerMapStatus.Playing)
                SendRegisteredWorldBaseline(peer);
            SetStatus(peer.Name + ": " + PeerMapStatusLabel(peer.MapStatus) + " host map.");
        }

        // Called by the narrow MapLoaderBehaviour Harmony postfix.  The host
        // is the only peer that broadcasts a map choice; a client merely
        // confirms that its own installed copy of the requested map has loaded.
        internal void OnLocalMapLoaded(MapLoaderBehaviour loader)
        {
            string identity;
            if (!TryGetMapIdentity(loader == null ? null : (MapLoaderBehaviour.CurrentMap ?? loader.MapLoadOverride), out identity))
            {
                Logger.LogWarning("[Connect][Sync] MapLoaderBehaviour.Load completed, but Connect could not read a valid map identity yet.");
                return;
            }
            Logger.LogInfo("[Connect][Sync] Local map loader completed: " + identity + ", role=" + (IsHost ? "HOST" : "CLIENT") + ".");

            if (IsHost && lobby.HasValue)
            {
                // A host that already has guests should not have to race the
                // Start button against the map UI.  Once that host enters a
                // real map, start/synchronise the session for those guests.
                if (hostStartAwaitingMap || (!sessionActive && lobby.Value.MemberCount > 1))
                    BeginHostSession(identity);
                else if (sessionActive && !string.Equals(activeMapIdentity, identity, StringComparison.Ordinal))
                    SynchroniseHostMapChange(identity);
                return;
            }

            if (!IsHost && clientMapLoadPending && string.Equals(clientRequestedMapIdentity, identity, StringComparison.Ordinal))
            {
                if (clientMapSceneTransitionPending)
                {
                    if (IsMapActuallyLoadedOn(loader, identity))
                        MarkClientMapLoaded("People Playground scene transition");
                    else
                        Logger.LogWarning("[Connect][Sync] The target sandbox scene reached MapLoaderBehaviour.Load, but the requested map root is not active yet. Waiting for the loader instead of falsely entering PLAYING.");
                }
                else if (IsRequestedClientMapActuallyLoaded())
                    MarkClientMapLoaded("MapLoaderBehaviour callback");
                else
                    Logger.LogWarning("[Connect][Sync] MapLoaderBehaviour.Load completed for the requested map, but no instantiated map root was found. Connect will keep waiting instead of entering a false PLAYING state.");
            }
        }

        private void UpdateMapSynchronisation()
        {
            if (IsHost && sessionActive)
            {
                string hostMap;
                if (TryGetCurrentMapIdentity(out hostMap) && !string.Equals(hostMap, activeMapIdentity, StringComparison.Ordinal))
                    SynchroniseHostMapChange(hostMap);
                return;
            }

            if (IsHost) return;
            // Steam may deliver a lobby data callback before the gameplay
            // scene's MapLoaderBehaviour exists. Re-read the tiny map
            // directive while joining so a valid host map can never be lost
            // solely because of callback timing.
            if (lobby.HasValue && !sessionActive && !clientMapLoadPending)
                ApplyLobbyMapDirective(lobby.Value);
            if (!clientMapLoadPending)
            {
                TryActivateClientSession();
                return;
            }
            // CurrentMap alone is not proof that the player left the title
            // screen: People Playground stores a selected map there before it
            // constructs the map.  Probe at 10 Hz while loading, never every
            // frame, and only activate after a map root really exists.
            if (Time.unscaledTime >= nextClientMapProbeAt)
            {
                nextClientMapProbeAt = Time.unscaledTime + 0.10f;
                if (IsRequestedClientMapActuallyLoaded())
                    MarkClientMapLoaded("map instance probe");
            }
            if (!clientMapLoadPending)
            {
                TryActivateClientSession();
                return;
            }
            if (Time.unscaledTime >= clientMapLoadDeadline)
            {
                clientMapLoadPending = false;
                clientSessionStartReceived = false;
                SendClientMapStatus(PeerMapStatus.Failed, clientRequestedMapIdentity);
                SetStatus("Map synchronisation timed out. The host map is unavailable locally.");
            }
            else if (Time.unscaledTime >= nextClientMapLoadAttemptAt)
            {
                TryBeginClientMapLoad();
            }

            TryActivateClientSession();
        }

        // Lobby metadata is only a compact map directive. It is never used for
        // cursors, physics, objects or other gameplay state. It closes the
        // race where the host selects a map just before relay handshake ends.
        private void ApplyLobbyMapDirective(Lobby source)
        {
            if (IsHost) return;
            string state = source.GetData("ppgt_state");
            string identity = source.GetData("ppgt_map_id");
            if ((state == "loading" || state == "playing") && IsValidMapIdentity(identity))
            {
                // Lobby-data notifications can be repeated many times while a
                // scene is loading. Do not reset a successful in-flight load
                // back to LOADING/SYNCING on every callback.
                if (string.Equals(clientRequestedMapIdentity, identity, StringComparison.Ordinal) &&
                    (clientMapLoadPending || clientMapLoadIssued || clientMapInstanceLoaded)) return;
                Logger.LogInfo("[Connect][Sync] Lobby map directive: state=" + state + ", map=" + identity + ".");
                QueueClientMapLoad(identity, "Steam Lobby");
            }
        }

        private void QueueClientMapLoad(string identity, string source)
        {
            if (IsHost || !IsValidMapIdentity(identity)) return;
            if (sessionActive && string.Equals(activeMapIdentity, identity, StringComparison.Ordinal)) return;
            if (string.Equals(clientRequestedMapIdentity, identity, StringComparison.Ordinal) &&
                (clientMapLoadPending || clientMapLoadIssued || clientMapInstanceLoaded)) return;
            clientRequestedMapIdentity = identity;
            ResetNetworkWorldForMapTransition();
            clientMapLoadPending = true;
            clientMapLoadIssued = false;
            clientMapInstanceLoaded = false;
            clientMapSceneTransitionPending = false;
            clientMapLoadDeadline = Time.unscaledTime + 30f;
            clientMapReadyAt = 0f;
            nextClientMapProbeAt = 0f;
            nextClientMapLoadAttemptAt = 0f;
            clientRequestedSceneName = string.Empty;
            SetStatus(source + " requested host map: " + SafeName(identity));
            Logger.LogInfo("[Connect][Sync] " + source + " map directive received: " + identity + ".");
            SendClientMapStatus(PeerMapStatus.LoadingMap, identity);
            TryBeginClientMapLoad();
        }

        private void TryActivateClientSession()
        {
            if (IsHost || sessionActive || !clientSessionStartReceived || clientMapLoadPending || clientMapSceneTransitionPending) return;
            if (string.IsNullOrEmpty(clientRequestedMapIdentity))
            {
                SetStatus("Host session started, but no map command has arrived yet.");
                return;
            }
            if (clientMapReadyAt > Time.unscaledTime) return;
            if (!clientMapInstanceLoaded) return;
            string currentMap;
            if (!TryGetCurrentMapIdentity(out currentMap) || !string.Equals(currentMap, clientRequestedMapIdentity, StringComparison.Ordinal)) return;
            sessionActive = true;
            activeMapIdentity = currentMap;
            SendImmediateCursor();
            SendClientMapStatus(PeerMapStatus.Playing, currentMap);
            SetStatus("Session active on host map: " + SafeName(currentMap));
        }

        private void TryBeginClientMapLoad()
        {
            if (!clientMapLoadPending || string.IsNullOrEmpty(clientRequestedMapIdentity)) return;
            // CurrentMap can be assigned in the title menu. Only accept an
            // already-instantiated root before issuing a load when the active
            // scene is known to be the sandbox scene; otherwise title-menu
            // remnants can produce a false ready result.
            if (!clientMapSceneTransitionPending && IsSandboxSceneActive() && IsRequestedClientMapActuallyLoaded())
            {
                MarkClientMapLoaded("pre-load map instance check");
                return;
            }
            if (clientMapSceneTransitionPending || clientMapLoadIssued) return;

            // A missing loader or a menu transition can legitimately take a
            // moment. Retrying a few times per second avoids Resource scans
            // and duplicate warning spam in Update.
            nextClientMapLoadAttemptAt = Time.unscaledTime + 0.25f;

            Map map = FindInstalledMap(clientRequestedMapIdentity);
            if (map == null)
            {
                SetStatus("Waiting for local map: " + SafeName(clientRequestedMapIdentity));
                Logger.LogWarning("[Connect][Sync] Map " + clientRequestedMapIdentity + " is not in the local installed catalogue yet.");
                return;
            }

            // Selecting a map in the base game performs a scene transition;
            // MapLoaderBehaviour.Load by itself only creates a prefab and
            // leaves title-menu UI alive. Follow the exact base-game path so
            // the client enters the sandbox scene before its map loader runs.
            MapViewBehaviour mapView = FindMapView(map);
            SceneSwitchBehaviour sceneSwitch = mapView == null ? null : mapView.GetComponent<SceneSwitchBehaviour>();
            if (sceneSwitch != null && !string.IsNullOrEmpty(sceneSwitch.SceneName))
            {
                MapLoaderBehaviour.CurrentMap = map;
                clientMapLoadIssued = true;
                clientMapSceneTransitionPending = true;
                clientRequestedSceneName = sceneSwitch.SceneName;
                SetStatus("Loading host map through People Playground scene transition: " + SafeName(clientRequestedMapIdentity));
                Logger.LogInfo("[Connect][Sync] Client requested base-game scene switch '" + SafeName(clientRequestedSceneName) + "' for host map " + clientRequestedMapIdentity + ".");
                clientConnectSceneSwitchCall = true;
                try
                {
                    sceneSwitch.Switch();
                }
                finally
                {
                    clientConnectSceneSwitchCall = false;
                }
                return;
            }

            // At the title screen no map-card GameObject and no map loader are
            // active, which is exactly why the old path waited forever for the
            // player to press Play. The normal map-card path is confirmed to
            // enter the local "Main" sandbox scene. Do that same local scene
            // transition after assigning only the host-selected installed map.
            MapLoaderBehaviour.CurrentMap = map;
            clientMapLoadIssued = true;
            clientMapSceneTransitionPending = true;
            clientRequestedSceneName = SandboxSceneName;
            try
            {
                AsyncOperation operation = SceneManager.LoadSceneAsync(SandboxSceneName, LoadSceneMode.Single);
                if (operation != null)
                {
                    SetStatus("Loading host map automatically through People Playground sandbox scene.");
                    Logger.LogInfo("[Connect][Sync] Guest title-menu fallback started confirmed sandbox scene '" + SandboxSceneName + "' for host map " + clientRequestedMapIdentity + ".");
                    return;
                }
            }
            catch (Exception exception)
            {
                Logger.LogWarning("[Connect][Sync] Direct sandbox scene transition failed: " + exception.Message);
            }
            clientMapLoadIssued = false;
            clientMapSceneTransitionPending = false;
            clientRequestedSceneName = string.Empty;

            Logger.LogWarning("[Connect][Sync] Direct sandbox scene transition was unavailable for " + clientRequestedMapIdentity + ". Falling back to the current scene's MapLoaderBehaviour.");
            MapLoaderBehaviour loader = FindMapLoader();
            if (loader == null)
            {
                SetStatus("Waiting for People Playground map loader.");
                Logger.LogWarning("[Connect][Sync] Cannot load host map yet: no MapLoaderBehaviour was found.");
                return;
            }

            int childrenBefore = loader.transform == null ? -1 : loader.transform.childCount;
            // In People Playground 1.27.16 MapLoadOverride is honoured only
            // in Application.isEditor. Runtime MapLoaderBehaviour.Load()
            // instantiates its public static CurrentMap instead. Set both so
            // this remains safe in either context, then use the game's own
            // loader rather than creating prefabs ourselves.
            MapLoaderBehaviour.CurrentMap = map;
            loader.MapLoadOverride = map;
            clientMapLoadIssued = true;
            SetStatus("Loading host map: " + SafeName(clientRequestedMapIdentity));
            Logger.LogInfo("[Connect][Sync] Loading host map with MapLoaderBehaviour " + loader.GetInstanceID() + ", target=" + clientRequestedMapIdentity + ", children-before=" + childrenBefore + ". CurrentMap was assigned explicitly for runtime loading.");
            loader.Load();
            int childrenAfter = loader.transform == null ? -1 : loader.transform.childCount;
            Logger.LogInfo("[Connect][Sync] Issued local map load for " + clientRequestedMapIdentity + ". MapLoader children-after=" + childrenAfter + ".");
            if (clientMapLoadPending && IsRequestedClientMapActuallyLoaded())
                MarkClientMapLoaded("post-load map instance check");
        }

        private void MarkClientMapLoaded(string evidence)
        {
            if (!clientMapLoadPending) return;
            clientMapInstanceLoaded = true;
            clientMapLoadPending = false;
            clientMapSceneTransitionPending = false;
            clientRequestedSceneName = string.Empty;
            clientMapReadyAt = Time.unscaledTime + 0.20f;
            SetStatus("Loaded host map. Synchronising Connect session.");
            Logger.LogInfo("[Connect][Sync] Client verified an instantiated host map via " + evidence + ": " + clientRequestedMapIdentity + ".");
            SendClientMapStatus(PeerMapStatus.Synchronising, clientRequestedMapIdentity);
        }

        private void BeginHostSession(string mapIdentity)
        {
            if (!IsHost || !lobby.HasValue || !IsValidMapIdentity(mapIdentity)) return;
            hostStartAwaitingMap = false;
            sessionActive = true;
            activeMapIdentity = mapIdentity;
            SetPeerMapStatus(PeerMapStatus.LoadingMap, mapIdentity);
            lobby.Value.SetData("ppgt_state", "loading");
            lobby.Value.SetData("ppgt_map_id", mapIdentity);
            BroadcastMapLoad(mapIdentity);
            Broadcast(WireMessage.SessionStarted, WireChannel.Control, new byte[0], true);
            BroadcastHostSettings();
            lobby.Value.SetData("ppgt_state", "playing");
            SendImmediateCursor();
            Logger.LogInfo("[Connect][Sync] Host session start broadcast: map=" + mapIdentity + ", connected peers=" + peers.Count + ".");
            SetStatus("Session started. Loading " + SafeName(mapIdentity) + " for every connected player.");
        }

        private void SynchroniseHostMapChange(string mapIdentity)
        {
            if (!IsHost || !lobby.HasValue || !IsValidMapIdentity(mapIdentity)) return;
            ResetNetworkWorldForMapTransition();
            activeMapIdentity = mapIdentity;
            SetPeerMapStatus(PeerMapStatus.LoadingMap, mapIdentity);
            lobby.Value.SetData("ppgt_state", "loading");
            lobby.Value.SetData("ppgt_map_id", mapIdentity);
            BroadcastMapLoad(mapIdentity);
            Broadcast(WireMessage.SessionStarted, WireChannel.Control, new byte[0], true);
            lobby.Value.SetData("ppgt_state", "playing");
            SetStatus("Host map changed. Loading " + SafeName(mapIdentity) + " for every connected player.");
        }

        private void BroadcastMapLoad(string mapIdentity)
        {
            foreach (Peer peer in peers.Values) SendMapLoad(peer.Connection, peer.PeerId, mapIdentity);
        }

        private void SendMapLoad(Connection connection, ushort peerId, string mapIdentity)
        {
            if (connection == null || !IsValidMapIdentity(mapIdentity)) return;
            Writer writer = new Writer(128);
            writer.String(mapIdentity);
            SendToConnection(connection, WireMessage.MapLoad, WireChannel.Control, peerId, writer.ToArray(), true);
        }

        // A guest can finish the base-game scene transition after objects have
        // already been spawned by the host or another player.  Steam Lobby
        // metadata intentionally never contains world data, so send a bounded
        // per-peer baseline only after that guest reports PLAYING for the exact
        // current host map. Reliable Spawn events establish IDs first; normal
        // host snapshots then keep those objects moving.
        private void SendRegisteredWorldBaseline(Peer peer)
        {
            if (!IsHost || peer == null || peer.Connection == null) return;
            int sent = 0;
            foreach (PPGTogetherIdentity identity in registry.All())
            {
                if (identity == null || identity.gameObject == null || string.IsNullOrEmpty(identity.SpawnKey)) continue;
                SendSpawnToConnection(peer.Connection, peer.PeerId, identity, identity.gameObject);
                sent++;
            }
            Logger.LogInfo("[Connect][Sync] Sent registered-world baseline to " + peer.Name + ": " + sent + " object(s).");
        }

        private void ResetNetworkWorldForMapTransition()
        {
            grabs.Clear();
            continuousActivations.Clear();
            botCoordination.Clear();
            botWorld.Clear();
            botCatalog.Clear();
            clientHeldActivationRoots.Clear();
            clientGrabId = 0;
            clientGrabToken = 0;
            registry.Clear();
            botSpawnedItems.Clear();
            botSpawnCount = 0;
            Logger.LogInfo("[Connect][World] Cleared registered network world for map transition.");
        }

        private void SetPeerMapStatus(PeerMapStatus mapStatus, string mapIdentity)
        {
            foreach (Peer peer in peers.Values)
            {
                peer.MapStatus = mapStatus;
                peer.MapIdentity = mapIdentity;
            }
        }

        private void SendClientMapStatus(PeerMapStatus mapStatus, string mapIdentity)
        {
            if (IsHost || !lobby.HasValue || clientPeerId == 0 || transport == null || !transport.Connected || !IsValidMapIdentity(mapIdentity)) return;
            Writer writer = new Writer(80);
            writer.Byte((byte)mapStatus);
            writer.String(mapIdentity);
            SendToHost(WireMessage.ClientMapStatus, WireChannel.Control, writer.ToArray(), true);
            Logger.LogInfo("[Connect][Sync] Sent host map status " + PeerMapStatusLabel(mapStatus) + " for " + mapIdentity + ".");
        }

        private static string PeerMapStatusLabel(PeerMapStatus mapStatus)
        {
            switch (mapStatus)
            {
                case PeerMapStatus.LoadingMap: return "LOADING MAP";
                case PeerMapStatus.Synchronising: return "SYNCING";
                case PeerMapStatus.Playing: return "PLAYING";
                case PeerMapStatus.Failed: return "MAP FAILED";
                default: return "IN LOBBY";
            }
        }

        private static MapLoaderBehaviour FindMapLoader()
        {
            MapLoaderBehaviour[] loaders = Resources.FindObjectsOfTypeAll<MapLoaderBehaviour>();
            MapLoaderBehaviour fallback = null;
            for (int i = 0; i < loaders.Length; i++)
                if (loaders[i] != null && loaders[i].gameObject != null)
                {
                    if (fallback == null) fallback = loaders[i];
                    if (loaders[i].gameObject.activeInHierarchy) return loaders[i];
                }
            return fallback;
        }

        private static MapViewBehaviour FindMapView(Map map)
        {
            MapViewBehaviour[] views = Resources.FindObjectsOfTypeAll<MapViewBehaviour>();
            for (int i = 0; i < views.Length; i++)
            {
                MapViewBehaviour view = views[i];
                if (view != null && view.gameObject != null && view.gameObject.activeInHierarchy && view.Map == map) return view;
            }
            return null;
        }

        private bool IsRequestedClientMapActuallyLoaded()
        {
            if (clientMapSceneTransitionPending) return false;
            return IsMapActuallyLoaded(clientRequestedMapIdentity);
        }

        private static bool IsSandboxSceneActive()
        {
            Scene active = SceneManager.GetActiveScene();
            return active.IsValid() && string.Equals(active.name, SandboxSceneName, StringComparison.Ordinal);
        }

        // MapLoaderBehaviour.CurrentMap is a selection, not a load-complete
        // signal. MapLoaderBehaviour.Load creates the active map prefab as a
        // direct child of one of the loader transforms, which is the stable
        // runtime evidence needed by Connect's client state machine.
        private static bool IsMapActuallyLoaded(string identity)
        {
            string currentMap;
            if (!IsValidMapIdentity(identity) || !TryGetCurrentMapIdentity(out currentMap) || !string.Equals(currentMap, identity, StringComparison.Ordinal)) return false;
            return IsMapActuallyLoadedOn(FindMapLoader(), identity);
        }

        private static bool IsMapActuallyLoadedOn(MapLoaderBehaviour loader, string identity)
        {
            string currentMap;
            if (loader == null || loader.transform == null || !TryGetCurrentMapIdentity(out currentMap) || !string.Equals(currentMap, identity, StringComparison.Ordinal)) return false;
            if (loader.transform.childCount <= 0) return false;

            // A map prefab instance is the direct child produced by the
            // game's MapLoaderBehaviour. Matching its source name prevents
            // an unrelated title-menu child from becoming a false ready flag.
            Map current = MapLoaderBehaviour.CurrentMap;
            if (current != null && current.Prefab != null)
            {
                string prefabName = current.Prefab.name;
                for (int i = 0; i < loader.transform.childCount; i++)
                {
                    Transform child = loader.transform.GetChild(i);
                    if (child == null || child.gameObject == null || !child.gameObject.activeInHierarchy) continue;
                    if (string.Equals(child.gameObject.name, prefabName, StringComparison.Ordinal) || string.Equals(child.gameObject.name, prefabName + "(Clone)", StringComparison.Ordinal)) return true;
                }
                return false;
            }

            // Custom maps can use InstantiateOverride and have no prefab
            // object to name-match. For those maps an active direct child is
            // the best bounded proof available from the public loader API.
            for (int i = 0; i < loader.transform.childCount; i++)
            {
                Transform child = loader.transform.GetChild(i);
                if (child != null && child.gameObject != null && child.gameObject.activeInHierarchy) return true;
            }
            return false;
        }

        private static Map FindInstalledMap(string identity)
        {
            BackgroundItemLoader background = BackgroundItemLoader.Instance;
            if (background != null && background.BuiltInMaps != null)
            {
                for (int i = 0; i < background.BuiltInMaps.Length; i++)
                    if (MapMatches(background.BuiltInMaps[i], identity)) return background.BuiltInMaps[i];
            }

            MapViewBehaviour[] views = Resources.FindObjectsOfTypeAll<MapViewBehaviour>();
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null && MapMatches(views[i].Map, identity)) return views[i].Map;

            Map[] maps = Resources.FindObjectsOfTypeAll<Map>();
            for (int i = 0; i < maps.Length; i++)
                if (MapMatches(maps[i], identity)) return maps[i];
            return null;
        }

        private static bool TryGetCurrentMapIdentity(out string identity)
        {
            identity = string.Empty;
            if (TryGetMapIdentity(MapLoaderBehaviour.CurrentMap, out identity)) return true;
            return false;
        }

        private static bool TryGetMapIdentity(Map map, out string identity)
        {
            identity = map == null ? string.Empty : map.UniqueIdentity;
            return IsValidMapIdentity(identity);
        }

        private static bool MapMatches(Map map, string identity)
        {
            string candidate;
            return TryGetMapIdentity(map, out candidate) && string.Equals(candidate, identity, StringComparison.Ordinal);
        }

        private static bool IsValidMapIdentity(string identity)
        {
            return !string.IsNullOrWhiteSpace(identity) && Encoding.UTF8.GetByteCount(identity) <= Wire.MaxStringBytes;
        }

        private void HandleReject(Envelope envelope)
        {
            Reader reader = new Reader(envelope.Payload); string reason;
            if (reader.String(out reason)) SetStatus("Connection rejected: " + reason);
            transport.Close();
        }

        private bool HasCursorRelay()
        {
            if (IsHost) return peers.Count > 0;
            return lobby.HasValue && clientPeerId != 0 && transport != null && transport.Connected;
        }

        private void SendImmediateCursor()
        {
            nextCursorAt = 0f;
            if (HasCursorRelay()) UpdateCursorNetwork();
        }

        private void EnsureCursor(ushort peerId, ulong steamId, string name, Vector2 position, bool isBot)
        {
            RemoteCursor cursor;
            if (cursors.TryGetValue(peerId, out cursor)) return;
            cursor = new RemoteCursor
            {
                PeerId = peerId,
                SteamId = steamId,
                Name = isBot ? name : SafeName(name),
                Color = isBot ? BotColor(peerId - BotPeerBase) : CursorColor(peerId),
                Target = position,
                Render = position,
                LastAt = Time.unscaledTime,
                IsBot = isBot
            };
            cursors.Add(peerId, cursor);
        }

        // A lobby member gets a clearly marked provisional cursor immediately.
        // It is replaced by the real world-space cursor as soon as the relay
        // handshake supplies an assigned peer id and the first cursor packet.
        private void EnsureLobbyPresenceCursors()
        {
            if (!lobby.HasValue) return;
            foreach (Friend member in lobby.Value.Members) EnsureLobbyPresenceCursor(member);
        }

        private void EnsureLobbyPresenceCursor(Friend member)
        {
            ulong steamId = (ulong)member.Id;
            if (steamId == 0 || steamId == (ulong)SteamClient.SteamId) return;
            foreach (RemoteCursor existing in cursors.Values)
                if (existing != null && existing.SteamId == steamId) return;

            ushort peerId = AllocateLobbyPresencePeerId(steamId);
            Vector2 position = GetWorldCursor();
            cursors.Add(peerId, new RemoteCursor
            {
                PeerId = peerId,
                SteamId = steamId,
                Name = SafeName(member.Name),
                Color = CursorColor(peerId),
                Target = position,
                Render = position,
                LastAt = Time.unscaledTime,
                IsProvisional = true
            });
        }

        private ushort AllocateLobbyPresencePeerId(ulong steamId)
        {
            ushort candidate = (ushort)(1000UL + (steamId % 58000UL));
            RemoteCursor existing;
            while (cursors.TryGetValue(candidate, out existing) && existing != null && existing.SteamId != steamId)
            {
                candidate++;
                if (candidate >= BotPeerBase) candidate = 1000;
            }
            return candidate;
        }

        private void RemoveCursorsForSteam(ulong steamId)
        {
            if (steamId == 0) return;
            List<ushort> remove = new List<ushort>();
            foreach (KeyValuePair<ushort, RemoteCursor> pair in cursors)
                if (pair.Value != null && pair.Value.SteamId == steamId) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++) cursors.Remove(remove[i]);
        }

        private void RemoveProvisionalCursorsForSteam(ulong steamId)
        {
            List<ushort> remove = new List<ushort>();
            foreach (KeyValuePair<ushort, RemoteCursor> pair in cursors)
                if (pair.Value != null && pair.Value.SteamId == steamId && pair.Value.IsProvisional) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++) cursors.Remove(remove[i]);
        }

        private void SendCursorToConnection(Connection connection, ushort ownerPeerId, ulong steamId, Vector2 position, Vector2 velocity, bool primaryDown, bool uiBusy)
        {
            if (connection == null) return;
            byte[] body = CursorPayloadCodec.Encode(steamId, position.x, position.y, velocity.x, velocity.y, primaryDown ? (byte)1 : (byte)0, uiBusy);
            SendToConnection(connection, WireMessage.Cursor, WireChannel.Cursor, ownerPeerId, body, false);
        }

        private void UpdateCursorNetwork()
        {
            bool uiBusy = menuVisible || (Global.main != null && Global.main.UILock);
            if (Time.unscaledTime < nextCursorAt) return;
            // UI busy is conveyed as an interaction flag only. Throttling the
            // cursor itself while a player was in the menu made remote cursors
            // appear frozen at two updates per second.
            nextCursorAt = Time.unscaledTime + (1f / CursorSendRateHz());
            Vector2 position = GetWorldCursor();
            Vector2 velocity = Vector2.zero;
            if (hasPreviousLocalCursor)
            {
                float elapsed = Time.unscaledTime - previousLocalCursorAt;
                if (elapsed > 0.0001f)
                    velocity = Vector2.ClampMagnitude((position - previousLocalCursor) / elapsed, 250f);
            }
            previousLocalCursor = position;
            previousLocalCursorAt = Time.unscaledTime;
            hasPreviousLocalCursor = true;
            byte[] body = CursorPayloadCodec.Encode((ulong)SteamClient.SteamId, position.x, position.y, velocity.x, velocity.y, (byte)(Input.GetMouseButton(0) ? 1 : 0), uiBusy);
            // The envelope PeerId identifies the cursor's owner, not the recipient.
            // The host is always peer 0, so retain it when relaying its own cursor.
            if (IsHost) BroadcastFromPeer(WireMessage.Cursor, WireChannel.Cursor, 0, body, false);
            else SendToHost(WireMessage.Cursor, WireChannel.Cursor, body, false);
        }

        private void SetBotMode(bool enabled)
        {
            if (!IsHost || !sessionActive) { SetStatus("Only the active session host can change Bot Mode."); return; }
            if (enabled && !hostBotsAllowedSetting.Value) { SetStatus("Bots are disabled in Host Settings."); return; }
            if (enabled)
            {
                botsEnabled = true;
                BuildBots();
                BroadcastBotMode(true, botCount);
                SetStatus("Bot Mode enabled: " + botCount + " autonomous cursors.");
                return;
            }
            botsEnabled = false;
            ReleaseBots();
            bots.Clear();
            ClearBotCursors();
            BroadcastBotMode(false, 0);
            SetStatus("Bot Mode disabled.");
        }

        private void BuildBots()
        {
            ReleaseBots();
            ClearBotCursors();
            bots.Clear();
            Vector2 origin = GetWorldCursor();
            float now = Time.unscaledTime;
            for (int i = 0; i < botCount; i++)
            {
                float angle = (Mathf.PI * 2f * i) / Mathf.Max(1, botCount);
                Vector2 position = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 3.5f;
                BotAgent bot = new BotAgent
                {
                    Index = i,
                    PeerId = (ushort)(BotPeerBase + i),
                    SteamId = 0xF000000000000000UL + (ulong)(i + 1),
                    Name = BotDisplayName(i),
                    Mind = new BotMind((BotPersonality)(i % 3), (uint)(0xC011EC7u + (uint)i * 2654435761u)),
                    Origin = origin,
                    Position = position,
                    Target = position,
                    Action = BotAction.Idle,
                    NextDecisionAt = now + 0.45f + i * 0.35f,
                    NextBroadcastAt = now
                };
                bots.Add(bot);
                PublishBotCursor(bot);
            }
        }

        private void UpdateBots()
        {
            // A host may start a Steam session while still on the title screen.
            // Do not instantiate fallback prefabs or apply physics until the
            // actual People Playground sandbox has supplied its catalog/world.
            if (!IsHost || !sessionActive || !botsEnabled || !BotWorldReady()) return;
            float now = Time.unscaledTime;
            botWorld.Refresh(registry, now);
            botCatalog.Refresh(now, LogBotCatalogWarning);
            PruneBotSpawnedItems();
            for (int i = 0; i < bots.Count; i++)
            {
                BotAgent bot = bots[i];
                if (now >= bot.ActionUntil && bot.Action != BotAction.Idle && bot.Mind != null)
                    bot.Mind.ReportOutcome(bot.PeerId, BotOutcome.Timeout, botCoordination, now);
                if (bot.Action == BotAction.Idle || now >= bot.ActionUntil)
                    PlanBotAction(bot, now);

                Vector2 previous = bot.Position;
                bot.Position = Vector2.MoveTowards(bot.Position, bot.Target, Time.unscaledDeltaTime * (2.15f + bot.Index * 0.30f));
                bot.Velocity = Time.unscaledDeltaTime > 0.0001f ? (bot.Position - previous) / Time.unscaledDeltaTime : Vector2.zero;

                if (bot.Action == BotAction.Spawn && Reached(bot.Position, bot.Target))
                {
                    SpawnBotItem(bot, now);
                }
                else if (bot.Action == BotAction.GrabAndPlace)
                {
                    UpdateBotGrab(bot, now);
                }
                else if (bot.Action == BotAction.Cleanup && Reached(bot.Position, bot.Target))
                {
                    CleanupBotItem(bot);
                }
                else if (bot.Action == BotAction.Activate && Reached(bot.Position, bot.Target))
                {
                    ActivateBotItem(bot, now);
                }
                else if ((bot.Action == BotAction.Explore || bot.Action == BotAction.Inspect || bot.Action == BotAction.Recover || bot.Action == BotAction.Wander) && Reached(bot.Position, bot.Target))
                {
                    FinishBotAction(bot, now, BotOutcome.Success);
                }

                if (now >= bot.NextBroadcastAt)
                {
                    bot.NextBroadcastAt = now + (1f / 20f);
                    PublishBotCursor(bot);
                }
            }
        }

        private void PlanBotAction(BotAgent bot, float now)
        {
            if (bot == null || bot.Mind == null) return;
            BotPerception perception = botWorld.Perceive(bot.Position, botSpawnCount < BotSpawnLimit(), hostBotInteractionsSetting.Value, true, hostBotCleanupSetting.Value, now);
            BotDecision decision = bot.Mind.Decide(bot.PeerId, perception, botCoordination, now);
            bot.Decision = decision;
            bot.Action = decision == null ? BotAction.Explore : decision.Action;
            if (decision != null) Logger.LogDebug("[Connect][Bots] " + bot.Name + " chose " + decision.Goal + "/" + decision.Action + " (" + decision.Rationale + ", utility=" + decision.Utility.ToString("0.00") + ").");
            bot.ActionUntil = now + BotActionTimeout;
            bot.CurrentItem = null;
            bot.GrabNetId = 0;
            bot.GrabToken = 0;
            if (decision == null) { bot.Target = bot.Position; bot.Action = BotAction.Idle; return; }
            bot.Target = ToUnityPoint(decision.Target);
            bot.PlaceTarget = ToUnityPoint(decision.Placement);
            if (bot.Action == BotAction.Spawn || bot.Action == BotAction.Explore || bot.Action == BotAction.Inspect || bot.Action == BotAction.Recover) return;
            if (bot.Action == BotAction.GrabAndPlace)
            {
                BotSpawnRecord record = FindRegisteredBotItem(decision.TargetKey);
                if (record != null && TryGetBotInteractionPoint(record, out bot.InteractionPoint)) { bot.CurrentItem = record; bot.Target = bot.InteractionPoint; return; }
            }
            else if (bot.Action == BotAction.Cleanup)
            {
                // Cleanup is intentionally limited to bot-created records. A
                // bot may study player items, but never deletes them itself.
                BotSpawnRecord record = FindBotCreatedItem(true);
                if (record != null && TryGetBotInteractionPoint(record, out bot.InteractionPoint)) { bot.CurrentItem = record; bot.Target = bot.InteractionPoint; return; }
            }
            else if (bot.Action == BotAction.Activate)
            {
                BotSpawnRecord record = FindRegisteredBotItem(decision.TargetKey);
                if (record != null && TryGetBotInteractionPoint(record, out bot.InteractionPoint)) { bot.CurrentItem = record; bot.Target = bot.InteractionPoint; return; }
            }
            bot.Mind.ReportOutcome(bot.PeerId, BotOutcome.MissingTarget, botCoordination, now);
            bot.Action = BotAction.Idle;
        }

        private void SpawnBotItem(BotAgent bot, float now)
        {
            if (botSpawnCount >= BotSpawnLimit()) { FinishBotAction(bot, now, BotOutcome.Denied); return; }
            BotSpawnChoice choice = bot.Decision == null ? null : botCatalog.Select(bot.Decision.SpawnKind, bot.Index);
            if (choice == null || choice.Asset == null) { FinishBotAction(bot, now, BotOutcome.MissingTarget); return; }
            SpawnableAsset asset = choice.Asset;
            GameObject created = SpawnAndReplicate(asset, bot.Position);
            if (created == null) { FinishBotAction(bot, now, BotOutcome.Denied); return; }
            PPGTogetherIdentity identity = created.GetComponent<PPGTogetherIdentity>();
            if (identity == null || identity.NetId == 0) { FinishBotAction(bot, now, BotOutcome.MissingTarget); return; }
            botSpawnCount++;
            botSpawnedItems.Add(new BotSpawnRecord
            {
                OwnerPeerId = bot.PeerId,
                NetId = identity.NetId,
                Instance = created,
                CreatedAt = now
            });
            FinishBotAction(bot, now, BotOutcome.Success);
        }

        private void UpdateBotGrab(BotAgent bot, float now)
        {
            if (bot.CurrentItem == null || bot.CurrentItem.Instance == null)
            {
                FinishBotAction(bot, now, BotOutcome.MissingTarget);
                return;
            }
            if (bot.GrabToken == 0)
            {
                if (!Reached(bot.Position, bot.InteractionPoint)) return;
                ActiveGrab grab;
                string denial;
                if (!grabs.TryBegin(bot.PeerId, bot.InteractionPoint, hostTick, out grab, out denial))
                {
                    FinishBotAction(bot, now, BotOutcome.Denied);
                    return;
                }
                bot.GrabNetId = grab.NetId;
                bot.GrabToken = grab.Token;
                bot.Target = bot.PlaceTarget;
                bot.ActionUntil = now + BotActionTimeout;
                return;
            }
            grabs.Update(bot.PeerId, bot.GrabNetId, bot.GrabToken, bot.PlaceTarget, hostTick);
            if (Reached(bot.Position, bot.PlaceTarget)) FinishBotAction(bot, now, BotOutcome.Success);
        }

        private void CleanupBotItem(BotAgent bot)
        {
            BotSpawnRecord record = bot.CurrentItem;
            if (record == null || record.Instance == null || grabs.IsActive(record.NetId)) { FinishBotAction(bot, Time.unscaledTime, BotOutcome.MissingTarget); return; }
            PPGTogetherIdentity identity = record.Instance.GetComponent<PPGTogetherIdentity>();
            if (identity == null || identity.NetId != record.NetId) { FinishBotAction(bot, Time.unscaledTime, BotOutcome.MissingTarget); return; }
            registry.Remove(record.Instance);
            identity.NetId = 0;
            Writer writer = new Writer(8); writer.ULong(record.NetId);
            Broadcast(WireMessage.Despawn, WireChannel.World, writer.ToArray(), true);
            Destroy(record.Instance);
            botSpawnedItems.Remove(record);
            FinishBotAction(bot, Time.unscaledTime, BotOutcome.Success);
        }

        private void ActivateBotItem(BotAgent bot, float now)
        {
            PPGTogetherIdentity identity = bot.CurrentItem == null || bot.CurrentItem.Instance == null ? null : bot.CurrentItem.Instance.GetComponent<PPGTogetherIdentity>();
            bool applied = identity != null && identity.NetId == bot.CurrentItem.NetId && HostActivate(identity);
            FinishBotAction(bot, now, applied ? BotOutcome.Success : BotOutcome.Denied);
        }

        private void FinishBotAction(BotAgent bot, float now, BotOutcome outcome)
        {
            if (bot.GrabToken != 0) grabs.End(bot.PeerId, bot.GrabNetId, bot.GrabToken);
            if (bot.Mind != null) bot.Mind.ReportOutcome(bot.PeerId, outcome, botCoordination, now);
            bot.GrabNetId = 0;
            bot.GrabToken = 0;
            bot.CurrentItem = null;
            bot.Decision = null;
            bot.Action = BotAction.Idle;
            bot.Target = bot.Position;
            bot.ActionUntil = now + 0.15f;
            bot.NextDecisionAt = bot.ActionUntil;
        }

        private void ReleaseBots()
        {
            for (int i = 0; i < bots.Count; i++)
            {
                grabs.ReleasePeer(bots[i].PeerId);
                if (bots[i].Mind != null) bots[i].Mind.Cancel(bots[i].PeerId, botCoordination);
            }
            botCoordination.Clear();
        }

        private void PruneBotSpawnedItems()
        {
            for (int i = botSpawnedItems.Count - 1; i >= 0; i--)
            {
                BotSpawnRecord record = botSpawnedItems[i];
                PPGTogetherIdentity identity = record.Instance != null ? record.Instance.GetComponent<PPGTogetherIdentity>() : null;
                if (identity == null || identity.NetId != record.NetId) botSpawnedItems.RemoveAt(i);
            }
        }

        private void RemoveBotSpawnRecord(GameObject instance)
        {
            if (instance == null) return;
            for (int i = botSpawnedItems.Count - 1; i >= 0; i--)
                if (botSpawnedItems[i].Instance == instance)
                    botSpawnedItems.RemoveAt(i);
        }

        private BotSpawnRecord FindBotCreatedItem(bool requireOld)
        {
            PruneBotSpawnedItems();
            if (botSpawnedItems.Count == 0) return null;
            float now = Time.unscaledTime;
            int start = UnityEngine.Random.Range(0, botSpawnedItems.Count);
            for (int offset = 0; offset < botSpawnedItems.Count; offset++)
            {
                BotSpawnRecord record = botSpawnedItems[(start + offset) % botSpawnedItems.Count];
                if (requireOld && now - record.CreatedAt < BotMinimumCleanupAge) continue;
                if (grabs.IsActive(record.NetId)) continue;
                PhysicalBehaviour physical = record.Instance != null ? record.Instance.GetComponent<PhysicalBehaviour>() : null;
                if (physical != null && physical.rigidbody != null && physical.Selectable) return record;
            }
            return null;
        }

        private BotSpawnRecord FindRegisteredBotItem(ulong netId)
        {
            if (netId == 0) return null;
            PPGTogetherIdentity identity;
            if (!registry.TryGet(netId, out identity) || identity == null || identity.gameObject == null) return null;
            return new BotSpawnRecord
            {
                OwnerPeerId = 0,
                NetId = netId,
                Instance = identity.gameObject,
                CreatedAt = Time.unscaledTime
            };
        }

        private static bool TryGetBotInteractionPoint(BotSpawnRecord record, out Vector2 point)
        {
            point = Vector2.zero;
            if (record == null || record.Instance == null) return false;
            PhysicalBehaviour physical = record.Instance.GetComponent<PhysicalBehaviour>();
            if (physical == null || physical.rigidbody == null) return false;
            Collider2D collider = physical.GetComponent<Collider2D>();
            if (collider != null)
            {
                Bounds bounds = collider.bounds;
                point = new Vector2(bounds.center.x, bounds.center.y);
            }
            else point = physical.rigidbody.position;
            return true;
        }

        private static bool Reached(Vector2 current, Vector2 target)
        {
            return (current - target).sqrMagnitude <= BotReachDistance * BotReachDistance;
        }

        private static Vector2 ToUnityPoint(BotPoint point) { return new Vector2(point.X, point.Y); }

        private static bool BotWorldReady()
        {
            return Global.main != null && CatalogBehaviour.Main != null && GetActiveCamera() != null;
        }

        private void LogBotCatalogWarning(string warning)
        {
            if (!string.IsNullOrEmpty(warning)) Logger.LogDebug("[Connect][Bots] " + warning);
        }

        private void PublishBotCursor(BotAgent bot)
        {
            RemoteCursor cursor;
            if (!cursors.TryGetValue(bot.PeerId, out cursor))
            {
                cursor = new RemoteCursor { PeerId = bot.PeerId, SteamId = bot.SteamId, Name = bot.Name, Color = BotColor(bot.Index), Render = bot.Position, IsBot = true };
                cursors.Add(bot.PeerId, cursor);
            }
            cursor.Target = bot.Position;
            cursor.Velocity = bot.Velocity;
            cursor.LastAt = Time.unscaledTime;
            bool acting = bot.Action == BotAction.Spawn || bot.Action == BotAction.GrabAndPlace || bot.Action == BotAction.Cleanup;
            cursor.Buttons = (byte)(BotCursorFlag | (acting ? 1 : 0));
            cursor.UiBusy = false;
            byte[] body = CursorPayloadCodec.Encode(bot.SteamId, bot.Position.x, bot.Position.y, bot.Velocity.x, bot.Velocity.y, cursor.Buttons, false);
            BroadcastFromPeer(WireMessage.Cursor, WireChannel.Cursor, bot.PeerId, body, false);
        }

        private void BroadcastBotMode(bool enabled, int count)
        {
            foreach (Peer peer in peers.Values) SendBotMode(peer.Connection, peer.PeerId, enabled, count);
        }

        private void SendBotMode(Connection connection, ushort peerId, bool enabled, int count)
        {
            Writer writer = new Writer(4); writer.Bool(enabled); writer.Byte((byte)Mathf.Clamp(count, 0, MaximumBots));
            SendToConnection(connection, WireMessage.BotMode, WireChannel.Control, peerId, writer.ToArray(), true);
        }

        private void HandleBotMode(Envelope envelope)
        {
            Reader reader = new Reader(envelope.Payload); bool enabled; byte count;
            if (!reader.Bool(out enabled) || !reader.Byte(out count) || reader.Remaining != 0 || count > MaximumBots) return;
            if (!enabled) ClearBotCursors();
            SetStatus(enabled ? "Host enabled Bot Mode (" + count + ")." : "Host disabled Bot Mode.");
        }

        private void HandleHostSettings(Envelope envelope)
        {
            Reader reader = new Reader(envelope.Payload);
            byte velocityIterations;
            byte positionIterations;
            byte snapshotRate;
            ushort maximumObjects;
            byte guestSpawnsPerMinute;
            bool guestsCanSpawn;
            bool guestsCanGrab;
            bool guestsCanActivate;
            bool guestsCanDelete;
            bool botsAllowed;
            byte botSpawnLimit;
            if (!reader.Byte(out velocityIterations) || !reader.Byte(out positionIterations) || !reader.Byte(out snapshotRate) ||
                !reader.UShort(out maximumObjects) || !reader.Byte(out guestSpawnsPerMinute) || !reader.Bool(out guestsCanSpawn) ||
                !reader.Bool(out guestsCanGrab) || !reader.Bool(out guestsCanActivate) || !reader.Bool(out guestsCanDelete) ||
                !reader.Bool(out botsAllowed) || !reader.Byte(out botSpawnLimit) || reader.Remaining != 0)
                return;
            if (velocityIterations < 1 || velocityIterations > 16 || positionIterations < 1 || positionIterations > 16 ||
                snapshotRate < 10 || snapshotRate > 30 || maximumObjects < 25 || maximumObjects > 1000 ||
                guestSpawnsPerMinute < 1 || guestSpawnsPerMinute > 60 || botSpawnLimit > 100)
                return;
            remoteHostSettings = new HostSettingsView
            {
                Received = true,
                VelocityIterations = velocityIterations,
                PositionIterations = positionIterations,
                SnapshotRate = snapshotRate,
                MaximumObjects = maximumObjects,
                GuestSpawnsPerMinute = guestSpawnsPerMinute,
                GuestsCanSpawn = guestsCanSpawn,
                GuestsCanGrab = guestsCanGrab,
                GuestsCanActivate = guestsCanActivate,
                GuestsCanDelete = guestsCanDelete,
                BotsAllowed = botsAllowed,
                BotSpawnLimit = botSpawnLimit
            };
        }

        private void HandleActionDenied(Envelope envelope)
        {
            Reader reader = new Reader(envelope.Payload);
            string reason;
            if (reader.String(out reason) && reader.Remaining == 0)
                SetStatus("Action denied: " + SafeName(reason));
        }

        private void SendHostSettings(Connection connection, ushort peerId)
        {
            if (!IsHost) return;
            Writer writer = new Writer(16);
            writer.Byte((byte)PhysicsVelocityIterations());
            writer.Byte((byte)PhysicsPositionIterations());
            writer.Byte((byte)SnapshotRateHz());
            writer.UShort((ushort)MaximumNetworkObjects());
            writer.Byte((byte)GuestSpawnLimitPerMinute());
            writer.Bool(hostGuestsCanSpawnSetting.Value);
            writer.Bool(hostGuestsCanGrabSetting.Value);
            writer.Bool(hostGuestsCanActivateSetting.Value);
            writer.Bool(hostGuestsCanDeleteSetting.Value);
            writer.Bool(hostBotsAllowedSetting.Value);
            writer.Byte((byte)BotSpawnLimit());
            SendToConnection(connection, WireMessage.HostSettings, WireChannel.Control, peerId, writer.ToArray(), true);
        }

        private void BroadcastHostSettings()
        {
            if (!IsHost) return;
            foreach (Peer peer in peers.Values) SendHostSettings(peer.Connection, peer.PeerId);
        }

        private void ClearBotCursors()
        {
            List<ushort> remove = new List<ushort>();
            foreach (KeyValuePair<ushort, RemoteCursor> pair in cursors)
                if (pair.Value != null && pair.Value.IsBot)
                    remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++) cursors.Remove(remove[i]);
        }

        private void HandleCursor(ReceivedPacket packet, Envelope envelope)
        {
            CursorPayload payload;
            if (!CursorPayloadCodec.TryDecode(envelope.Payload, out payload)) return;
            if (IsHost && payload.SteamId != packet.SteamId) return;
            ushort id = IsHost ? GetPeerId(payload.SteamId) : envelope.PeerId;
            // Peer 0 is the host. It is valid for a client to receive the
            // host's remote cursor, but never valid for the host to receive an
            // unregistered client cursor as peer 0.
            if (IsHost && id == 0) return;
            bool isBot = IsBotPeer(id) && (payload.Buttons & BotCursorFlag) != 0;
            RemoteCursor cursor;
            if (!cursors.TryGetValue(id, out cursor))
            {
                RemoveProvisionalCursorsForSteam(payload.SteamId);
                cursor = new RemoteCursor
                {
                    PeerId = id,
                    SteamId = payload.SteamId,
                    Name = isBot ? "Bot " + (id - BotPeerBase + 1) : SafeName(new Friend((SteamId)payload.SteamId).Name),
                    Color = isBot ? BotColor(id - BotPeerBase) : CursorColor(id),
                    Render = new Vector2(payload.X, payload.Y),
                    IsBot = isBot
                };
                cursors.Add(id, cursor);
            }
            else if (!CursorSequence.IsNewer(envelope.Sequence, cursor.LastSequence)) return;
            cursor.Target = new Vector2(payload.X, payload.Y);
            cursor.Velocity = Vector2.ClampMagnitude(new Vector2(payload.VelocityX, payload.VelocityY), 250f);
            cursor.LastAt = Time.unscaledTime;
            cursor.LastSequence = envelope.Sequence;
            cursor.Buttons = payload.Buttons;
            cursor.UiBusy = payload.UiBusy;
            cursor.IsBot = isBot;
            cursor.IsProvisional = false;
            if (IsHost) BroadcastFromPeer(WireMessage.Cursor, WireChannel.Cursor, id, envelope.Payload, false);
        }

        private void UpdateCursorInterpolation()
        {
            float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime * CursorSmoothing());
            foreach (RemoteCursor cursor in cursors.Values)
            {
                // A very short, bounded prediction removes the one-packet
                // render delay without allowing a stale cursor to drift.
                float predictionAge = Mathf.Clamp(Time.unscaledTime - cursor.LastAt, 0f, 0.050f);
                Vector2 desired = cursor.Target + cursor.Velocity * predictionAge;
                cursor.Render = Vector2.Lerp(cursor.Render, desired, t);
            }
        }

        private void UpdateInteractionInput()
        {
            if (menuVisible || Global.main == null || Global.main.UILock) return;
            Vector2 point = GetWorldCursor();
            if (IsHost)
            {
                if (!Input.GetKey(KeyCode.LeftAlt)) return;
                if (Input.GetMouseButtonDown(0))
                {
                    ActiveGrab local; string denied;
                    if (!grabs.TryBegin(0, point, hostTick, out local, out denied)) SetStatus(denied);
                }
                if (Input.GetMouseButtonUp(0)) grabs.ReleasePeer(0);
                return;
            }
            if (!sessionActive) return;
            if (Input.GetMouseButtonDown(0)) SendGrabBegin(point);
            if (clientGrabId != 0 && Input.GetMouseButton(0) && Time.unscaledTime >= nextGrabAt)
            {
                nextGrabAt = Time.unscaledTime + (1f / 30f);
                Writer writer = new Writer(32); writer.ULong(clientGrabId); writer.UInt(clientGrabToken); writer.Float(point.x); writer.Float(point.y);
                SendToHost(WireMessage.GrabUpdate, WireChannel.World, writer.ToArray(), false);
            }
            if (clientGrabId != 0 && Input.GetMouseButtonUp(0))
            {
                Writer writer = new Writer(16); writer.ULong(clientGrabId); writer.UInt(clientGrabToken);
                SendToHost(WireMessage.GrabEnd, WireChannel.World, writer.ToArray(), true);
                clientGrabId = 0; clientGrabToken = 0;
            }
        }

        private void SendGrabBegin(Vector2 point)
        {
            Writer writer = new Writer(12); writer.Float(point.x); writer.Float(point.y);
            SendToHost(WireMessage.GrabBegin, WireChannel.World, writer.ToArray(), true);
        }

        private void HandleGrabBegin(ReceivedPacket packet, Envelope envelope)
        {
            Peer peer; if (!peers.TryGetValue(packet.SteamId, out peer)) return;
            if (!hostGuestsCanGrabSetting.Value) { SendGrabDenied(packet.Connection, "Guests cannot grab objects in this session."); return; }
            Reader reader = new Reader(envelope.Payload); float x; float y;
            if (!reader.Float(out x) || !reader.Float(out y) || reader.Remaining != 0) return;
            ActiveGrab grab; string denied;
            if (!grabs.TryBegin(peer.PeerId, new Vector2(x, y), hostTick, out grab, out denied)) { SendGrabDenied(packet.Connection, denied); return; }
            Writer response = new Writer(16); response.ULong(grab.NetId); response.UInt(grab.Token);
            SendToConnection(packet.Connection, WireMessage.GrabGranted, WireChannel.World, peer.PeerId, response.ToArray(), true);
        }

        private void HandleGrabGranted(Envelope envelope)
        {
            Reader reader = new Reader(envelope.Payload);
            if (!reader.ULong(out clientGrabId) || !reader.UInt(out clientGrabToken) || reader.Remaining != 0) { clientGrabId = 0; clientGrabToken = 0; }
        }

        private void HandleGrabDenied(Envelope envelope)
        {
            Reader reader = new Reader(envelope.Payload); string reason;
            if (reader.String(out reason)) SetStatus(reason);
        }

        private void HandleGrabUpdate(ReceivedPacket packet, Envelope envelope)
        {
            Peer peer; if (!peers.TryGetValue(packet.SteamId, out peer)) return;
            Reader reader = new Reader(envelope.Payload); ulong id; uint token; float x; float y;
            if (reader.ULong(out id) && reader.UInt(out token) && reader.Float(out x) && reader.Float(out y) && reader.Remaining == 0)
                grabs.Update(peer.PeerId, id, token, new Vector2(x, y), hostTick);
        }

        private void HandleGrabEnd(ReceivedPacket packet, Envelope envelope)
        {
            Peer peer; if (!peers.TryGetValue(packet.SteamId, out peer)) return;
            Reader reader = new Reader(envelope.Payload); ulong id; uint token;
            if (reader.ULong(out id) && reader.UInt(out token) && reader.Remaining == 0) grabs.End(peer.PeerId, id, token);
        }

        private void BroadcastSnapshots()
        {
            foreach (PPGTogetherIdentity identity in registry.All())
            {
                if (identity == null) continue;
                PhysicalBehaviour physical = identity.GetComponent<PhysicalBehaviour>();
                if (physical == null || physical.rigidbody == null) continue;
                Rigidbody2D body = physical.rigidbody;
                Writer writer = new Writer(48);
                writer.ULong(identity.NetId); writer.Float(body.position.x); writer.Float(body.position.y); writer.Float(body.rotation);
                writer.Float(body.velocity.x); writer.Float(body.velocity.y); writer.Float(body.angularVelocity); writer.Bool(body.simulated); writer.Bool(!body.IsAwake());
                Broadcast(WireMessage.Snapshot, WireChannel.Snapshot, writer.ToArray(), false);
            }
        }

        private void HandleSnapshot(Envelope envelope)
        {
            Reader reader = new Reader(envelope.Payload); ulong id; float x; float y; float rotation; float vx; float vy; float angular; bool simulated; bool sleeping;
            if (!reader.ULong(out id) || !reader.Float(out x) || !reader.Float(out y) || !reader.Float(out rotation) || !reader.Float(out vx) || !reader.Float(out vy) || !reader.Float(out angular) || !reader.Bool(out simulated) || !reader.Bool(out sleeping) || reader.Remaining != 0) return;
            PPGTogetherIdentity identity; if (!registry.TryGet(id, out identity)) return;
            PhysicalBehaviour physical = identity.GetComponent<PhysicalBehaviour>(); if (physical == null || physical.rigidbody == null) return;
            Rigidbody2D body = physical.rigidbody; Vector2 target = new Vector2(x, y);
            if (Vector2.Distance(body.position, target) > 2f) body.position = target;
            else body.velocity = Vector2.Lerp(body.velocity, new Vector2(vx, vy), 0.35f);
            body.rotation = Mathf.LerpAngle(body.rotation, rotation, 0.4f); body.angularVelocity = Mathf.Lerp(body.angularVelocity, angular, 0.35f); body.simulated = simulated;
            if (sleeping) body.Sleep(); else body.WakeUp();
        }

        private void OnItemSpawned(object sender, UserSpawnEventArgs args)
        {
            if (!IsHost || !sessionActive || args == null || args.Instance == null || args.SpawnableAsset == null) return;
            PPGTogetherIdentity known;
            if (registry.TryGet(args.Instance, out known)) return;
            string key = args.SpawnableAsset.NameToOrderBy;
            PPGTogetherIdentity identity = registry.RegisterHost(args.Instance, key);
            if (identity != null)
            {
                Logger.LogInfo("[Connect][Spawn] Host catalog spawn observed: key=" + SafeName(key) + ", netId=" + identity.NetId + ". Broadcasting to " + peers.Count + " guest(s).");
                BroadcastSpawn(identity, args.Instance);
            }
            else Logger.LogWarning("[Connect][Spawn] Host catalog spawn could not be registered: " + SafeName(key) + ".");
        }

        private void OnItemRemoved(object sender, UserSpawnEventArgs args)
        {
            if (args == null || args.Instance == null) return;
            RemoveBotSpawnRecord(args.Instance);
            PPGTogetherIdentity identity = args.Instance.GetComponent<PPGTogetherIdentity>();
            if (IsHost && sessionActive && identity != null && identity.NetId != 0)
            {
                Writer writer = new Writer(8); writer.ULong(identity.NetId); Broadcast(WireMessage.Despawn, WireChannel.World, writer.ToArray(), true);
            }
            registry.Remove(args.Instance);
        }

        private void BroadcastSpawn(PPGTogetherIdentity identity, GameObject instance)
        {
            if (string.IsNullOrEmpty(identity.SpawnKey)) return;
            byte[] payload = BuildSpawnPayload(identity, instance);
            if (payload == null) return;
            Broadcast(WireMessage.Spawn, WireChannel.World, payload, true);
            Logger.LogInfo("[Connect][Spawn] Broadcast Spawn: key=" + SafeName(identity.SpawnKey) + ", netId=" + identity.NetId + ", guests=" + peers.Count + ".");
        }

        private void SendSpawnToConnection(Connection connection, ushort peerId, PPGTogetherIdentity identity, GameObject instance)
        {
            byte[] payload = BuildSpawnPayload(identity, instance);
            if (payload != null) SendToConnection(connection, WireMessage.Spawn, WireChannel.World, peerId, payload, true);
        }

        private static byte[] BuildSpawnPayload(PPGTogetherIdentity identity, GameObject instance)
        {
            if (identity == null || instance == null || string.IsNullOrEmpty(identity.SpawnKey)) return null;
            Transform transform = instance.transform;
            if (transform == null) return null;
            Writer writer = new Writer(96);
            writer.ULong(identity.NetId); writer.String(identity.SpawnKey); writer.Float(transform.position.x); writer.Float(transform.position.y); writer.Float(transform.eulerAngles.z);
            writer.Float(transform.localScale.x); writer.Float(transform.localScale.y); writer.Float(transform.localScale.z);
            return writer.ToArray();
        }

        private void HandleSpawnRequest(ReceivedPacket packet, Envelope envelope)
        {
            Peer peer; if (!peers.TryGetValue(packet.SteamId, out peer)) { Logger.LogWarning("[Connect][Spawn] Rejected SpawnRequest from unknown Steam identity " + packet.SteamId + "."); return; }
            if (envelope.PeerId != peer.PeerId) { Logger.LogWarning("[Connect][Spawn] Rejected SpawnRequest with mismatched peer id from " + peer.Name + "."); return; }
            if (!sessionActive) { Logger.LogWarning("[Connect][Spawn] Rejected SpawnRequest from " + peer.Name + ": host session is not active."); SendActionDenied(packet.Connection, "The host map is still loading."); return; }
            if (!hostGuestsCanSpawnSetting.Value) { Logger.LogInfo("[Connect][Spawn] Denied SpawnRequest from " + peer.Name + ": host disabled guest spawns."); SendActionDenied(packet.Connection, "Guests cannot spawn items in this session."); return; }
            Reader reader = new Reader(envelope.Payload); string key; float x; float y; bool flipped;
            if (!reader.String(out key) || !reader.Float(out x) || !reader.Float(out y) || !reader.Bool(out flipped) || reader.Remaining != 0 || !Finite(x) || !Finite(y)) { Logger.LogWarning("[Connect][Spawn] Rejected malformed SpawnRequest from " + peer.Name + "."); return; }
            Logger.LogInfo("[Connect][Spawn] Received SpawnRequest from " + peer.Name + ": key=" + SafeName(key) + ", x=" + x + ", y=" + y + ", flipped=" + flipped + ".");
            SpawnableAsset asset = ModAPI.FindSpawnable(key);
            if (asset == null || asset.IsLocked) { Logger.LogInfo("[Connect][Spawn] Denied SpawnRequest from " + peer.Name + ": unavailable or locked " + SafeName(key) + "."); SendActionDenied(packet.Connection, "Spawnable is unavailable: " + SafeName(key)); return; }
            if (!TryConsumeGuestSpawn(packet.SteamId)) { Logger.LogInfo("[Connect][Spawn] Denied SpawnRequest from " + peer.Name + ": rate limit."); SendActionDenied(packet.Connection, "Guest spawn limit reached. Try again shortly."); return; }
            if (SpawnAndReplicate(asset, new Vector2(x, y), flipped) == null)
            {
                Logger.LogWarning("[Connect][Spawn] Host could not create requested item " + SafeName(key) + " for " + peer.Name + ".");
                SendActionDenied(packet.Connection, "Server object limit reached or host could not create: " + SafeName(key)); return;
            }
            SetStatus(peer.Name + " spawned " + SafeName(key) + " from the catalog.");
        }

        private void HandleSpawn(Envelope envelope)
        {
            Reader reader = new Reader(envelope.Payload); ulong id; string key; float x; float y; float rotation; float sx; float sy; float sz;
            if (!reader.ULong(out id) || !reader.String(out key) || !reader.Float(out x) || !reader.Float(out y) || !reader.Float(out rotation) || !reader.Float(out sx) || !reader.Float(out sy) || !reader.Float(out sz) || reader.Remaining != 0) return;
            PPGTogetherIdentity existing;
            if (registry.TryGet(id, out existing)) { Logger.LogInfo("[Connect][Spawn] Ignored duplicate Spawn netId=" + id + "."); return; }
            SpawnableAsset asset = ModAPI.FindSpawnable(key);
            if (asset == null) { Logger.LogWarning("[Connect][Spawn] Guest is missing Spawn key=" + SafeName(key) + ", netId=" + id + "."); SetStatus("Missing spawnable: " + SafeName(key)); return; }
            GameObject instance = SpawnAsset(asset, new Vector2(x, y), rotation);
            if (instance == null) { Logger.LogWarning("[Connect][Spawn] Guest failed to instantiate Spawn key=" + SafeName(key) + ", netId=" + id + "."); SetStatus("Failed to create: " + SafeName(key)); return; }
            instance.transform.localScale = new Vector3(sx, sy, sz);
            if (registry.RegisterReplica(instance, id, key) == null) { Logger.LogWarning("[Connect][Spawn] Guest could not register Spawn key=" + SafeName(key) + ", netId=" + id + "."); return; }
            Logger.LogInfo("[Connect][Spawn] Guest created authoritative Spawn: key=" + SafeName(key) + ", netId=" + id + ".");
        }

        private void HandleDespawn(Envelope envelope)
        {
            Reader reader = new Reader(envelope.Payload); ulong id;
            if (!reader.ULong(out id) || reader.Remaining != 0) return;
            PPGTogetherIdentity identity;
            if (registry.TryGet(id, out identity) && identity != null)
            {
                registry.Remove(identity.gameObject);
                Destroy(identity.gameObject);
            }
        }

        private GameObject SpawnAsset(SpawnableAsset asset, Vector2 position)
        {
            return SpawnAsset(asset, position, 0f);
        }

        private GameObject SpawnAndReplicate(SpawnableAsset asset, Vector2 position)
        {
            return SpawnAndReplicate(asset, position, false);
        }

        private GameObject SpawnAndReplicate(SpawnableAsset asset, Vector2 position, bool flipped)
        {
            if (registry.Count >= MaximumNetworkObjects())
                return null;
            GameObject created = SpawnAsset(asset, position, 0f, flipped);
            if (created == null) { Logger.LogWarning("[Connect][Spawn] SpawnAsset returned null for " + SafeName(asset == null ? string.Empty : asset.NameToOrderBy) + "."); return null; }
            PPGTogetherIdentity existing;
            if (!registry.TryGet(created, out existing))
            {
                existing = registry.RegisterHost(created, asset.NameToOrderBy);
                if (existing != null) BroadcastSpawn(existing, created);
                else Logger.LogWarning("[Connect][Spawn] Created item could not receive a network identity: " + SafeName(asset.NameToOrderBy) + ".");
            }
            return created;
        }

        private GameObject SpawnAsset(SpawnableAsset asset, Vector2 position, float rotation)
        {
            return SpawnAsset(asset, position, rotation, false);
        }

        private GameObject SpawnAsset(SpawnableAsset asset, Vector2 position, float rotation, bool flipped)
        {
            if (asset == null || asset.Prefab == null) return null;
            CatalogBehaviour catalog = CatalogBehaviour.Main;
            if (catalog != null)
            {
                bool requestedFlip = flipped;
                GameObject created = catalog.PerformInstantiation(asset, position, ref requestedFlip, rotation);
                if (created != null)
                {
                    CatalogBehaviour.PerformBeforeSpawn(asset, created);
                    if (flipped)
                    {
                        Vector3 scale = created.transform.localScale;
                        scale.x *= -1f;
                        created.transform.localScale = scale;
                    }
                    CatalogBehaviour.PerformAfterSpawn(asset, created);
                    return created;
                }
            }
            GameObject fallback = Instantiate(asset.Prefab);
            fallback.transform.position = position;
            fallback.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
            if (flipped)
            {
                Vector3 scale = fallback.transform.localScale;
                scale.x *= -1f;
                fallback.transform.localScale = scale;
            }
            return fallback;
        }

        internal void RequestCatalogSpawn(SpawnableAsset asset, bool flipped)
        {
            if (asset == null || string.IsNullOrEmpty(asset.NameToOrderBy)) { Logger.LogWarning("[Connect][Spawn] Tab catalog interception had no usable SpawnableAsset."); SetStatus("The selected catalog item is unavailable."); return; }
            if (!CanClientSendWorldRequest()) { Logger.LogWarning("[Connect][Spawn] Tab spawn for " + SafeName(asset.NameToOrderBy) + " was intercepted, but the guest relay session is not ready. session=" + sessionActive + ", peer=" + clientPeerId + ", connected=" + (transport != null && transport.Connected) + "."); SetStatus("Waiting for the Steam Relay handshake before catalog spawn."); return; }
            Vector2 point = GetWorldCursor();
            Writer writer = new Writer(80); writer.String(asset.NameToOrderBy); writer.Float(point.x); writer.Float(point.y); writer.Bool(flipped);
            SendToHost(WireMessage.SpawnRequest, WireChannel.World, writer.ToArray(), true);
            Logger.LogInfo("[Connect][Spawn] Intercepted Tab spawn and sent SpawnRequest: key=" + SafeName(asset.NameToOrderBy) + ", x=" + point.x + ", y=" + point.y + ", flipped=" + flipped + ".");
            SetStatus("Requested " + SafeName(asset.NameToOrderBy) + " from the Tab catalog. Waiting for host authority.");
        }

        // These helpers are called only from the narrow client-side Harmony
        // patches.  Selection remains local UI state; the host receives only a
        // registered root NetId and repeats its own validation before acting.
        internal void PrepareClientContextMenu()
        {
            if (!ShouldBlockVanillaWorldInput || !InputSystem.Down("context")) return;
            SelectionController selection = SelectionController.Main;
            if (selection == null) return;
            PhysicalBehaviour hovered = selection.CurrentlyUnderMouse;
            if (hovered == null || GetIdentity(hovered) == null) return;
            selection.Select(hovered, false);
        }

        internal void RequestClientContextActivate()
        {
            RequestClientContextAction(NetworkInteraction.Activate);
        }

        internal void RequestClientContextDelete()
        {
            RequestClientContextAction(NetworkInteraction.Delete);
        }

        internal bool HandleClientDirectActivation()
        {
            if (!ShouldBlockVanillaWorldInput) return false;
            if (!InputSystem.Down("activateDirect")) return true;
            SelectionController selection = SelectionController.Main;
            if (selection != null && selection.SelectedObjects.Count > 0)
                return true;
            PhysicalBehaviour hovered = selection == null ? null : selection.CurrentlyUnderMouse;
            PPGTogetherIdentity identity = GetIdentity(hovered);
            if (identity == null)
            {
                SetStatus("No Connect object is under the direct-use cursor.");
                return true;
            }
            BeginClientActivation(identity.NetId);
            return true;
        }

        // The game routes the user-configurable activateDirect action through
        // DragTool while an item is selected. HandleTools is blocked on clients
        // to protect host authority, so preserve this semantic action here.
        // A right-click gives every client an independent local selection for
        // their own context menu; no other player's menu state is shared.
        internal void HandleClientBlockedToolInput()
        {
            if (!ShouldBlockVanillaWorldInput) return;
            UpdateClientContinuousActivation();
            if (menuVisible || Global.main == null || Global.main.UILock) return;
            if (InputSystem.Down("activateDirect"))
                BeginClientSelectedActivation();
            if (InputSystem.Down("delete"))
                RequestClientSelectedAction(NetworkInteraction.Delete, "Select a Connect object before using Delete.");
        }

        private void RequestClientContextAction(NetworkInteraction action)
        {
            RequestClientSelectedAction(action, "This context-menu target is not a Connect object.");
        }

        private void RequestClientSelectedAction(NetworkInteraction action, string emptyMessage)
        {
            if (!CanClientSendWorldRequest()) { SetStatus("Waiting for the Steam Relay handshake before interaction."); return; }
            SelectionController selection = SelectionController.Main;
            if (selection == null) return;
            bool sent = false;
            foreach (PhysicalBehaviour physical in selection.SelectedObjects)
            {
                PPGTogetherIdentity identity = GetIdentity(physical);
                if (identity == null) continue;
                SendInteractionRequest(action, identity.NetId);
                sent = true;
            }
            if (!sent) SetStatus(emptyMessage);
        }

        private void BeginClientSelectedActivation()
        {
            if (!CanClientSendWorldRequest()) { SetStatus("Waiting for the Steam Relay handshake before interaction."); return; }
            SelectionController selection = SelectionController.Main;
            if (selection == null) return;
            bool sent = false;
            foreach (PhysicalBehaviour physical in selection.SelectedObjects)
            {
                PPGTogetherIdentity identity = GetIdentity(physical);
                if (identity == null) continue;
                BeginClientActivation(identity.NetId);
                sent = true;
            }
            if (!sent) SetStatus("Select a Connect object, then use your Activate key.");
        }

        private void BeginClientActivation(ulong netId)
        {
            if (netId == 0 || !CanClientSendWorldRequest()) return;
            if (clientHeldActivationRoots.Add(netId))
                SendInteractionRequest(NetworkInteraction.ActivateBegin, netId);
        }

        private void UpdateClientContinuousActivation()
        {
            if (clientHeldActivationRoots.Count == 0 || IsHost) return;
            if (!InputSystem.Held("activateDirect"))
            {
                EndClientActivations();
                return;
            }
            if (Time.unscaledTime < nextClientActivationHeartbeatAt) return;
            nextClientActivationHeartbeatAt = Time.unscaledTime + 0.20f;
            List<ulong> stale = null;
            foreach (ulong netId in clientHeldActivationRoots)
            {
                PPGTogetherIdentity identity;
                if (!registry.TryGet(netId, out identity) || identity == null)
                {
                    if (stale == null) stale = new List<ulong>();
                    stale.Add(netId);
                    continue;
                }
                SendInteractionRequest(NetworkInteraction.ActivateKeepAlive, netId);
            }
            if (stale == null) return;
            for (int i = 0; i < stale.Count; i++) clientHeldActivationRoots.Remove(stale[i]);
        }

        private void EndClientActivations()
        {
            if (clientHeldActivationRoots.Count == 0) return;
            foreach (ulong netId in clientHeldActivationRoots)
                SendInteractionRequest(NetworkInteraction.ActivateEnd, netId);
            clientHeldActivationRoots.Clear();
        }

        private void SendInteractionRequest(NetworkInteraction action, ulong netId)
        {
            if (netId == 0) return;
            Writer writer = new Writer(12);
            writer.Byte((byte)action);
            writer.ULong(netId);
            SendToHost(WireMessage.InteractionRequest, WireChannel.World, writer.ToArray(), true);
        }

        private void HandleInteractionRequest(ReceivedPacket packet, Envelope envelope)
        {
            Peer peer;
            if (!peers.TryGetValue(packet.SteamId, out peer)) return;
            Reader reader = new Reader(envelope.Payload);
            byte rawAction;
            ulong netId;
            if (!reader.Byte(out rawAction) || !reader.ULong(out netId) || reader.Remaining != 0 ||
                !Enum.IsDefined(typeof(NetworkInteraction), rawAction) || netId == 0)
                return;
            NetworkInteraction action = (NetworkInteraction)rawAction;
            bool activationAction = action == NetworkInteraction.Activate || action == NetworkInteraction.ActivateBegin ||
                                    action == NetworkInteraction.ActivateKeepAlive || action == NetworkInteraction.ActivateEnd;
            if (activationAction && !hostGuestsCanActivateSetting.Value)
            {
                SendActionDenied(packet.Connection, "Guests cannot activate items in this session.");
                return;
            }
            if (action == NetworkInteraction.Delete && !hostGuestsCanDeleteSetting.Value)
            {
                SendActionDenied(packet.Connection, "Guests cannot delete items in this session.");
                return;
            }
            if (action == NetworkInteraction.ActivateEnd)
            {
                continuousActivations.End(peer.PeerId, netId);
                return;
            }
            if (action != NetworkInteraction.ActivateKeepAlive && !TryConsumeGuestInteraction(packet.SteamId))
            {
                SendActionDenied(packet.Connection, "Guest interaction limit reached. Try again shortly.");
                return;
            }
            PPGTogetherIdentity identity;
            if (!registry.TryGet(netId, out identity) || identity == null || !IsPeerNearObject(peer, identity))
            {
                SendActionDenied(packet.Connection, "The requested object is no longer under your network cursor.");
                return;
            }
            if (action == NetworkInteraction.ActivateKeepAlive)
            {
                if (!continuousActivations.Renew(peer.PeerId, netId, hostTick))
                    SendActionDenied(packet.Connection, "The continuous-use lease expired or belongs to another player.");
                return;
            }
            if (action == NetworkInteraction.ActivateBegin)
            {
                string denial;
                if (!continuousActivations.TryBegin(peer.PeerId, netId, hostTick, out denial))
                {
                    SendActionDenied(packet.Connection, denial);
                    return;
                }
                if (!HostActivate(identity))
                {
                    continuousActivations.End(peer.PeerId, netId);
                    SendActionDenied(packet.Connection, "This object does not support vanilla activation.");
                }
                return;
            }
            bool applied = action == NetworkInteraction.Activate ? HostActivate(identity) : HostDelete(identity);
            if (!applied) SendActionDenied(packet.Connection, action == NetworkInteraction.Activate ? "This object does not support vanilla activation." : "This object cannot be deleted.");
        }

        private bool HostActivate(PPGTogetherIdentity identity)
        {
            PhysicalBehaviour physical = identity == null ? null : identity.GetComponent<PhysicalBehaviour>();
            if (physical == null) return false;
            ActivationPropagation activation = new ActivationPropagation(true, 0, physical.gameObject);
            activation.Target = physical.gameObject;
            Utils.Activations.SendOnce(activation, physical);
            physical.BroadcastMessage("Use", activation, SendMessageOptions.DontRequireReceiver);
            return true;
        }

        private void HostContinuousActivate(ulong netId)
        {
            PPGTogetherIdentity identity;
            if (!registry.TryGet(netId, out identity) || identity == null)
            {
                return;
            }
            PhysicalBehaviour physical = identity.GetComponent<PhysicalBehaviour>();
            if (physical == null)
            {
                return;
            }
            ActivationPropagation activation = new ActivationPropagation(true, 0, physical.gameObject);
            activation.Target = physical.gameObject;
            Utils.Activations.SendContinuous(activation, physical);
        }

        private bool HostDelete(PPGTogetherIdentity identity)
        {
            PhysicalBehaviour physical = identity == null ? null : identity.GetComponent<PhysicalBehaviour>();
            if (physical == null || !physical.Deletable || grabs.IsActive(identity.NetId) || continuousActivations.IsActive(identity.NetId)) return false;
            ulong netId = identity.NetId;
            GameObject target = identity.gameObject;
            registry.Remove(target);
            identity.NetId = 0;
            Writer writer = new Writer(8); writer.ULong(netId);
            Broadcast(WireMessage.Despawn, WireChannel.World, writer.ToArray(), true);
            target.SendMessage("OnUserDelete", SendMessageOptions.DontRequireReceiver);
            Destroy(target);
            return true;
        }

        private bool IsPeerNearObject(Peer peer, PPGTogetherIdentity identity)
        {
            if (peer == null || identity == null) return false;
            RemoteCursor cursor;
            if (!cursors.TryGetValue(peer.PeerId, out cursor) || cursor == null) return false;
            PhysicalBehaviour physical = identity.GetComponent<PhysicalBehaviour>();
            if (physical == null) return false;
            Vector2 position = physical.transform.position;
            return (position - cursor.Target).sqrMagnitude <= 36f;
        }

        private static PPGTogetherIdentity GetIdentity(PhysicalBehaviour physical)
        {
            return physical == null ? null : physical.GetComponentInParent<PPGTogetherIdentity>();
        }

        private bool CanClientSendWorldRequest()
        {
            return !IsHost && sessionActive && lobby.HasValue && clientPeerId != 0 && transport != null && transport.Connected;
        }

        // A guest never chooses a divergent local map.  Connect's own
        // scene-switch call is explicitly marked and remains allowed.
        internal bool AllowClientMapViewSelection()
        {
            if (!lobby.HasValue || IsHost) return true;
            SetStatus("The session host controls map selection. Waiting for the host map.");
            return false;
        }

        internal bool AllowClientSceneSwitch()
        {
            if (!lobby.HasValue || IsHost || clientConnectSceneSwitchCall) return true;
            SetStatus("The session host controls map selection. Waiting for the host map.");
            return false;
        }

        private void StartSession()
        {
            if (!IsHost || !lobby.HasValue) { SetStatus("Only the lobby host can start the session."); return; }
            ApplyHostPhysicsSettings();
            botsEnabled = false;
            botSpawnCount = 0;
            bots.Clear();
            botSpawnedItems.Clear();
            ClearBotCursors();
            string mapIdentity;
            if (TryGetCurrentMapIdentity(out mapIdentity) && IsMapActuallyLoaded(mapIdentity))
            {
                BeginHostSession(mapIdentity);
                return;
            }
            hostStartAwaitingMap = true;
            sessionActive = false;
            lobby.Value.SetData("ppgt_state", "loading");
            lobby.Value.SetData("ppgt_map_id", string.Empty);
            SetStatus("Choose a People Playground map. Every connected player will load it automatically.");
        }

        private void InviteFriends()
        {
            if (!lobby.HasValue) { SetStatus("Create or join a lobby first."); return; }
            if (!SteamUtils.IsOverlayEnabled) { SetStatus("Steam Overlay is disabled. Launch through Steam and enable the overlay for People Playground."); return; }
            SteamFriends.OpenGameInviteOverlay(lobby.Value.Id);
        }

        private void LeaveLobby()
        {
            if (IsHost && sessionActive) Broadcast(WireMessage.SessionEnding, WireChannel.Control, new byte[0], true);
            Cleanup(true);
            SetStatus("Left lobby.");
        }

        private void Cleanup(bool leaveSteamLobby)
        {
            RestoreHostPhysicsSettings();
            sessionActive = false; clientPeerId = 0; clientGrabId = 0; clientGrabToken = 0;
            hostStartAwaitingMap = false; clientSessionStartReceived = false; clientMapLoadPending = false; clientMapLoadIssued = false; clientMapInstanceLoaded = false; clientMapSceneTransitionPending = false; clientConnectSceneSwitchCall = false;
            clientMapLoadDeadline = 0f; clientMapReadyAt = 0f; nextClientMapProbeAt = 0f; nextClientMapLoadAttemptAt = 0f; clientRequestedMapIdentity = string.Empty; clientRequestedSceneName = string.Empty; activeMapIdentity = string.Empty;
            botsEnabled = false; botSpawnCount = 0; ReleaseBots(); bots.Clear(); botSpawnedItems.Clear(); botWorld.Clear(); botCatalog.Clear();
            grabs.Clear(); continuousActivations.Clear(); clientHeldActivationRoots.Clear(); registry.Clear(); peers.Clear(); cursors.Clear(); avatars.Clear(); guestSpawnWindows.Clear(); guestInteractionWindows.Clear(); remoteHostSettings = new HostSettingsView();
            if (transport != null) transport.Close();
            if (leaveSteamLobby && lobby.HasValue) lobby.Value.Leave();
            lobby = null; nonce = 0;
            if (Instance == this && !leaveSteamLobby)
            {
                UnsubscribeSteam();
                ModAPI.OnItemSpawned -= OnItemSpawned;
                ModAPI.OnItemRemoved -= OnItemRemoved;
                Instance = null;
            }
        }

        private void WriteLobbyMetadata()
        {
            if (!lobby.HasValue) return;
            Lobby value = lobby.Value;
            value.SetData("ppgt_protocol", Wire.ProtocolVersion.ToString());
            value.SetData("ppgt_mod_version", PluginVersion);
            value.SetData("ppgt_game_version", ExpectedGameVersion);
            value.SetData("ppgt_host_steam_id", ((ulong)SteamClient.SteamId).ToString());
            value.SetData("ppgt_session_nonce", nonce.ToString());
            value.SetData("ppgt_state", "lobby");
            value.SetData("ppgt_map_id", string.Empty);
            value.SetData("ppgt_max_players", maxPlayers.ToString());
            value.SetData("ppgt_snapshot_hz", SnapshotRateHz().ToString());
            value.SetData("ppgt_physics_velocity_iterations", PhysicsVelocityIterations().ToString());
            value.SetData("ppgt_physics_position_iterations", PhysicsPositionIterations().ToString());
            value.SetData("ppgt_max_network_objects", MaximumNetworkObjects().ToString());
            value.SetJoinable(true);
        }

        private void ApplyLobbyPrivacy(Lobby value)
        {
            value.MaxMembers = maxPlayers;
            if (privacy == LobbyPrivacy.Private) value.SetPrivate();
            else if (privacy == LobbyPrivacy.Public) value.SetPublic();
            else value.SetFriendsOnly();
        }

        private void HostSettingsChanged()
        {
            hostVelocityIterationsSetting.Value = PhysicsVelocityIterations();
            hostPositionIterationsSetting.Value = PhysicsPositionIterations();
            hostSnapshotRateSetting.Value = SnapshotRateHz();
            hostMaxNetworkObjectsSetting.Value = MaximumNetworkObjects();
            hostGuestSpawnLimitSetting.Value = GuestSpawnLimitPerMinute();
            hostGuestInteractionLimitSetting.Value = GuestInteractionLimitPerMinute();
            hostBotSpawnLimitSetting.Value = BotSpawnLimit();
            SaveSettings();
            if (!IsHost) return;
            ApplyHostPhysicsSettings();
            if (lobby.HasValue) WriteLobbyMetadata();
            if (!hostBotsAllowedSetting.Value && botsEnabled)
            {
                botsEnabled = false;
                bots.Clear();
                ClearBotCursors();
                BroadcastBotMode(false, 0);
                SetStatus("Bot Mode disabled by Host Settings.");
            }
            BroadcastHostSettings();
        }

        private void ApplyHostPhysicsSettings()
        {
            if (!IsHost) return;
            if (!hostPhysicsApplied)
            {
                originalVelocityIterations = Physics2D.velocityIterations;
                originalPositionIterations = Physics2D.positionIterations;
                hostPhysicsApplied = true;
            }
            Physics2D.velocityIterations = PhysicsVelocityIterations();
            Physics2D.positionIterations = PhysicsPositionIterations();
            Logger.LogInfo("[Connect][Host] Physics2D iterations applied: velocity " + Physics2D.velocityIterations + ", position " + Physics2D.positionIterations + ".");
        }

        private void RestoreHostPhysicsSettings()
        {
            if (!hostPhysicsApplied) return;
            Physics2D.velocityIterations = originalVelocityIterations;
            Physics2D.positionIterations = originalPositionIterations;
            hostPhysicsApplied = false;
            Logger.LogInfo("[Connect][Host] Restored local Physics2D iterations after host cleanup.");
        }

        private bool TryConsumeGuestSpawn(ulong steamId)
        {
            float now = Time.unscaledTime;
            SpawnRateWindow window;
            if (!guestSpawnWindows.TryGetValue(steamId, out window) || now - window.StartTime >= 60f)
            {
                window = new SpawnRateWindow { StartTime = now, Count = 0 };
                guestSpawnWindows[steamId] = window;
            }
            if (window.Count >= GuestSpawnLimitPerMinute()) return false;
            window.Count++;
            guestSpawnWindows[steamId] = window;
            return true;
        }

        private bool TryConsumeGuestInteraction(ulong steamId)
        {
            float now = Time.unscaledTime;
            SpawnRateWindow window;
            if (!guestInteractionWindows.TryGetValue(steamId, out window) || now - window.StartTime >= 60f)
            {
                window = new SpawnRateWindow { StartTime = now, Count = 0 };
                guestInteractionWindows[steamId] = window;
            }
            if (window.Count >= GuestInteractionLimitPerMinute()) return false;
            window.Count++;
            guestInteractionWindows[steamId] = window;
            return true;
        }

        private int PhysicsVelocityIterations() { return Mathf.Clamp(hostVelocityIterationsSetting.Value, 1, 16); }
        private int PhysicsPositionIterations() { return Mathf.Clamp(hostPositionIterationsSetting.Value, 1, 16); }
        private int SnapshotRateHz() { return Mathf.Clamp(hostSnapshotRateSetting.Value, 10, 30); }
        private int MaximumNetworkObjects() { return Mathf.Clamp(hostMaxNetworkObjectsSetting.Value, 25, 1000); }
        private int GuestSpawnLimitPerMinute() { return Mathf.Clamp(hostGuestSpawnLimitSetting.Value, 1, 60); }
        private int GuestInteractionLimitPerMinute() { return Mathf.Clamp(hostGuestInteractionLimitSetting.Value, 5, 120); }
        private int BotSpawnLimit() { return Mathf.Clamp(hostBotSpawnLimitSetting.Value, 0, 100); }
        private int CursorSendRateHz() { return Mathf.Clamp(playerCursorSendRateSetting.Value, 60, 120); }
        private float CursorScale() { return Mathf.Clamp(playerCursorScaleSetting.Value, 0.60f, 1.80f); }
        private float CursorSmoothing() { return Mathf.Clamp(playerCursorSmoothingSetting.Value, 8f, 48f); }
        private static int ClampHostCapacity(int value) { return Mathf.Clamp(value, 2, 8); }

        private static LobbyPrivacy ReadPrivacy(string value)
        {
            if (string.Equals(value, "Private", StringComparison.OrdinalIgnoreCase)) return LobbyPrivacy.Private;
            if (string.Equals(value, "Public", StringComparison.OrdinalIgnoreCase)) return LobbyPrivacy.Public;
            return LobbyPrivacy.FriendsOnly;
        }

        private void SaveSettings()
        {
            Config.Save();
        }

        private void ProcessStartupLobbyArgument()
        {
            if (launchArgumentChecked || !SteamReady()) return;
            launchArgumentChecked = true;
            string commandLine = SteamApps.CommandLine ?? string.Empty;
            Match match = Regex.Match(commandLine, @"(?:^|\s)\+connect_lobby\s+([0-9]{5,20})(?:\s|$)");
            if (!match.Success) return;
            ulong lobbyId;
            if (ulong.TryParse(match.Groups[1].Value, out lobbyId) && lobbyId != 0)
            {
                SetStatus("Joining Steam lobby from launch argument.");
                JoinLobbyAsync((SteamId)lobbyId);
            }
        }

        private float CurrentMenuHeight()
        {
            if (menuSettingsVisible) return settingsPage == SettingsPage.Host ? 592f : 490f;
            return !lobby.HasValue ? 490f : (sessionActive ? (IsHost ? 675f : 590f) : 602f);
        }

        private void ClampMenuPosition(float width, float height)
        {
            if (!Finite(menuPosition.x) || !Finite(menuPosition.y)) menuPosition = new Vector2(20f, 34f);
            float maxX = Mathf.Max(8f, Screen.width - width - 8f);
            float maxY = Mathf.Max(8f, Screen.height - height - 8f);
            menuPosition.x = Mathf.Clamp(menuPosition.x, 8f, maxX);
            menuPosition.y = Mathf.Clamp(menuPosition.y, 8f, maxY);
        }

        private void HandleMenuDrag()
        {
            if (!menuVisible)
            {
                if (menuDragging) { menuDragging = false; SaveMenuPosition(); }
                return;
            }
            if (menuReveal < 0.92f) return;
            float width = Mathf.Min(510f, Screen.width - 32f);
            float height = CurrentMenuHeight();
            ClampMenuPosition(width, height);
            Event input = Event.current;
            if (input == null) return;
            Rect header = new Rect(menuPosition.x, menuPosition.y, width, 82f);
            Rect close = new Rect(menuPosition.x + width - 38f, menuPosition.y + 19f, 20f, 20f);
            Rect settings = new Rect(menuPosition.x + width - 91f, menuPosition.y + 19f, 47f, 20f);
            if (input.type == EventType.MouseDown && input.button == 0 && header.Contains(input.mousePosition) && !close.Contains(input.mousePosition) && !settings.Contains(input.mousePosition))
            {
                menuDragging = true;
                menuDragOffset = input.mousePosition - menuPosition;
                input.Use();
                return;
            }
            if (menuDragging && input.type == EventType.MouseDrag && input.button == 0)
            {
                menuPosition = input.mousePosition - menuDragOffset;
                ClampMenuPosition(width, height);
                input.Use();
                return;
            }
            if (menuDragging && input.type == EventType.MouseUp && input.button == 0)
            {
                menuDragging = false;
                SaveMenuPosition();
                input.Use();
            }
        }

        private void SaveMenuPosition()
        {
            if (menuXSetting == null || menuYSetting == null) return;
            menuXSetting.Value = menuPosition.x;
            menuYSetting.Value = menuPosition.y;
            Config.Save();
            Logger.LogDebug("[Connect][UI] Saved panel position " + menuPosition.x.ToString("F0") + ", " + menuPosition.y.ToString("F0") + ".");
        }

        private void DrawMenu()
        {
            if (ui == null) ui = new RoundedUiTheme();
            float width = Mathf.Min(510f, Screen.width - 32f);
            float height = CurrentMenuHeight();
            ClampMenuPosition(width, height);
            float x = menuPosition.x - (1f - menuReveal) * 28f;
            float y = menuPosition.y + (1f - menuReveal) * 10f;
            Color oldColor = GUI.color;
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.color = new Color(1f, 1f, 1f, menuReveal);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * (0.985f + 0.015f * menuReveal));

            Rect panel = new Rect(x, y, width, height);
            ui.Panel(panel, new Color(0.035f, 0.057f, 0.078f, 0.975f));
            ui.Card(new Rect(panel.x + 1f, panel.y + 1f, panel.width - 2f, 82f), new Color(0.075f, 0.118f, 0.15f, 0.94f));
            DrawMenuHeader(panel);

            float contentY = panel.y + 100f;
            if (menuSettingsVisible)
                DrawSettings(panel, ref contentY);
            else if (!lobby.HasValue)
                DrawLobbySetup(panel, ref contentY);
            else
                DrawLobbyMembers(panel, ref contentY);

            DrawStatusCard(panel, menuSettingsVisible || !lobby.HasValue ? height - 130f : height - 104f);
            Rect close = new Rect(panel.x + 18f, panel.y + height - 35f, 110f, 22f);
            if (DrawButton("close", close, "CLOSE  ·  F8", new Color(0.10f, 0.15f, 0.18f, 1f), new Color(0.15f, 0.23f, 0.27f, 1f), ui.ButtonSmall)) menuVisible = false;
            GUI.Label(new Rect(panel.x + 142f, panel.y + height - 33f, panel.width - 160f, 18f), menuSettingsVisible ? "Settings are saved locally   ·   Host values are relayed" : "DRAG HEADER   ·   F10 diagnostics   ·   Host Alt + LMB", ui.Small);

            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void DrawMenuHeader(Rect panel)
        {
            float pulse = 0.72f + 0.28f * Mathf.Sin(Time.unscaledTime * 3.2f);
            ui.Pill(new Rect(panel.x + 19f, panel.y + 24f, 12f, 12f), SteamReady() ? new Color(0.17f, 0.94f, 0.68f, pulse) : new Color(1f, 0.56f, 0.26f, pulse));
            float brandX = panel.x + 42f;
            if (modIcon != null)
            {
                GUI.DrawTexture(new Rect(panel.x + 38f, panel.y + 10f, 54f, 54f), modIcon, ScaleMode.ScaleToFit, true);
                brandX = panel.x + 100f;
            }
            GUI.Label(new Rect(brandX, panel.y + 13f, 174f, 31f), "CONNECT", ui.Title);
            GUI.Label(new Rect(brandX + 1f, panel.y + 44f, 186f, 17f), "HOST-AUTHORITATIVE  ·  STEAM RELAY", ui.Subtitle);
            string user = SteamReady() ? SafeName(SteamClient.Name) : "Steam unavailable";
            ui.Pill(new Rect(panel.x + panel.width - 202f, panel.y + 18f, 106f, 27f), new Color(0.10f, 0.21f, 0.25f, 1f));
            GUI.Label(new Rect(panel.x + panel.width - 196f, panel.y + 23f, 94f, 17f), Truncate(user, 13), ui.ButtonSmall);
            if (DrawButton("settings", new Rect(panel.x + panel.width - 91f, panel.y + 19f, 47f, 20f), menuSettingsVisible ? "BACK" : "SET", new Color(0.13f, 0.31f, 0.36f, 1f), new Color(0.21f, 0.59f, 0.62f, 1f), ui.ButtonSmall)) menuSettingsVisible = !menuSettingsVisible;
            if (DrawIconButton("close-top", new Rect(panel.x + panel.width - 38f, panel.y + 19f, 20f, 20f), "×", new Color(0.18f, 0.27f, 0.31f, 1f))) menuVisible = false;
            GUI.Label(new Rect(panel.x + panel.width - 202f, panel.y + 53f, 184f, 15f), "v" + PluginVersion + "  ·  GAME " + ExpectedGameVersion, ui.Small);
        }

        private void DrawSettings(Rect panel, ref float y)
        {
            float x = panel.x + 18f;
            float width = panel.width - 36f;
            GUI.Label(new Rect(x + 2f, y, width, 18f), "SETTINGS", ui.Subtitle);
            GUI.Label(new Rect(x + 110f, y, width - 112f, 18f), settingsPage == SettingsPage.Player ? "LOCAL PLAYER" : "HOST / SERVER", ui.Small);
            y += 23f;
            float tabWidth = (width - 5f) * 0.5f;
            if (DrawButton("settings-player", new Rect(x, y, tabWidth, 27f), "PLAYER", settingsPage == SettingsPage.Player ? new Color(0.13f, 0.58f, 0.62f, 1f) : new Color(0.09f, 0.18f, 0.22f, 1f), new Color(0.20f, 0.75f, 0.77f, 1f), ui.ButtonSmall)) settingsPage = SettingsPage.Player;
            if (DrawButton("settings-host", new Rect(x + tabWidth + 5f, y, tabWidth, 27f), "HOST", settingsPage == SettingsPage.Host ? new Color(0.13f, 0.58f, 0.62f, 1f) : new Color(0.09f, 0.18f, 0.22f, 1f), new Color(0.20f, 0.75f, 0.77f, 1f), ui.ButtonSmall)) settingsPage = SettingsPage.Host;
            y += 38f;
            if (settingsPage == SettingsPage.Player) DrawPlayerSettings(x, y, width);
            else DrawHostSettings(x, y, width);
        }

        private void DrawPlayerSettings(float x, float y, float width)
        {
            ui.Card(new Rect(x, y, width, 188f), new Color(0.075f, 0.122f, 0.15f, 0.98f));
            GUI.Label(new Rect(x + 14f, y + 10f, 220f, 17f), "PLAYER EXPERIENCE", ui.Subtitle);
            GUI.Label(new Rect(x + 14f, y + 28f, width - 28f, 14f), "These are local only — they never change the host simulation.", ui.Small);
            float row = y + 49f;
            if (DrawSettingToggle("player-names", new Rect(x + 14f, row, width - 28f, 23f), "Show remote player names", playerShowRemoteNamesSetting.Value))
            {
                playerShowRemoteNamesSetting.Value = !playerShowRemoteNamesSetting.Value;
                SaveSettings();
            }
            row += 27f;
            if (DrawSettingToggle("player-avatars", new Rect(x + 14f, row, width - 28f, 23f), "Show Steam avatars beside remote cursors", playerShowRemoteAvatarsSetting.Value))
            {
                playerShowRemoteAvatarsSetting.Value = !playerShowRemoteAvatarsSetting.Value;
                SaveSettings();
            }
            row += 27f;
            int scale = Mathf.RoundToInt(CursorScale() * 100f);
            int updatedScale = DrawSettingStepper("player-scale", new Rect(x + 14f, row, width - 28f, 23f), "Remote cursor scale", scale, 60, 180, "%");
            if (updatedScale != scale) { playerCursorScaleSetting.Value = updatedScale / 100f; SaveSettings(); }
            row += 27f;
            int smoothing = Mathf.RoundToInt(CursorSmoothing());
            int updatedSmoothing = DrawSettingStepper("player-smoothing", new Rect(x + 14f, row, width - 28f, 23f), "Cursor smoothing", smoothing, 8, 48, string.Empty);
            if (updatedSmoothing != smoothing) { playerCursorSmoothingSetting.Value = updatedSmoothing; SaveSettings(); }
            row += 27f;
            int sendRate = CursorSendRateHz();
            int updatedSendRate = DrawSettingStepper("player-cursor-rate", new Rect(x + 14f, row, width - 28f, 23f), "Cursor send rate", sendRate, 60, 120, " Hz");
            if (updatedSendRate != sendRate) { playerCursorSendRateSetting.Value = updatedSendRate; SaveSettings(); }
        }

        private void DrawHostSettings(float x, float y, float width)
        {
            ui.Card(new Rect(x, y, width, 272f), new Color(0.075f, 0.122f, 0.15f, 0.98f));
            bool canEdit = !lobby.HasValue || IsHost;
            GUI.Label(new Rect(x + 14f, y + 10f, 240f, 17f), canEdit ? "HOST / SERVER PROFILE" : "HOST / SERVER SETTINGS", ui.Subtitle);
            GUI.Label(new Rect(x + 14f, y + 28f, width - 28f, 14f), canEdit ? "Applied by the host only; changes are relayed to players." : "Read-only values received from the active session host.", ui.Small);
            if (!canEdit)
            {
                DrawRemoteHostSettings(x, y, width);
                return;
            }
            float row = y + 48f;
            int velocity = PhysicsVelocityIterations();
            int changedVelocity = DrawSettingStepper("host-velocity-iterations", new Rect(x + 14f, row, width - 28f, 22f), "Physics velocity iterations", velocity, 1, 16, string.Empty);
            if (changedVelocity != velocity) { hostVelocityIterationsSetting.Value = changedVelocity; HostSettingsChanged(); }
            row += 23f;
            int position = PhysicsPositionIterations();
            int changedPosition = DrawSettingStepper("host-position-iterations", new Rect(x + 14f, row, width - 28f, 22f), "Physics position iterations", position, 1, 16, string.Empty);
            if (changedPosition != position) { hostPositionIterationsSetting.Value = changedPosition; HostSettingsChanged(); }
            row += 23f;
            int snapshots = SnapshotRateHz();
            int changedSnapshots = DrawSettingStepper("host-snapshot-rate", new Rect(x + 14f, row, width - 28f, 22f), "Physics snapshot rate", snapshots, 10, 30, " Hz");
            if (changedSnapshots != snapshots) { hostSnapshotRateSetting.Value = changedSnapshots; HostSettingsChanged(); }
            row += 23f;
            int objects = MaximumNetworkObjects();
            int changedObjects = DrawSettingStepper("host-object-limit", new Rect(x + 14f, row, width - 28f, 22f), "Connect object limit", objects, 25, 1000, string.Empty);
            if (changedObjects != objects) { hostMaxNetworkObjectsSetting.Value = changedObjects; HostSettingsChanged(); }
            row += 23f;
            int guestSpawns = GuestSpawnLimitPerMinute();
            int changedGuestSpawns = DrawSettingStepper("host-guest-spawns", new Rect(x + 14f, row, width - 28f, 22f), "Guest spawns / minute", guestSpawns, 1, 60, string.Empty);
            if (changedGuestSpawns != guestSpawns) { hostGuestSpawnLimitSetting.Value = changedGuestSpawns; HostSettingsChanged(); }
            row += 23f;
            int guestInteractions = GuestInteractionLimitPerMinute();
            int changedGuestInteractions = DrawSettingStepper("host-guest-interactions", new Rect(x + 14f, row, width - 28f, 22f), "Guest interactions / minute", guestInteractions, 5, 120, string.Empty);
            if (changedGuestInteractions != guestInteractions) { hostGuestInteractionLimitSetting.Value = changedGuestInteractions; HostSettingsChanged(); }
            row += 23f;
            int botLimit = BotSpawnLimit();
            int changedBotLimit = DrawSettingStepper("host-bot-limit", new Rect(x + 14f, row, width - 28f, 22f), "Bot spawns / session", botLimit, 0, 100, string.Empty);
            if (changedBotLimit != botLimit) { hostBotSpawnLimitSetting.Value = changedBotLimit; HostSettingsChanged(); }
            row += 23f;
            float toggleWidth = (width - 38f) / 3f;
            if (DrawSettingToggle("host-guest-spawn", new Rect(x + 14f, row, toggleWidth, 22f), "Guests spawn", hostGuestsCanSpawnSetting.Value)) { hostGuestsCanSpawnSetting.Value = !hostGuestsCanSpawnSetting.Value; HostSettingsChanged(); }
            if (DrawSettingToggle("host-guest-grab", new Rect(x + 19f + toggleWidth, row, toggleWidth, 22f), "Guests grab", hostGuestsCanGrabSetting.Value)) { hostGuestsCanGrabSetting.Value = !hostGuestsCanGrabSetting.Value; HostSettingsChanged(); }
            if (DrawSettingToggle("host-guest-activate", new Rect(x + 24f + toggleWidth * 2f, row, toggleWidth, 22f), "Guests use", hostGuestsCanActivateSetting.Value)) { hostGuestsCanActivateSetting.Value = !hostGuestsCanActivateSetting.Value; HostSettingsChanged(); }
            row += 25f;
            if (DrawSettingToggle("host-guest-delete", new Rect(x + 14f, row, toggleWidth, 22f), "Guests delete", hostGuestsCanDeleteSetting.Value)) { hostGuestsCanDeleteSetting.Value = !hostGuestsCanDeleteSetting.Value; HostSettingsChanged(); }
            if (DrawSettingToggle("host-bots", new Rect(x + 19f + toggleWidth, row, toggleWidth, 22f), "Bots", hostBotsAllowedSetting.Value)) { hostBotsAllowedSetting.Value = !hostBotsAllowedSetting.Value; HostSettingsChanged(); }
        }

        private void DrawRemoteHostSettings(float x, float y, float width)
        {
            if (!remoteHostSettings.Received)
            {
                GUI.Label(new Rect(x + 14f, y + 76f, width - 28f, 34f), "Waiting for the host settings message. It arrives after the Steam Relay handshake.", ui.Small);
                return;
            }
            float row = y + 51f;
            DrawReadOnlySetting(new Rect(x + 14f, row, width - 28f, 20f), "Physics velocity / position", remoteHostSettings.VelocityIterations + " / " + remoteHostSettings.PositionIterations);
            row += 23f;
            DrawReadOnlySetting(new Rect(x + 14f, row, width - 28f, 20f), "Physics snapshot rate", remoteHostSettings.SnapshotRate + " Hz");
            row += 23f;
            DrawReadOnlySetting(new Rect(x + 14f, row, width - 28f, 20f), "Connect object limit", remoteHostSettings.MaximumObjects.ToString());
            row += 23f;
            DrawReadOnlySetting(new Rect(x + 14f, row, width - 28f, 20f), "Guest spawn limit", remoteHostSettings.GuestSpawnsPerMinute + " / min");
            row += 23f;
            DrawReadOnlySetting(new Rect(x + 14f, row, width - 28f, 20f), "Permissions", (remoteHostSettings.GuestsCanSpawn ? "Spawn " : "No spawn ") + (remoteHostSettings.GuestsCanGrab ? "· Grab " : "· No grab ") + (remoteHostSettings.GuestsCanActivate ? "· Use " : "· No use ") + (remoteHostSettings.GuestsCanDelete ? "· Delete" : "· No delete"));
            row += 23f;
            DrawReadOnlySetting(new Rect(x + 14f, row, width - 28f, 20f), "Bot spawns / session", remoteHostSettings.BotSpawnLimit.ToString());
        }

        private bool DrawSettingToggle(string id, Rect rect, string label, bool enabled)
        {
            GUI.Label(new Rect(rect.x, rect.y + 3f, rect.width - 72f, rect.height), label, ui.Small);
            return DrawButton(id, new Rect(rect.x + rect.width - 66f, rect.y, 66f, rect.height), enabled ? "ON" : "OFF", enabled ? new Color(0.14f, 0.57f, 0.40f, 1f) : new Color(0.28f, 0.14f, 0.18f, 1f), enabled ? new Color(0.22f, 0.76f, 0.53f, 1f) : new Color(0.44f, 0.23f, 0.28f, 1f), ui.ButtonSmall);
        }

        private int DrawSettingStepper(string id, Rect rect, string label, int value, int minimum, int maximum, string suffix)
        {
            GUI.Label(new Rect(rect.x, rect.y + 3f, rect.width - 132f, rect.height), label, ui.Small);
            float controlsX = rect.x + rect.width - 127f;
            if (DrawIconButton(id + "-", new Rect(controlsX, rect.y, 23f, rect.height), "−", new Color(0.11f, 0.20f, 0.24f, 1f))) value = Mathf.Max(minimum, value - 1);
            ui.Pill(new Rect(controlsX + 28f, rect.y, 70f, rect.height), new Color(0.10f, 0.27f, 0.31f, 1f));
            GUI.Label(new Rect(controlsX + 28f, rect.y + 3f, 70f, rect.height), value + suffix, ui.ButtonSmall);
            if (DrawIconButton(id + "+", new Rect(controlsX + 103f, rect.y, 24f, rect.height), "+", new Color(0.11f, 0.20f, 0.24f, 1f))) value = Mathf.Min(maximum, value + 1);
            return value;
        }

        private void DrawReadOnlySetting(Rect rect, string label, string value)
        {
            GUI.Label(new Rect(rect.x, rect.y + 2f, rect.width - 115f, rect.height), label, ui.Small);
            ui.Pill(new Rect(rect.x + rect.width - 110f, rect.y, 110f, rect.height), new Color(0.10f, 0.24f, 0.29f, 1f));
            GUI.Label(new Rect(rect.x + rect.width - 106f, rect.y + 2f, 102f, rect.height), value, ui.ButtonSmall);
        }

        private void DrawLobbySetup(Rect panel, ref float y)
        {
            float x = panel.x + 18f;
            float width = panel.width - 36f;
            GUI.Label(new Rect(x + 2f, y, width, 18f), "CREATE A LOBBY", ui.Subtitle);
            y += 24f;
            ui.Card(new Rect(x, y, width, 170f), new Color(0.075f, 0.112f, 0.142f, 0.98f));
            GUI.Label(new Rect(x + 16f, y + 14f, 220f, 20f), "Privacy", ui.Label);
            GUI.Label(new Rect(x + 16f, y + 35f, width - 32f, 17f), "Who can discover and join the Steam lobby", ui.Small);
            float choiceWidth = (width - 48f) / 3f;
            DrawPrivacyChoice("private", new Rect(x + 16f, y + 61f, choiceWidth, 32f), LobbyPrivacy.Private, "PRIVATE");
            DrawPrivacyChoice("friends", new Rect(x + 24f + choiceWidth, y + 61f, choiceWidth, 32f), LobbyPrivacy.FriendsOnly, "FRIENDS");
            DrawPrivacyChoice("public", new Rect(x + 32f + choiceWidth * 2f, y + 61f, choiceWidth, 32f), LobbyPrivacy.Public, "PUBLIC");
            GUI.Label(new Rect(x + 16f, y + 113f, 180f, 20f), "Maximum players", ui.Label);
            GUI.Label(new Rect(x + 16f, y + 135f, 210f, 17f), "Steam lobby capacity", ui.Small);
            Rect minus = new Rect(x + width - 130f, y + 119f, 31f, 31f);
            Rect value = new Rect(x + width - 94f, y + 119f, 44f, 31f);
            Rect plus = new Rect(x + width - 45f, y + 119f, 31f, 31f);
            if (DrawIconButton("players-minus", minus, "−", new Color(0.12f, 0.20f, 0.24f, 1f))) { maxPlayers = Mathf.Max(2, maxPlayers - 1); hostDefaultMaxPlayersSetting.Value = maxPlayers; SaveSettings(); }
            ui.Pill(value, new Color(0.11f, 0.25f, 0.29f, 1f));
            GUI.Label(value, maxPlayers.ToString(), ui.Center);
            if (DrawIconButton("players-plus", plus, "+", new Color(0.12f, 0.20f, 0.24f, 1f))) { maxPlayers = Mathf.Min(8, maxPlayers + 1); hostDefaultMaxPlayersSetting.Value = maxPlayers; SaveSettings(); }
            y += 185f;
            if (DrawButton("create-lobby", new Rect(x, y, width, 44f), "CREATE STEAM LOBBY   +", new Color(0.19f, 0.88f, 0.88f, 1f), new Color(0.42f, 1f, 0.94f, 1f), ui.Center)) CreateLobbyAsync();
            GUI.Label(new Rect(x + 4f, y + 52f, width - 8f, 17f), "Friends Only is the default. Steam Overlay is used for invitations.", ui.Small);
        }

        private void DrawPrivacyChoice(string id, Rect rect, LobbyPrivacy value, string label)
        {
            bool selected = privacy == value;
            Color normal = selected ? new Color(0.12f, 0.55f, 0.61f, 1f) : new Color(0.10f, 0.17f, 0.21f, 1f);
            Color hover = selected ? new Color(0.18f, 0.76f, 0.78f, 1f) : new Color(0.16f, 0.29f, 0.34f, 1f);
            if (DrawButton(id, rect, label, normal, hover, selected ? ui.ButtonText : ui.ButtonSmall))
            {
                privacy = value;
                hostDefaultPrivacySetting.Value = privacy.ToString();
                SaveSettings();
            }
        }

        private void DrawLobbyMembers(Rect panel, ref float y)
        {
            Lobby current = lobby.Value;
            float x = panel.x + 18f;
            float width = panel.width - 36f;
            ui.Card(new Rect(x, y, width, 61f), new Color(0.075f, 0.126f, 0.155f, 0.98f));
            GUI.Label(new Rect(x + 15f, y + 9f, 230f, 18f), "YOUR STEAM LOBBY", ui.Subtitle);
            GUI.Label(new Rect(x + 15f, y + 29f, 310f, 18f), "Players  " + current.MemberCount + " / " + current.MaxMembers + "    ·    Steam Relay", ui.Label);
            string state = sessionActive ? "PLAYING" : (hostStartAwaitingMap ? "LOADING" : "LOBBY");
            ui.Pill(new Rect(x + width - 88f, y + 17f, 70f, 25f), sessionActive ? new Color(0.16f, 0.63f, 0.41f, 1f) : (hostStartAwaitingMap ? new Color(0.58f, 0.34f, 0.12f, 1f) : new Color(0.16f, 0.36f, 0.47f, 1f)));
            GUI.Label(new Rect(x + width - 84f, y + 22f, 62f, 15f), state, ui.ButtonSmall);
            y += 73f;
            GUI.Label(new Rect(x + 2f, y, width, 18f), "MEMBERS", ui.Subtitle);
            y += 24f;
            foreach (Friend member in current.Members)
            {
                DrawMemberCard(current, member, new Rect(x, y, width, 49f));
                y += 55f;
            }
            for (int i = current.MemberCount; i < current.MaxMembers; i++)
            {
                DrawInviteCard("invite-" + i, new Rect(x, y, width, 43f), i);
                y += 49f;
            }
            y += 4f;
            if (IsHost && !sessionActive)
            {
                if (DrawButton("start-session", new Rect(x, y, width, 42f), "START & SYNC MAP   ▶", new Color(0.19f, 0.88f, 0.88f, 1f), new Color(0.42f, 1f, 0.94f, 1f), ui.Center)) StartSession();
                y += 52f;
            }
            if (sessionActive && IsHost)
            {
                ui.Card(new Rect(x, y, width, 72f), new Color(0.09f, 0.13f, 0.18f, 1f));
                GUI.Label(new Rect(x + 14f, y + 10f, 150f, 17f), "BOT MODE", ui.Subtitle);
                GUI.Label(new Rect(x + 14f, y + 31f, 250f, 16f), botsEnabled ? "Spawn, grab, place and safely clean their own items." : "Host-only sandbox assistants.", ui.Small);
                int before = botCount;
                if (DrawIconButton("bots-minus", new Rect(x + width - 186f, y + 22f, 27f, 29f), "−", new Color(0.12f, 0.20f, 0.24f, 1f))) botCount = Mathf.Max(1, botCount - 1);
                ui.Pill(new Rect(x + width - 154f, y + 22f, 34f, 29f), new Color(0.12f, 0.26f, 0.30f, 1f));
                GUI.Label(new Rect(x + width - 154f, y + 28f, 34f, 15f), botCount.ToString(), ui.Center);
                if (DrawIconButton("bots-plus", new Rect(x + width - 115f, y + 22f, 27f, 29f), "+", new Color(0.12f, 0.20f, 0.24f, 1f))) botCount = Mathf.Min(MaximumBots, botCount + 1);
                if (botCount != before && botsEnabled) { BuildBots(); BroadcastBotMode(true, botCount); }
                if (DrawButton("bot-toggle", new Rect(x + width - 82f, y + 22f, 68f, 29f), botsEnabled ? "ON" : "OFF", botsEnabled ? new Color(0.16f, 0.64f, 0.40f, 1f) : new Color(0.25f, 0.15f, 0.18f, 1f), botsEnabled ? new Color(0.24f, 0.82f, 0.52f, 1f) : new Color(0.43f, 0.23f, 0.27f, 1f), ui.ButtonSmall)) SetBotMode(!botsEnabled);
                y += 82f;
            }
            if (DrawButton("leave-lobby", new Rect(x, y, width, 31f), "LEAVE LOBBY", new Color(0.20f, 0.10f, 0.13f, 1f), new Color(0.43f, 0.16f, 0.20f, 1f), ui.ButtonSmall)) LeaveLobby();
        }

        private void DrawMemberCard(Lobby current, Friend member, Rect rect)
        {
            ulong memberId = (ulong)member.Id;
            bool host = member.Id == current.Owner.Id;
            Peer peer = null;
            if (IsHost && !host) peers.TryGetValue(memberId, out peer);
            avatars.Request(memberId);
            Texture2D avatar = avatars.Get(memberId);
            ui.Card(rect, new Color(0.09f, 0.15f, 0.18f, 1f));
            ui.Pill(new Rect(rect.x + 9f, rect.y + 8f, 33f, 33f), host ? new Color(0.90f, 0.67f, 0.22f, 1f) : CursorColor(GetPeerId(memberId)));
            if (avatar != null) GUI.DrawTexture(new Rect(rect.x + 11f, rect.y + 10f, 29f, 29f), avatar, ScaleMode.StretchToFill, true);
            else GUI.Label(new Rect(rect.x + 10f, rect.y + 14f, 31f, 17f), "?", ui.Center);
            GUI.Label(new Rect(rect.x + 53f, rect.y + 8f, rect.width - 180f, 19f), Truncate(SafeName(member.Name), 27), ui.Label);
            PeerMapStatus guestMapStatus = peer == null ? (sessionActive ? PeerMapStatus.LoadingMap : PeerMapStatus.InLobby) : peer.MapStatus;
            string guestStatusLabel = IsHost ? PeerMapStatusLabel(guestMapStatus) : (sessionActive ? "PLAYING" : (hostStartAwaitingMap ? "WAITING FOR MAP" : "IN LOBBY"));
            string detail = host ? "SESSION HOST" : guestStatusLabel;
            GUI.Label(new Rect(rect.x + 53f, rect.y + 27f, 150f, 14f), detail, ui.Small);
            if (host)
            {
                ui.Pill(new Rect(rect.x + rect.width - 77f, rect.y + 13f, 61f, 22f), new Color(0.48f, 0.33f, 0.10f, 1f));
                GUI.Label(new Rect(rect.x + rect.width - 73f, rect.y + 17f, 53f, 14f), "HOST", ui.ButtonSmall);
            }
            else
            {
                UColor statusColor = IsHost ? PeerMapStatusColor(guestMapStatus) : new Color(0.09f, 0.34f, 0.31f, 1f);
                ui.Pill(new Rect(rect.x + rect.width - 98f, rect.y + 13f, 82f, 22f), statusColor);
                GUI.Label(new Rect(rect.x + rect.width - 94f, rect.y + 17f, 74f, 14f), IsHost ? guestStatusLabel : (sessionActive ? "PLAYING" : (hostStartAwaitingMap ? "SYNCING" : "READY")), ui.ButtonSmall);
            }
        }

        private static UColor PeerMapStatusColor(PeerMapStatus mapStatus)
        {
            switch (mapStatus)
            {
                case PeerMapStatus.LoadingMap: return new UColor(0.56f, 0.34f, 0.12f, 1f);
                case PeerMapStatus.Synchronising: return new UColor(0.12f, 0.32f, 0.52f, 1f);
                case PeerMapStatus.Playing: return new UColor(0.09f, 0.34f, 0.31f, 1f);
                case PeerMapStatus.Failed: return new UColor(0.48f, 0.12f, 0.18f, 1f);
                default: return new UColor(0.14f, 0.26f, 0.32f, 1f);
            }
        }

        private void DrawInviteCard(string id, Rect rect, int slot)
        {
            float hover = Hover(id, rect);
            float pulse = 0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * 2.5f + slot * 0.7f);
            ui.Card(Expand(rect, hover * 1.5f), Color.Lerp(new Color(0.07f, 0.12f, 0.15f, 0.98f), new Color(0.11f, 0.25f, 0.29f, 1f), hover));
            ui.Pill(new Rect(rect.x + 10f, rect.y + 7f, 29f, 29f), new Color(0.13f, 0.53f, 0.58f, pulse));
            GUI.Label(new Rect(rect.x + 10f, rect.y + 12f, 29f, 18f), "+", ui.Center);
            GUI.Label(new Rect(rect.x + 51f, rect.y + 7f, 180f, 16f), "Invite a friend", ui.Label);
            GUI.Label(new Rect(rect.x + 51f, rect.y + 23f, 240f, 14f), "Open Steam's official invite dialog", ui.Small);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) InviteFriends();
        }

        private void DrawStatusCard(Rect panel, float localY)
        {
            Rect rect = new Rect(panel.x + 18f, panel.y + localY, panel.width - 36f, 52f);
            bool warning = status.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 || status.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0 || status.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0;
            UColor color = warning ? new UColor(0.25f, 0.15f, 0.12f, 0.98f) : new UColor(0.08f, 0.18f, 0.20f, 0.98f);
            ui.Card(rect, color);
            ui.Pill(new Rect(rect.x + 12f, rect.y + 18f, 8f, 8f), warning ? new Color(1f, 0.58f, 0.28f, 1f) : new Color(0.21f, 0.92f, 0.76f, 1f));
            GUI.Label(new Rect(rect.x + 30f, rect.y + 8f, 110f, 15f), warning ? "CONNECTION NOTICE" : "NETWORK STATUS", ui.Subtitle);
            GUI.Label(new Rect(rect.x + 30f, rect.y + 25f, rect.width - 43f, 18f), Truncate(status, 67), ui.Small);
        }

        private bool DrawButton(string id, Rect rect, string text, UColor normal, UColor hoverColor, GUIStyle textStyle)
        {
            float hover = Hover(id, rect);
            ui.Pill(Expand(rect, hover * 1.3f), UColor.Lerp(normal, hoverColor, hover));
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            GUI.Label(rect, text, textStyle);
            return clicked;
        }

        private bool DrawIconButton(string id, Rect rect, string text, UColor normal)
        {
            float hover = Hover(id, rect);
            ui.Pill(Expand(rect, hover), UColor.Lerp(normal, new UColor(0.29f, 0.72f, 0.76f, 1f), hover));
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            GUI.Label(rect, text, ui.Center);
            return clicked;
        }

        private float Hover(string id, Rect rect)
        {
            float current;
            if (!uiHover.TryGetValue(id, out current)) current = 0f;
            bool over = rect.Contains(Event.current.mousePosition);
            float target = over ? 1f : 0f;
            current = Mathf.MoveTowards(current, target, Time.unscaledDeltaTime * 9f);
            uiHover[id] = current;
            return current;
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(rect.x - amount, rect.y - amount, rect.width + amount * 2f, rect.height + amount * 2f);
        }

        private static string Truncate(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximum) return value ?? string.Empty;
            return value.Substring(0, Mathf.Max(1, maximum - 1)) + "…";
        }

        private void DrawRemoteCursors()
        {
            Camera activeCamera = GetActiveCamera();
            if (!lobby.HasValue || cursors.Count == 0 || activeCamera == null) return;
            if (ui == null) ui = new RoundedUiTheme();
            foreach (RemoteCursor cursor in cursors.Values)
            {
                if (cursor.SteamId == (ulong)SteamClient.SteamId || (!cursor.IsProvisional && Time.unscaledTime - cursor.LastAt > 2f)) continue;
                Vector3 screen = activeCamera.WorldToScreenPoint(cursor.Render); if (screen.z < 0f) continue;
                float x = screen.x; float y = Screen.height - screen.y;
                Texture2D avatar = null;
                if (!cursor.IsBot && playerShowRemoteAvatarsSetting.Value)
                {
                    avatars.Request(cursor.SteamId);
                    avatar = avatars.Get(cursor.SteamId);
                }
                float pulse = (cursor.Buttons & 1) != 0 ? 1.12f + 0.12f * Mathf.Sin(Time.unscaledTime * 12f) : 1f;
                float markerSize = 24f * CursorScale() * pulse;
                ui.CursorRing(new Rect(x - markerSize * 0.5f, y - markerSize * 0.5f, markerSize, markerSize), cursor.Color);
                ui.CursorDot(new Rect(x - 3f, y - 3f, 6f, 6f), cursor.Color);
                if (playerShowRemoteAvatarsSetting.Value && avatar != null) GUI.DrawTexture(new Rect(x - 26f, y - 27f, 16f, 16f), avatar, ScaleMode.StretchToFill, true);
                if (playerShowRemoteNamesSetting.Value)
                {
                    GUI.color = cursor.Color;
                    string activity = cursor.IsProvisional ? "  SYNCING" : (cursor.IsBot ? ((cursor.Buttons & 1) != 0 ? "  SPAWN" : "  BOT") : ((cursor.Buttons & 1) != 0 ? "  GRAB" : (cursor.UiBusy ? "  UI" : string.Empty)));
                    GUI.Label(new Rect(x + 13f, y - 16f, 172f, 20f), cursor.Name + activity, ui.Label);
                    GUI.color = UColor.white;
                }
            }
            GUI.color = UColor.white;
        }

        private void DrawDebug()
        {
            GUILayout.BeginArea(new Rect(Screen.width - 315f, 20f, 295f, 220f), GUI.skin.box);
            GUILayout.Label("CONNECT DEBUG");
            GUILayout.Label("Role: " + (IsHost ? "HOST" : (lobby.HasValue ? "CLIENT" : "NONE")));
            GUILayout.Label("Lobby: " + (lobby.HasValue ? ((ulong)lobby.Value.Id).ToString() : "none"));
            GUILayout.Label("Session: " + sessionActive + " · Peer: " + clientPeerId);
            GUILayout.Label("Relay: " + (transport != null && (transport.Hosting || transport.Connected) ? "ready" : "idle"));
            GUILayout.Label("Peers: " + peers.Count + " · Cursors: " + cursors.Count);
            GUILayout.Label("Objects: " + registry.Count + " · Bots: " + bots.Count + " · Tick: " + hostTick);
            GUILayout.Label("Bot knowledge: " + botWorld.Count + " · Catalog: " + botCatalog.Count);
            GUILayout.Label("Harmony world-input patch: " + (patchApplied ? "applied" : "not applied"));
            GUILayout.EndArea();
        }

        private void TryInstallPatch()
        {
            try
            {
                new Harmony(PluginGuid).PatchAll(typeof(PPGTogetherPlugin).Assembly);
                patchApplied = true;
                Logger.LogInfo("[Connect][Patch] ToolControllerBehaviour.HandleTools patch applied.");
            }
            catch (Exception exception)
            {
                patchApplied = false;
                Logger.LogError("[Connect][Patch] Input patch unavailable; client interaction will stay disabled. " + exception.Message);
            }
        }

        internal bool ShouldBlockVanillaWorldInput { get { return patchApplied && !IsHost && sessionActive && lobby.HasValue; } }
        // CatalogBehaviour.Spawn is independently patched. Do not make its
        // routing depend on the unrelated world-tool patch flag: otherwise a
        // harmless tool compatibility failure silently turns a guest Tab spawn
        // into an unsynchronised local item.
        internal bool ShouldRouteVanillaCatalogSpawn { get { return !IsHost && sessionActive && lobby.HasValue; } }
        private bool IsHost { get { return lobby.HasValue && SteamReady() && lobby.Value.Owner.Id == SteamClient.SteamId && transport != null && transport.Hosting; } }

        private ushort GetPeerId(ulong steamId)
        {
            if (steamId == (ulong)SteamClient.SteamId) return 0;
            Peer peer; return peers.TryGetValue(steamId, out peer) ? peer.PeerId : (ushort)0;
        }

        private void SendReject(Connection connection, string reason)
        {
            Writer writer = new Writer(64); writer.String(reason); SendToConnection(connection, WireMessage.Reject, WireChannel.Control, 0, writer.ToArray(), true);
        }
        private void SendActionDenied(Connection connection, string reason) { Writer writer = new Writer(64); writer.String(reason); SendToConnection(connection, WireMessage.ActionDenied, WireChannel.World, 0, writer.ToArray(), true); }
        private void SendGrabDenied(Connection connection, string reason) { Writer writer = new Writer(64); writer.String(reason); SendToConnection(connection, WireMessage.GrabDenied, WireChannel.World, 0, writer.ToArray(), true); }
        private void SendToConnection(Connection connection, WireMessage type, WireChannel channel, ushort peerId, byte[] payload, bool reliable) { transport.SendToClient(connection, Wire.Pack(type, channel, nonce, peerId, ++sequence, hostTick, payload), reliable); }
        private void SendToHost(WireMessage type, WireChannel channel, byte[] payload, bool reliable) { transport.SendToHost(Wire.Pack(type, channel, nonce, clientPeerId, ++sequence, hostTick, payload), reliable); }
        private void Broadcast(WireMessage type, WireChannel channel, byte[] payload, bool reliable) { foreach (Peer peer in peers.Values) SendToConnection(peer.Connection, type, channel, peer.PeerId, payload, reliable); }
        private void BroadcastFromPeer(WireMessage type, WireChannel channel, ushort sourcePeerId, byte[] payload, bool reliable) { foreach (Peer peer in peers.Values) SendToConnection(peer.Connection, type, channel, sourcePeerId, payload, reliable); }

        private void SetStatus(string value) { status = value ?? string.Empty; Logger.LogInfo("[Connect] " + status); }
        private static Camera GetActiveCamera()
        {
            if (Global.main != null && Global.main.camera != null) return Global.main.camera;
            return Camera.main;
        }
        private static Vector2 GetWorldCursor() { Camera activeCamera = GetActiveCamera(); return Global.main != null ? (Vector2)Global.main.MousePosition : (activeCamera != null ? (Vector2)activeCamera.ScreenToWorldPoint(Input.mousePosition) : Vector2.zero); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static ulong MakeNonce() { byte[] bytes = Guid.NewGuid().ToByteArray(); return BitConverter.ToUInt64(bytes, 0) ^ (ulong)DateTime.UtcNow.Ticks; }
        private static string SafeName(string name) { if (string.IsNullOrEmpty(name)) return "Player"; name = name.Replace("<", "&lt;").Replace(">", "&gt;"); return name.Length > 32 ? name.Substring(0, 32) : name; }
        private static UColor CursorColor(ushort peerId)
        {
            CursorColorRgb rgb = CursorColorPalette.ForPeer(peerId);
            return new UColor(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f, 1f);
        }

        private static bool IsBotPeer(ushort peerId) { return peerId >= BotPeerBase && peerId < BotPeerBase + MaximumBots; }

        private static UColor BotColor(int index)
        {
            switch (index % MaximumBots)
            {
                case 0: return new UColor(1f, 0.35f, 0.35f, 1f);
                case 1: return new UColor(0.35f, 1f, 0.55f, 1f);
                default: return new UColor(0.72f, 0.43f, 1f, 1f);
            }
        }
        private static string BotDisplayName(int index)
        {
            switch (index % MaximumBots)
            {
                case 0: return "Builder Bot";
                case 1: return "Mover Bot";
                default: return "Cleaner Bot";
            }
        }
        private static GUIStyle HeaderStyle() { GUIStyle style = new GUIStyle(GUI.skin.label); style.fontSize = 20; style.fontStyle = FontStyle.Bold; style.normal.textColor = new UColor(0.3f, 0.9f, 1f); return style; }
        private static GUIStyle WrapStyle() { GUIStyle style = new GUIStyle(GUI.skin.label); style.wordWrap = true; return style; }

        private sealed class Peer { internal ulong SteamId; internal ushort PeerId; internal Connection Connection; internal string Name; internal PeerMapStatus MapStatus; internal string MapIdentity; }
        private struct SpawnRateWindow { internal float StartTime; internal int Count; }
        private struct HostSettingsView
        {
            internal bool Received;
            internal int VelocityIterations;
            internal int PositionIterations;
            internal int SnapshotRate;
            internal int MaximumObjects;
            internal int GuestSpawnsPerMinute;
            internal bool GuestsCanSpawn;
            internal bool GuestsCanGrab;
            internal bool GuestsCanActivate;
            internal bool GuestsCanDelete;
            internal bool BotsAllowed;
            internal int BotSpawnLimit;
        }
        private sealed class RemoteCursor { internal ushort PeerId; internal ulong SteamId; internal string Name; internal UColor Color; internal Vector2 Target; internal Vector2 Render; internal Vector2 Velocity; internal uint LastSequence; internal byte Buttons; internal float LastAt; internal bool UiBusy; internal bool IsBot; internal bool IsProvisional; }
        private sealed class BotSpawnRecord { internal ushort OwnerPeerId; internal ulong NetId; internal GameObject Instance; internal float CreatedAt; }
        private sealed class BotAgent
        {
            internal int Index;
            internal ushort PeerId;
            internal ulong SteamId;
            internal string Name;
            internal BotMind Mind;
            internal BotDecision Decision;
            internal BotAction Action;
            internal Vector2 Origin;
            internal Vector2 Position;
            internal Vector2 Target;
            internal Vector2 Velocity;
            internal Vector2 InteractionPoint;
            internal Vector2 PlaceTarget;
            internal float ActionUntil;
            internal float NextDecisionAt;
            internal float NextBroadcastAt;
            internal BotSpawnRecord CurrentItem;
            internal ulong GrabNetId;
            internal uint GrabToken;
        }
    }
}
