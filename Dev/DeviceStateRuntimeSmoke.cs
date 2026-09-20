using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using PPGTogether.BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

// Compile separately from the other runtime harnesses, with its own copies of
// WorldState/ObjectState/DeviceState. NEVER ship this opt-in fixture plugin.
[BepInPlugin("local.connect.device-smoke", "Connect Device Smoke", "1.0.0")]
public sealed class DeviceStateRuntimeSmoke : BaseUnityPlugin
{
    private readonly List<GameObject> owned = new List<GameObject>();
    private int checks;
    private bool armed;

    private void Awake()
    {
        foreach (string arg in Environment.GetCommandLineArgs()) if (arg == "-connect-replication-smoke") armed = true;
        if (armed) { Logger.LogInfo("[ConnectDeviceSmoke] Armed"); StartCoroutine(Run()); }
    }

    private IEnumerator Run()
    {
        while (Global.main == null || CatalogBehaviour.Main == null || SceneManager.GetActiveScene().name != "Main") yield return null;
        yield return null;
        List<SpawnableAsset> fixtures = new List<SpawnableAsset>();
        bool light = false, flood = false;
        foreach (SpawnableAsset asset in CatalogBehaviour.Main.Catalog.Items)
        {
            if (asset == null || asset.Prefab == null || ModAPI.FindSpawnable(asset.name) != asset) continue;
            bool hasLight = asset.Prefab.GetComponentInChildren<LightSprite>(true) != null;
            bool hasFlood = asset.Prefab.GetComponentInChildren<SingleFloodlightBehaviour>(true) != null;
            if ((!light && hasLight) || (!flood && hasFlood)) { fixtures.Add(asset); light |= hasLight; flood |= hasFlood; }
            if (light && flood) break;
        }
        if (!flood) { Logger.LogError("[ConnectDeviceSmoke] FAILED: no native floodlight catalog fixture"); yield break; }
        if (!light) Logger.LogWarning("[ConnectDeviceSmoke] LightSprite has no authored catalog fixture; brightness runtime coverage unavailable (codec still covered).");
        foreach (SpawnableAsset asset in fixtures)
        {
            IEnumerator test = TestFixture(asset); bool failed = false;
            while (true)
            {
                bool next = false; object current = null;
                try { next = test.MoveNext(); if (next) current = test.Current; }
                catch (Exception error) { failed = true; Logger.LogError("[ConnectDeviceSmoke] FAILED " + asset.name + ": " + error); }
                if (failed || !next) break;
                yield return current;
            }
            Cleanup();
            if (failed) yield break;
        }
        Logger.LogInfo("[ConnectDeviceSmoke] PASSED " + checks + " assertions over " + fixtures.Count + " native catalog fixture(s). This is local capture/apply, not two-account networking.");
    }

    private IEnumerator TestFixture(SpawnableAsset asset)
    {
        Bounds bounds = Global.main.CameraControlBehaviour.BoundingBox;
        Vector3 position = bounds.center; position.z = 0;
        if (Physics2D.OverlapCircle(position, 8) != null) throw new InvalidOperationException("Test area occupied; leave existing objects untouched");
        GameObject host = Instantiate(asset.Prefab, position, Quaternion.identity) as GameObject;
        owned.Add(host); Freeze(host);
        GameObject guest = Instantiate(asset.Prefab, position + new Vector3(2, 0, 0), Quaternion.identity) as GameObject;
        owned.Add(guest); Freeze(guest);
        foreach (LightSprite ls in host.GetComponentsInChildren<LightSprite>(true)) ls.gameObject.SetActive(true);
        yield return null;
        SingleFloodlightBehaviour[] originalFloods = asset.Prefab.GetComponentsInChildren<SingleFloodlightBehaviour>(true);
        bool[] originalActivation = new bool[originalFloods.Length];
        for (int i = 0; i < originalFloods.Length; i++) originalActivation[i] = originalFloods[i].Activated;
        foreach (SingleFloodlightBehaviour lamp in host.GetComponentsInChildren<SingleFloodlightBehaviour>(true)) lamp.Activated = true;
        foreach (SingleFloodlightBehaviour lamp in guest.GetComponentsInChildren<SingleFloodlightBehaviour>(true)) lamp.Activated = false;
        foreach (LightSprite ls in host.GetComponentsInChildren<LightSprite>(true)) if (ls.SpriteRenderer != null) ls.Brightness = 7.25f;
        PPGTogetherIdentity hi = host.AddComponent<PPGTogetherIdentity>(); hi.NetId = 900000; hi.SpawnKey = asset.name;
        Check(ReplicatedObjectState.Prime(hi), "host prime");
        List<byte[]> packets = ReplicatedDeviceState.Capture(hi);
        Check(packets.Count > 0, "native device captured");
        DeviceState rejected;
        Check(DeviceStateCodec.TryDecode(packets[0], out rejected) && !ReplicatedDeviceState.Apply(hi, rejected), "host cannot be mutated by replica apply");
        ReplicatedObjectState.Forget(hi.NetId); ReplicatedDeviceState.Forget(hi.NetId);
        PPGTogetherIdentity gi = guest.AddComponent<PPGTogetherIdentity>(); gi.NetId = hi.NetId; gi.SpawnKey = asset.name; gi.ReplicatedSpawn = true;
        Check(ReplicatedObjectState.Prime(gi), "guest prime");
        foreach (byte[] packet in packets)
        {
            DeviceState state; Check(DeviceStateCodec.TryDecode(packet, out state), "decode");
            uint actualLayout = state.Layout; state.Layout ^= 1;
            Check(!ReplicatedDeviceState.Apply(gi, state), "layout mismatch rejected"); state.Layout = actualLayout;
            Check(ReplicatedDeviceState.Apply(gi, state), "on state applied");
            uint objectHash; int count;
            Check(ReplicatedObjectState.TryGetCachedLayout(gi, out objectHash, out count), "cached canonical nodes");
            foreach (DeviceNodeState node in state.Nodes)
            {
                Transform t; Check(ReplicatedObjectState.TryGetCachedTransform(gi, objectHash, node.NodeIndex, out t), "canonical device slot");
                if ((node.Kind & 2) != 0) Check(t.GetComponent<SingleFloodlightBehaviour>().Activated, "native floodlight on field synchronized");
                if ((node.Kind & 1) != 0)
                {
                    LightSprite ls = t.GetComponent<LightSprite>(); MaterialPropertyBlock block = new MaterialPropertyBlock();
                    ls.SpriteRenderer.GetPropertyBlock(block);
                    Check(Mathf.Abs(block.GetFloat(Shader.PropertyToID("_GlowIntensity")) - 7.25f) < .001f, "light local property block synchronized");
                }
                node.Activated = false;
            }
            Check(ReplicatedDeviceState.Apply(gi, state), "off state applied");
            foreach (DeviceNodeState node in state.Nodes) if ((node.Kind & 2) != 0)
            { Transform t; Check(ReplicatedObjectState.TryGetCachedTransform(gi, objectHash, node.NodeIndex, out t) && !t.GetComponent<SingleFloodlightBehaviour>().Activated, "native floodlight off field synchronized"); }
        }
        for (int i = 0; i < originalFloods.Length; i++) Check(originalFloods[i].Activated == originalActivation[i], "shared prefab unchanged");
        Logger.LogInfo("[ConnectDeviceSmoke] Fixture passed: " + asset.name);
    }

    private static void Freeze(GameObject root)
    {
        if (root == null) return;
        foreach (Rigidbody2D body in root.GetComponentsInChildren<Rigidbody2D>(true)) { body.bodyType = RigidbodyType2D.Kinematic; body.simulated = false; body.velocity = Vector2.zero; body.angularVelocity = 0; }
        foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
    }
    private void FixedUpdate() { if (armed) foreach (GameObject root in owned) Freeze(root); }
    private void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException(label); }
    private void Cleanup() { ReplicatedDeviceState.Clear(); ReplicatedObjectState.Clear(); foreach (GameObject root in owned) if (root != null) Destroy(root); owned.Clear(); }
    private void OnDestroy() { if (armed) Cleanup(); }
}
