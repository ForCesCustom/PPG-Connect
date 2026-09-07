using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

// Developer-only, separate DLL. Never package this harness in a release.
// The explicit process flag authorises a fresh sandbox and private Steam lobby;
// no invitations, fake peers, subscriptions or persisted configuration changes.
[BepInPlugin("local.connect.host-spawn-regression", "Connect Host Spawn Regression", "1.0.0")]
public sealed class HostSpawnRegressionRuntime : BaseUnityPlugin
{
    private const string MapId = "fb813068-e717-45de-a97f-4677a41758e6";
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private readonly List<GameObject> owned = new List<GameObject>();
    private object plugin;
    private Type pluginType;
    private object originalPrivacy;
    private bool ownsLobby;
    private int checks;
    private string resultPath;

    private void Awake()
    {
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg == "-connect-host-spawn-smoke")
            {
                resultPath = Path.Combine(Path.GetTempPath(), "Connect-HostSpawnRegression-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(resultPath, "RUNNING " + DateTime.UtcNow.ToString("O") + Environment.NewLine);
                Logger.LogInfo("[HostSpawnRegression] Durable result: " + resultPath);
                Logger.LogInfo("[HostSpawnRegression] Armed explicitly for this process.");
                StartCoroutine(RunGuarded());
                return;
            }
    }

    private IEnumerator RunGuarded()
    {
        IEnumerator test = Run();
        bool failed = false;
        while (true)
        {
            bool more = false;
            object current = null;
            try { more = test.MoveNext(); if (more) current = test.Current; }
            catch (Exception error) { failed = true; Report("FAILED: " + error, true); }
            if (failed || !more) break;
            yield return current;
        }
        try { Cleanup(); }
        catch (Exception error) { failed = true; Report("Cleanup FAILED: " + error, true); }
        if (!failed) Report("PASSED " + checks + " assertions: native host spawn after ModAPI.ClearEvents, private real Steam host, exact production registration and payload. Not a two-account test.", false);
        // BepInEx's disk listener may buffer the last lines; Unity's Player.log
        // and the explicitly flushed result file remain independent evidence.
        float flushAt = Time.realtimeSinceStartup + 2f;
        while (Time.realtimeSinceStartup < flushAt) yield return null;
        Application.Quit();
    }

    private IEnumerator Run()
    {
        float deadline = Time.realtimeSinceStartup + 90f;
        while (plugin == null)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                pluginType = assembly.GetType("PPGTogether.BepInEx.PPGTogetherPlugin", false);
                if (pluginType != null) { plugin = pluginType.GetField("Instance", Flags).GetValue(null); if (plugin != null) break; }
            }
            Require(Time.realtimeSinceStartup < deadline, "production plugin startup timeout");
            yield return null;
        }
        Require(Field(plugin, "lobby") == null, "refuse pre-existing lobby");
        while (!(bool)Call(plugin, "SteamReady"))
        {
            Require(Time.realtimeSinceStartup < deadline, "Steam startup timeout");
            yield return null;
        }
        if (!string.Equals(SceneManager.GetActiveScene().name, "Main", StringComparison.OrdinalIgnoreCase))
        {
            Map map = null;
            while (map == null)
            {
                map = (Map)pluginType.GetMethod("FindInstalledMap", Flags).Invoke(null, new object[] { MapId });
                Require(Time.realtimeSinceStartup < deadline, "installed default map lookup timeout");
                yield return null;
            }
            MapLoaderBehaviour.CurrentMap = map;
            SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
        }
        deadline = Time.realtimeSinceStartup + 90f;
        while (Global.main == null || CatalogBehaviour.Main == null || ModAPI.FindSpawnable("Human") == null ||
            !string.Equals(SceneManager.GetActiveScene().name, "Main", StringComparison.OrdinalIgnoreCase))
        {
            Require(Time.realtimeSinceStartup < deadline, "sandbox startup timeout");
            yield return null;
        }
        // A separately enabled object-state harness performs isolated fixtures
        // before this driver starts a real lobby.
        float settleAt = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < settleAt) yield return null;
        Check(Field(plugin, "lobby") == null, "no lobby before test-owned creation");
        FieldInfo privacy = pluginType.GetField("privacy", Flags);
        originalPrivacy = privacy.GetValue(plugin);
        privacy.SetValue(plugin, Enum.Parse(privacy.FieldType, "Private"));
        Call(plugin, "CreateLobbyAsync");
        ownsLobby = true;
        deadline = Time.realtimeSinceStartup + 30f;
        while (!(bool)pluginType.GetProperty("IsHost", Flags).GetValue(plugin, null))
        {
            Require(Time.realtimeSinceStartup < deadline, "private real Steam lobby timeout");
            yield return null;
        }
        privacy.SetValue(plugin, originalPrivacy);
        CheckSolo();
        Call(plugin, "BeginHostSession", MapId);
        Check((bool)Field(plugin, "sessionActive"), "production host session active");
        object registry = Field(plugin, "registry");
        int initial = RegistryCount(registry);
        string[] keys = { "Human", "Android", "Metal Cube" };
        for (int i = 0; i < keys.Length; i++)
        {
            CheckSolo();
            SpawnableAsset asset = ModAPI.FindSpawnable(keys[i]);
            Check(asset != null, "fixture available " + keys[i]);
            int before = RegistryCount(registry);
            HashSet<ulong> previous = Ids(registry);
            typeof(ModAPI).GetMethod("ClearEvents", Flags).Invoke(null, null);
            SpawnableAsset saved = CatalogBehaviour.Main.SelectedItem;
            Vector3 savedMouse = Global.main.MousePosition;
            try
            {
                CatalogBehaviour.Main.SelectedItem = asset;
                Global.main.MousePosition = new Vector3(60f + i * 8f, 10f, 0f);
                typeof(CatalogBehaviour).GetMethod("Spawn", Flags, null, new[] { typeof(SpawnableAsset), typeof(bool) }, null)
                    .Invoke(CatalogBehaviour.Main, new object[] { asset, i == 1 });
            }
            finally { CatalogBehaviour.Main.SelectedItem = saved; Global.main.MousePosition = savedMouse; }
            object identity = null;
            foreach (object candidate in (IEnumerable)Call(registry, "All"))
                if (!previous.Contains((ulong)Field(candidate, "NetId")))
                {
                    Check(identity == null, "one new identity per native spawn");
                    identity = candidate;
                    GameObject root = ((Component)candidate).gameObject;
                    owned.Add(root);
                    Freeze(root);
                }
            Check(identity != null && RegistryCount(registry) == before + 1, "immediate production registration after ClearEvents " + keys[i]);
            Check((string)Field(identity, "SpawnKey") == keys[i], "exact host SpawnKey " + keys[i]);
            GameObject instance = ((Component)identity).gameObject;
            byte[] payload = (byte[])Call(plugin, "BuildSpawnPayload", identity, instance);
            using (BinaryReader reader = new BinaryReader(new MemoryStream(payload)))
            {
                Check(reader.ReadUInt64() == (ulong)Field(identity, "NetId"), "spawn payload ID");
                ushort length = reader.ReadUInt16();
                Check(System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length)) == keys[i], "spawn payload key");
            }
            Type stateType = pluginType.Assembly.GetType("PPGTogether.BepInEx.ReplicatedObjectState", true);
            object[] layout = { identity, (uint)0, 0 };
            Check((bool)stateType.GetMethod("TryGetCachedLayout", Flags).Invoke(null, layout), "layout primed by production event path");
            Check((int)layout[2] == asset.Prefab.GetComponentsInChildren<Transform>(true).Length, "authored node count cached");
            UserSpawnEventArgs args = new UserSpawnEventArgs(instance, asset);
            MethodInfo notify = typeof(ModAPI).GetMethod("InvokeItemSpawned", Flags);
            notify.Invoke(null, new object[] { CatalogBehaviour.Main, args });
            notify.Invoke(null, new object[] { CatalogBehaviour.Main, args });
            Check(RegistryCount(registry) == before + 1, "duplicate native observation is idempotent");
            yield return null;
            Freeze(instance);
            Check(RegistryCount(registry) == before + 1, "no delayed duplicate identity");
            typeof(ModAPI).GetMethod("InvokeItemRemoved", Flags).Invoke(null, new object[] { CatalogBehaviour.Main, args });
            Check(RegistryCount(registry) == before, "durable native removal observer");
            instance.SetActive(false);
            Destroy(instance);
            yield return null;
            Check(RegistryCount(registry) == before, "destroy after removal stays idempotent");
            Report("Fixture passed: " + keys[i], false);
        }
        TestThrowingSubscriber(registry);
        yield return null;
        CheckSolo();
        Check(RegistryCount(registry) == initial, "test leaves original registry count");
    }

    private void TestThrowingSubscriber(object registry)
    {
        CheckSolo();
        const string sentinel = "Connect owned regression subscriber";
        SpawnableAsset asset = ModAPI.FindSpawnable("Metal Cube");
        GameObject created = null;
        EventHandler<UserSpawnEventArgs> throwing = delegate(object sender, UserSpawnEventArgs args)
        {
            created = args.Instance;
            owned.Add(created);
            Freeze(created);
            throw new InvalidOperationException(sentinel);
        };
        int before = RegistryCount(registry);
        SpawnableAsset previous = CatalogBehaviour.Main.SelectedItem;
        Vector3 previousMouse = Global.main.MousePosition;
        bool propagated = false;
        ModAPI.OnItemSpawned += throwing;
        try
        {
            CatalogBehaviour.Main.SelectedItem = asset;
            Global.main.MousePosition = new Vector3(84, 10, 0);
            typeof(CatalogBehaviour).GetMethod("Spawn", Flags, null, new[] { typeof(SpawnableAsset), typeof(bool) }, null)
                .Invoke(CatalogBehaviour.Main, new object[] { asset, false });
        }
        catch (TargetInvocationException error)
        {
            if (error.InnerException == null || error.InnerException.Message != sentinel) throw;
            propagated = true;
        }
        finally
        {
            ModAPI.OnItemSpawned -= throwing;
            CatalogBehaviour.Main.SelectedItem = previous;
            Global.main.MousePosition = previousMouse;
        }
        Check(propagated, "native subscriber exception is not swallowed");
        Check(((ICollection)Field(plugin, "hostCatalogSpawnKeys")).Count == 0, "Harmony finalizer clears key scope after native exception");
        Check(created != null && RegistryCount(registry) == before + 1, "prefix observer retains created root before subscriber exception");
        typeof(ModAPI).GetMethod("InvokeItemRemoved", Flags).Invoke(null, new object[] { CatalogBehaviour.Main, new UserSpawnEventArgs(created, asset) });
        created.SetActive(false);
        Destroy(created);
        Check(RegistryCount(registry) == before, "exception fixture cleanup removes only owned root");
        Report("Native throwing-subscriber / finalizer regression passed.", false);
    }

    private void Report(string value, bool error)
    {
        string line = "[HostSpawnRegression] " + value;
        File.AppendAllText(resultPath, line + Environment.NewLine);
        if (error) Logger.LogError(line); else Logger.LogInfo(line);
    }

    private void CheckSolo()
    {
        object lobby = Field(plugin, "lobby");
        Check(lobby != null, "test-owned lobby exists");
        int members = (int)lobby.GetType().GetProperty("MemberCount", Flags).GetValue(lobby, null);
        Require(members == 1, "abort: private lobby has another member");
    }
    private static object Field(object instance, string name) { return instance.GetType().GetField(name, Flags).GetValue(instance); }
    private static object Call(object instance, string name, params object[] args) { return instance.GetType().GetMethod(name, Flags).Invoke(instance, args); }
    private static int RegistryCount(object registry) { return (int)registry.GetType().GetProperty("Count", Flags).GetValue(registry, null); }
    private static HashSet<ulong> Ids(object registry)
    {
        HashSet<ulong> result = new HashSet<ulong>();
        foreach (object identity in (IEnumerable)Call(registry, "All")) result.Add((ulong)Field(identity, "NetId"));
        return result;
    }
    private static void Freeze(GameObject root)
    {
        foreach (Rigidbody2D body in root.GetComponentsInChildren<Rigidbody2D>(true))
        { body.velocity = Vector2.zero; body.angularVelocity = 0; body.bodyType = RigidbodyType2D.Kinematic; }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private void Check(bool value, string message) { Require(value, message); checks++; }
    private void Cleanup()
    {
        foreach (GameObject root in owned) if (root != null) { root.SetActive(false); Destroy(root); }
        owned.Clear();
        if (plugin != null && originalPrivacy != null) pluginType.GetField("privacy", Flags).SetValue(plugin, originalPrivacy);
        if (ownsLobby && plugin != null && Field(plugin, "lobby") != null) Call(plugin, "LeaveLobby");
    }
}
