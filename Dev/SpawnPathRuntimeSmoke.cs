using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.SceneManagement;

// Developer-only opt-in harness. Compile alongside RuntimeReplicationSmoke
// into a temporary plugin; do not ship this file's DLL in a release archive.
[BepInPlugin("local.connect.spawn-path-smoke", "Connect Spawn Path Smoke", "1.0.0")]
[BepInDependency("local.ppgtogether.steam")]
public sealed class SpawnPathRuntimeSmoke : BaseUnityPlugin
{
    private readonly List<GameObject> fixtures = new List<GameObject>();
    private int checks;
    private bool armed;

    private void Awake()
    {
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg == "-connect-replication-smoke") armed = true;
        if (!armed) return;
        Logger.LogInfo("[ConnectSpawnSmoke] Armed: waiting for Main sandbox and real Connect instance.");
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        while (Global.main == null || CatalogBehaviour.Main == null || !string.Equals(SceneManager.GetActiveScene().name, "Main", StringComparison.OrdinalIgnoreCase)) yield return null;
        yield return null;
        PluginInfo info;
        if (!Chainloader.PluginInfos.TryGetValue("local.ppgtogether.steam", out info) || info.Instance == null)
        { Logger.LogError("[ConnectSpawnSmoke] FAILED: production Connect plugin not loaded."); yield break; }
        object connect = info.Instance;
        Type type = connect.GetType();
        MethodInfo spawn = type.GetMethod("SpawnAsset", BindingFlags.Instance | BindingFlags.NonPublic, null,
            new Type[] { typeof(SpawnableAsset), typeof(Vector2), typeof(float), typeof(bool) }, null);
        FieldInfo singleton = type.GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo lobby = type.GetField("lobby", BindingFlags.Instance | BindingFlags.NonPublic);
        if (spawn == null || singleton == null || !ReferenceEquals(singleton.GetValue(null), connect) || lobby == null || lobby.GetValue(connect) != null)
        { Logger.LogError("[ConnectSpawnSmoke] FAILED: exact production spawn path unavailable or a Steam lobby is active; test requires an isolated sandbox."); yield break; }

        string[] selected = new string[] { "Human", "Android", "Metal Cube" };
        string[] requested = new string[] { "Android", "Metal Cube", "Human" };
        int passed = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            for (int flip = 0; flip < 2; flip++)
            {
                bool failed = false;
                try
                {
                    Check(lobby.GetValue(connect) == null, "test remains outside a lobby");
                    TestSpawn(connect, spawn, selected[i], requested[i], flip != 0);
                }
                catch (Exception error)
                {
                    failed = true;
                    Logger.LogError("[ConnectSpawnSmoke] FAILED selected=" + selected[i] + " requested=" + requested[i] + " flipped=" + (flip != 0) + ": " + error);
                }
                // Let native Start run with all owned bodies/colliders already
                // disabled, then destroy only the exact returned fixtures.
                yield return null;
                Cleanup();
                yield return null;
                if (failed) yield break;
                passed++;
            }
        }
        Logger.LogInfo("[ConnectSpawnSmoke] PASSED " + checks + " assertions across " + passed + " real production SpawnAsset calls. Different catalog selections, requested prefabs, flips, pose and selection restoration verified; no Steam session was emulated.");
    }

    private void TestSpawn(object connect, MethodInfo spawn, string selectedName, string requestedName, bool flipped)
    {
        SpawnableAsset selected = ModAPI.FindSpawnable(selectedName);
        SpawnableAsset requested = ModAPI.FindSpawnable(requestedName);
        Check(selected != null && requested != null && selected != requested && requested.Prefab != null, "distinct catalog fixtures available");
        CatalogBehaviour catalog = CatalogBehaviour.Main;
        SpawnableAsset originalSelection = catalog.SelectedItem;
        Vector2 position = FindEmptyPosition();
        float rotation = flipped ? 15f : 0f;
        try
        {
            catalog.SelectedItem = selected;
            object returned = spawn.Invoke(connect, new object[] { requested, position, rotation, flipped });
            GameObject created = returned as GameObject;
            if (created != null) { fixtures.Add(created); Freeze(created); }
            Check(created != null, "production spawn returns a real GameObject");
            Check(ReferenceEquals(catalog.SelectedItem, selected), "production scope restores deliberately different selection");
            Check(created.name.Replace("(Clone)", string.Empty).Trim() == requested.Prefab.name, "requested prefab name, not selected prefab");
            Check(created.GetComponentsInChildren<PhysicalBehaviour>(true).Length == requested.Prefab.GetComponentsInChildren<PhysicalBehaviour>(true).Length, "requested physical-body count");
            Check(created.GetComponentsInChildren<LimbBehaviour>(true).Length == requested.Prefab.GetComponentsInChildren<LimbBehaviour>(true).Length, "requested limb count");
            Check(Vector2.Distance(new Vector2(created.transform.position.x, created.transform.position.y), position) < .01f, "requested world position");
            Check(Mathf.Abs(Mathf.DeltaAngle(created.transform.eulerAngles.z, rotation)) < .01f, "requested rotation");
            float expectedX = requested.Prefab.transform.localScale.x * (flipped ? -1f : 1f);
            Check(Mathf.Abs(created.transform.localScale.x - expectedX) < .001f, "requested horizontal flip");
            int spriteChecks = 0;
            foreach (SpriteRenderer reference in requested.Prefab.GetComponentsInChildren<SpriteRenderer>(true))
            {
                string path = RelativePath(requested.Prefab.transform, reference.transform);
                Transform replicaNode = path.Length == 0 ? created.transform : created.transform.Find(path);
                Check(replicaNode != null, "requested sprite hierarchy " + path);
                SpriteRenderer actual = replicaNode.GetComponent<SpriteRenderer>();
                Check(actual != null && actual.sprite == reference.sprite, "requested sprite asset " + path);
                spriteChecks++;
            }
            Check(spriteChecks > 0, "fixture has verifiable visible sprites");
            Logger.LogInfo("[ConnectSpawnSmoke] PASS selected=" + selectedName + " requested=" + requestedName + " flipped=" + flipped + " created=" + created.name + " sprites=" + spriteChecks + " at=" + position + " selection restored.");
        }
        finally { if (catalog != null) catalog.SelectedItem = originalSelection; }
        Check(catalog == null || ReferenceEquals(catalog.SelectedItem, originalSelection), "harness restores user's original selection");
    }

    private static string RelativePath(Transform root, Transform child)
    {
        string result = string.Empty;
        while (child != root && child != null)
        {
            result = result.Length == 0 ? child.name : child.name + "/" + result;
            child = child.parent;
        }
        if (child != root) throw new InvalidOperationException("Sprite outside fixture prefab");
        return result;
    }

    private static Vector2 FindEmptyPosition()
    {
        if (Global.main.CameraControlBehaviour == null) throw new InvalidOperationException("Map bounds unavailable");
        Bounds bounds = Global.main.CameraControlBehaviour.BoundingBox;
        for (int y = 4; y >= 0; y--)
            for (int x = 4; x >= 0; x--)
            {
                Vector2 point = new Vector2(bounds.center.x + (x - 2) * bounds.extents.x * .25f, bounds.center.y + (y - 2) * bounds.extents.y * .25f);
                if (Physics2D.OverlapCircle(point, 12f) == null) return point;
            }
        throw new InvalidOperationException("No clear in-map area for isolated spawn fixtures; existing objects untouched");
    }

    private static void Freeze(GameObject root)
    {
        foreach (Rigidbody2D body in root.GetComponentsInChildren<Rigidbody2D>(true))
        { body.bodyType = RigidbodyType2D.Kinematic; body.simulated = false; body.velocity = Vector2.zero; body.angularVelocity = 0f; }
        foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
    }
    private void FixedUpdate() { if (armed) foreach (GameObject fixture in fixtures) if (fixture != null) Freeze(fixture); }
    private void Check(bool condition, string label) { checks++; if (!condition) throw new InvalidOperationException(label); }
    private void Cleanup() { foreach (GameObject fixture in fixtures) if (fixture != null) Destroy(fixture); fixtures.Clear(); }
    private void OnDestroy() { if (armed) Cleanup(); }
}
