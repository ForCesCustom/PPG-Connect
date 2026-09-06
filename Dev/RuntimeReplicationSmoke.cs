using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using PPGTogether.BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

// Opt-in developer harness. Build in a separate temporary BepInEx plugin;
// never include it in a public Connect ZIP. It instantiates only its own test
// fixtures and does not emulate a Steam session or claim a multiplayer test.
[BepInPlugin("local.connect.runtime-smoke", "Connect Runtime Smoke", "1.0.0")]
public sealed class RuntimeReplicationSmoke : BaseUnityPlugin
{
    private readonly List<GameObject> owned = new List<GameObject>();
    private int checks;
    private bool armed;

    private void Awake()
    {
        armed = Config.Bind("Test", "Enabled", false, "Run isolated fixture replication checks after a sandbox map loads. Disable after use.").Value;
        foreach (string arg in Environment.GetCommandLineArgs()) if (arg == "-connect-replication-smoke") armed = true;
        if (armed) { Logger.LogInfo("[ConnectSmoke] Armed: waiting for loaded sandbox and catalog."); StartCoroutine(Run()); }
    }

    private IEnumerator Run()
    {
        while (Global.main == null || CatalogBehaviour.Main == null || !string.Equals(SceneManager.GetActiveScene().name, "Main", StringComparison.OrdinalIgnoreCase)) yield return null;
        yield return null;
        string[] candidates = new[] { "Human", "Android", "Metal Cube" };
        int fixtures = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            SpawnableAsset asset = ModAPI.FindSpawnable(candidates[i]);
            if (asset == null || asset.Prefab == null) { Logger.LogWarning("[ConnectSmoke] Fixture unavailable: " + candidates[i]); continue; }
            IEnumerator test = TestFixture(asset, candidates[i]);
            bool failed = false;
            while (true)
            {
                bool next = false; object current = null;
                try { next = test.MoveNext(); if (next) current = test.Current; }
                catch (Exception error) { failed = true; Logger.LogError("[ConnectSmoke] FAILED " + candidates[i] + ": " + error); }
                if (failed || !next) break;
                yield return current;
            }
            Cleanup();
            if (failed) yield break;
            fixtures++;
            Logger.LogInfo("[ConnectSmoke] Fixture passed: " + candidates[i]);
            yield return null;
        }
        if (fixtures < 2) Logger.LogError("[ConnectSmoke] FAILED: expected both Human and Android fixtures; available=" + fixtures);
        else Logger.LogInfo("[ConnectSmoke] PASSED " + checks + " assertions over " + fixtures + " real Unity fixtures. This is capture/apply validation, not a two-account Steam test.");
    }

    private IEnumerator TestFixture(SpawnableAsset asset, string name)
    {
        Vector3 origin = FindEmptyTestPosition();
        Logger.LogInfo("[ConnectSmoke] In-map isolated fixture origin: " + origin);
        GameObject host = Instantiate(asset.Prefab, origin, Quaternion.identity) as GameObject;
        owned.Add(host);
        Check(host != null, "host fixture instantiation");
        Freeze(host);
        WorldRegistry hostRegistry = new WorldRegistry(); WorldRegistry replicaRegistry = new WorldRegistry();
        PPGTogetherIdentity hi = hostRegistry.RegisterHost(host, name);
        Transform[] hostNodes = host.GetComponentsInChildren<Transform>(true);
        Check(ReplicatedObjectState.Prime(hi), "host hierarchy prime");
        yield return null;
        Freeze(host);
        host.transform.position = origin + new Vector3(1, 2, 0);
        host.transform.localScale = new Vector3(-1.2f, 1.3f, 1);
        LimbBehaviour[] limbs = host.GetComponentsInChildren<LimbBehaviour>(true);
        PhysicalBehaviour[] physicals = host.GetComponentsInChildren<PhysicalBehaviour>(true);
        SpriteRenderer[] sprites = host.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (PhysicalBehaviour physical in physicals) { physical.Temperature = 240; physical.Charge = 3; physical.BurnProgress = .35f; physical.Wetness = .4f; physical.IsWeightless = true; }
        foreach (LimbBehaviour limb in limbs) { limb.Health = 8; limb.Numbness = .5f; limb.Broken = true; limb.Frozen = true; }
        foreach (SpriteRenderer sprite in sprites) { sprite.color = new Color(.3f, .4f, .7f, .8f); sprite.flipX = true; }
        foreach (SkinMaterialHandler skin in host.GetComponentsInChildren<SkinMaterialHandler>(true))
        {
            skin.damagePoints[0] = new Vector4(-.04f, .1f, .5f, 1);
            skin.damagePoints[1] = new Vector4(.08f, -.1f, .3f, 2);
            skin.damagePointTimeStamps[0] = Time.time - 5; skin.damagePointTimeStamps[1] = Time.time - 2;
            skin.currentDamagePointCount = 2; skin.TrackWoundAge = true; skin.Sync();
        }
        // Detachment must retain original slot identity. The host detached
        // limb is explicitly tracked for teardown outside the owned root.
        if (limbs.Length > 2)
        {
            Transform detach = limbs[limbs.Length - 1].transform;
            detach.SetParent(null, true); owned.Add(detach.gameObject);
            detach.position = origin + new Vector3(4, 3, 0);
            Check(ReplicatedObjectState.FindIdentity(detach) == hi, "detached limb retains identity");
            Check(Array.IndexOf(ReplicatedObjectState.GetPhysicalParts(hi), detach.GetComponent<PhysicalBehaviour>()) >= 0, "detached limb remains grabbable physical part");
            // Destroy a different owned limb after priming to test that later
            // snapshot slots are not shifted when a native part disappears.
            Destroy(limbs[limbs.Length - 2].gameObject);
        }
        yield return null;
        List<byte[]> packets = ReplicatedObjectState.Capture(hi);
        List<byte[]> woundPackets = ReplicatedWoundState.Capture(hi);
        Check(packets.Count > 0, "capture packets");
        // A new client starts from the original prefab; it must Prime before
        // Start adds runtime visuals, matching production spawn integration.
        ReplicatedObjectState.Forget(hi.NetId);
        GameObject replica = Instantiate(asset.Prefab, origin + new Vector3(-4, 0, 0), Quaternion.identity) as GameObject;
        owned.Add(replica); Check(replica != null, "replica fixture instantiation"); Freeze(replica);
        PPGTogetherIdentity ri = replicaRegistry.RegisterReplica(replica, hi.NetId, name);
        Transform[] replicaNodes = replica.GetComponentsInChildren<Transform>(true);
        Check(hostNodes.Length == replicaNodes.Length, "initial clone hierarchy parity");
        Check(ReplicatedObjectState.Prime(ri), "replica apply hierarchy prime");
        yield return null;
        ObjectStateChunk first = null;
        foreach (byte[] packet in packets)
        {
            ObjectStateChunk chunk;
            Check(ObjectStateCodec.TryDecode(packet, out chunk), "runtime packet parse");
            if (first == null) first = chunk;
            Check(ReplicatedObjectState.Apply(ri, chunk), "runtime packet application, prefab layout must match");
            AssertChunk(replicaNodes, chunk);
        }
        foreach (byte[] packet in woundPackets)
        {
            WoundState wound; Check(WoundStateCodec.TryDecode(packet, out wound), "wound packet parse");
            Check(ReplicatedWoundState.Apply(ri, wound), "wound shader application");
            SkinMaterialHandler skin = replicaNodes[wound.NodeIndex].GetComponent<SkinMaterialHandler>();
            Check(skin != null && skin.currentDamagePointCount == wound.Points.Length, "wound count parity");
            for (int i = 0; i < wound.Points.Length; i++)
            {
                WoundPointState point = wound.Points[i]; Vector4 actual = skin.damagePoints[i];
                Check(Mathf.Abs(actual.x - point.X) < .001f && Mathf.Abs(actual.y - point.Y) < .001f && Mathf.Abs(actual.z - point.Intensity) < .001f && actual.w == point.Kind, "bullet/stab shader point parity");
                Check(Mathf.Abs(Time.time - skin.damagePointTimeStamps[i] - point.Age) < .01f, "wound age parity");
            }
            Vector4 beforePoint = skin.damagePoints[0];
            WoundPointState[] original = wound.Points; wound.Points = new[] { new WoundPointState { Kind = 6 } };
            Check(!ReplicatedWoundState.Apply(ri, wound) && skin.damagePoints[0] == beforePoint, "invalid wound cannot partially modify shader state");
            wound.Points = new WoundPointState[0];
            Check(ReplicatedWoundState.Apply(ri, wound) && skin.currentDamagePointCount == 0 && skin.damagePoints[0].w == 5, "healed wounds clear old shader points");
            wound.Points = original;
        }
        Vector3 before = replica.transform.position;
        byte[] malformed = (byte[])packets[0].Clone();
        if (malformed.Length > 21) Array.Copy(BitConverter.GetBytes(float.NaN), 0, malformed, 18, 4);
        ObjectStateChunk invalid;
        Check(!ObjectStateCodec.TryDecode(malformed, out invalid), "invalid runtime payload rejected");
        Check(replica.transform.position == before, "parse rejection leaves world unchanged");
        byte[] truncated = new byte[packets[0].Length - 1]; Array.Copy(packets[0], truncated, truncated.Length);
        Check(!ObjectStateCodec.TryDecode(truncated, out invalid), "truncated runtime payload rejected");
        byte originalFlags = first.Nodes[first.Nodes.Length - 1].Flags;
        first.Nodes[0].X += 20;
        first.Nodes[first.Nodes.Length - 1].Flags ^= 4;
        Check(!ReplicatedObjectState.Apply(ri, first), "mismatched final component rejects entire chunk");
        Check(replica.transform.position == before, "no partial application on last-node mismatch");
        first.Nodes[first.Nodes.Length - 1].Flags = originalFlags;
        // Missing child semantics: a host-absent cached node must disappear
        // without deleting the root or changing a later node's slot.
        if (first.Nodes.Length > 1)
        {
            int missing = first.Nodes.Length - 1;
            first.Nodes[missing] = new ObjectStateNode();
            first.Nodes[0].X = before.x;
            Check(ReplicatedObjectState.Apply(ri, first), "missing child tombstone application");
            Check(!replicaNodes[missing].gameObject.activeSelf, "missing child hidden");
            Check(replica != null && replica.activeSelf, "missing child preserves root");
        }
        Logger.LogInfo("[ConnectSmoke] " + name + ": initial nodes=" + hostNodes.Length + ", chunks=" + packets.Count + ", physicals=" + physicals.Length + ", limbs=" + limbs.Length);
    }

    private void AssertChunk(Transform[] nodes, ObjectStateChunk chunk)
    {
        for (int i = 0; i < chunk.Nodes.Length; i++)
        {
            ObjectStateNode state = chunk.Nodes[i]; Transform t = nodes[chunk.Offset + i];
            if ((state.Flags & 1) == 0) { Check(!t.gameObject.activeSelf, "absent part inactive"); continue; }
            Check(Vector3.Distance(t.position, new Vector3(state.X, state.Y, state.Z)) < .01f, "world pose parity");
            Check(Vector3.Distance(t.localScale, new Vector3(state.ScaleX, state.ScaleY, state.ScaleZ)) < .001f, "scale parity");
            Rigidbody2D body = t.GetComponent<Rigidbody2D>(); if (body != null) Check(body.bodyType == RigidbodyType2D.Kinematic, "replica simulation suppressed");
            PhysicalBehaviour physical = t.GetComponent<PhysicalBehaviour>(); if (physical != null) Check(Mathf.Abs(physical.Temperature - state.Temperature) < .01f && Mathf.Abs(physical.BurnProgress - state.Burn) < .001f && physical.IsWeightless == ((state.PhysicalFlags & 1) != 0), "physical state parity");
            LimbBehaviour limb = t.GetComponent<LimbBehaviour>(); if (limb != null) Check(Mathf.Abs(limb.Health - state.Health) < .001f && limb.Broken == ((state.LimbFlags & 1) != 0), "limb state parity");
            SpriteRenderer sprite = t.GetComponent<SpriteRenderer>(); if (sprite != null) Check(Mathf.Abs(sprite.color.r - state.Red) < .001f && sprite.flipX == ((state.SpriteFlags & 2) != 0), "sprite state parity");
        }
    }

    private static Vector3 FindEmptyTestPosition()
    {
        if (Global.main == null || Global.main.CameraControlBehaviour == null) throw new InvalidOperationException("Loaded map bounds unavailable");
        Bounds bounds = Global.main.CameraControlBehaviour.BoundingBox;
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
            {
                Vector3 p = bounds.center + new Vector3((x - 2) * bounds.extents.x * .25f, (y - 2) * bounds.extents.y * .25f, 0);
                p.z = 0;
                if (Physics2D.OverlapCircle(new Vector2(p.x, p.y), 12) == null) return p;
            }
        throw new InvalidOperationException("No empty in-map area for isolated fixtures; existing objects were left untouched");
    }

    private static void Freeze(GameObject root)
    {
        foreach (Rigidbody2D body in root.GetComponentsInChildren<Rigidbody2D>(true)) { body.bodyType = RigidbodyType2D.Kinematic; body.simulated = false; body.velocity = Vector2.zero; body.angularVelocity = 0; }
        foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
    }

    private void FixedUpdate() { if (armed) foreach (GameObject root in owned) if (root != null) Freeze(root); }
    private void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException(label); }
    private void Cleanup() { ReplicatedObjectState.Clear(); foreach (GameObject obj in owned) if (obj != null) Destroy(obj); owned.Clear(); }
    private void OnDestroy() { if (armed) Cleanup(); }
}
