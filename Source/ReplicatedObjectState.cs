using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPGTogether.BepInEx
{
    // A deliberately closed schema: remote data never names a CLR type, method,
    // asset path or serializer. Each chunk is validated in full before application.
    internal sealed class ObjectStateNode
    {
        internal byte Flags; // present, active, body, sprite, physical, limb, skin
        internal byte Layer, ColliderCount, PhysicalFlags;
        internal uint ColliderMask;
        internal float X, Y, Z, Angle, ScaleX = 1, ScaleY = 1, ScaleZ = 1;
        internal float Red = 1, Green = 1, Blue = 1, Alpha = 1;
        internal byte SpriteFlags; // enabled, flipX, flipY
        internal float Temperature, Charge, Burn, BurnIntensity, Wetness;
        internal bool Fire;
        internal float Health, Numbness, BodyTemperature;
        internal byte LimbFlags; // broken, frozen, dismembered, joint still exists
        internal float Rot, Acid;
    }

    internal sealed class ObjectStateChunk
    {
        internal ulong NetId;
        internal uint Layout;
        internal ushort Total, Offset;
        internal ObjectStateNode[] Nodes;
    }

    internal static class ObjectStateCodec
    {
        internal const int MaximumNodes = 256;
        internal const int NodesPerChunk = 24;
        internal const int MaximumChunkBytes = 4096;

        internal static byte[] Encode(ObjectStateChunk value)
        {
            if (!Valid(value)) throw new ArgumentException("Invalid replicated object state");
            Writer w = new Writer(MaximumChunkBytes);
            w.ULong(value.NetId); w.UInt(value.Layout); w.UShort(value.Total); w.UShort(value.Offset); w.Byte((byte)value.Nodes.Length);
            foreach (ObjectStateNode n in value.Nodes)
            {
                w.Byte(n.Flags);
                if ((n.Flags & 1) == 0) continue;
                w.Float(n.X); w.Float(n.Y); w.Float(n.Z); w.Float(n.Angle);
                w.Float(n.ScaleX); w.Float(n.ScaleY); w.Float(n.ScaleZ);
                w.Byte(n.Layer); w.Byte(n.ColliderCount); w.UInt(n.ColliderMask);
                if ((n.Flags & 8) != 0) { w.Float(n.Red); w.Float(n.Green); w.Float(n.Blue); w.Float(n.Alpha); w.Byte(n.SpriteFlags); }
                if ((n.Flags & 16) != 0) { w.Float(n.Temperature); w.Float(n.Charge); w.Float(n.Burn); w.Float(n.BurnIntensity); w.Float(n.Wetness); w.Bool(n.Fire); w.Byte(n.PhysicalFlags); }
                if ((n.Flags & 32) != 0) { w.Float(n.Health); w.Float(n.Numbness); w.Float(n.BodyTemperature); w.Byte(n.LimbFlags); }
                if ((n.Flags & 64) != 0) { w.Float(n.Rot); w.Float(n.Acid); }
            }
            byte[] result = w.ToArray();
            if (result.Length > MaximumChunkBytes) throw new ArgumentException("Object state chunk too large");
            return result;
        }

        internal static bool TryDecode(byte[] bytes, out ObjectStateChunk value)
        {
            value = null;
            if (bytes == null || bytes.Length > MaximumChunkBytes || bytes.Length < 18) return false;
            Reader r = new Reader(bytes);
            ObjectStateChunk candidate = new ObjectStateChunk(); byte count;
            if (!r.ULong(out candidate.NetId) || !r.UInt(out candidate.Layout) || !r.UShort(out candidate.Total) || !r.UShort(out candidate.Offset) || !r.Byte(out count)) return false;
            if (candidate.NetId == 0 || candidate.Total == 0 || candidate.Total > MaximumNodes || count == 0 || count > NodesPerChunk || candidate.Offset + count > candidate.Total) return false;
            candidate.Nodes = new ObjectStateNode[count];
            for (int i = 0; i < count; i++)
            {
                ObjectStateNode n = new ObjectStateNode(); candidate.Nodes[i] = n;
                if (!r.Byte(out n.Flags) || n.Flags > 127) return false;
                if ((n.Flags & 1) == 0) { if (n.Flags != 0) return false; continue; }
                if (!r.Float(out n.X) || !r.Float(out n.Y) || !r.Float(out n.Z) || !r.Float(out n.Angle) || !r.Float(out n.ScaleX) || !r.Float(out n.ScaleY) || !r.Float(out n.ScaleZ)) return false;
                if (!r.Byte(out n.Layer) || !r.Byte(out n.ColliderCount) || !r.UInt(out n.ColliderMask)) return false;
                if ((n.Flags & 8) != 0 && (!r.Float(out n.Red) || !r.Float(out n.Green) || !r.Float(out n.Blue) || !r.Float(out n.Alpha) || !r.Byte(out n.SpriteFlags))) return false;
                if ((n.Flags & 16) != 0 && (!r.Float(out n.Temperature) || !r.Float(out n.Charge) || !r.Float(out n.Burn) || !r.Float(out n.BurnIntensity) || !r.Float(out n.Wetness) || !r.Bool(out n.Fire) || !r.Byte(out n.PhysicalFlags))) return false;
                if ((n.Flags & 32) != 0 && (!r.Float(out n.Health) || !r.Float(out n.Numbness) || !r.Float(out n.BodyTemperature) || !r.Byte(out n.LimbFlags))) return false;
                if ((n.Flags & 64) != 0 && (!r.Float(out n.Rot) || !r.Float(out n.Acid))) return false;
            }
            if (r.Remaining != 0 || !Valid(candidate)) return false;
            value = candidate; return true;
        }

        private static bool InRange(float value, float low, float high) { return !float.IsNaN(value) && !float.IsInfinity(value) && value >= low && value <= high; }
        internal static bool Valid(ObjectStateChunk value)
        {
            if (value == null || value.NetId == 0 || value.Total == 0 || value.Total > MaximumNodes || value.Nodes == null || value.Nodes.Length == 0 || value.Nodes.Length > NodesPerChunk || value.Offset + value.Nodes.Length > value.Total) return false;
            foreach (ObjectStateNode n in value.Nodes)
            {
                if (n == null || n.Flags > 127 || ((n.Flags & 1) == 0 && n.Flags != 0)) return false;
                if (n.Flags == 0) continue;
                if (n.Layer > 31 || n.ColliderCount > 32 || (n.ColliderCount < 32 && (n.ColliderMask >> n.ColliderCount) != 0)) return false;
                if (!InRange(n.X, -1000000, 1000000) || !InRange(n.Y, -1000000, 1000000) || !InRange(n.Z, -1000000, 1000000) || !InRange(n.Angle, -360, 360) || !InRange(n.ScaleX, -10000, 10000) || !InRange(n.ScaleY, -10000, 10000) || !InRange(n.ScaleZ, -10000, 10000)) return false;
                if ((n.Flags & 8) != 0 && (n.SpriteFlags > 7 || !InRange(n.Red, 0, 32) || !InRange(n.Green, 0, 32) || !InRange(n.Blue, 0, 32) || !InRange(n.Alpha, 0, 1))) return false;
                if ((n.Flags & 16) != 0 && (n.PhysicalFlags > 1 || !InRange(n.Temperature, -1000000, 1000000000) || !InRange(n.Charge, -1000000000, 1000000000) || !InRange(n.Burn, 0, 1) || !InRange(n.BurnIntensity, 0, 1000000) || !InRange(n.Wetness, 0, 1000000))) return false;
                if ((n.Flags & 32) != 0 && (n.LimbFlags > 15 || !InRange(n.Health, -1000000000, 1000000000) || !InRange(n.Numbness, 0, 1000000) || !InRange(n.BodyTemperature, -1000000, 1000000000))) return false;
                if ((n.Flags & 64) != 0 && (!InRange(n.Rot, 0, 1) || !InRange(n.Acid, 0, 1))) return false;
            }
            return true;
        }
    }

    // Pure helpers are shared by the Unity mapper and executable regression
    // tests; unrelated runtime siblings never participate in authored ordinals.
    internal static class AuthoredNodePaths
    {
        internal static string Segment(string name, int ordinal)
        {
            name = name ?? string.Empty;
            return "/" + name.Length + ":" + name + ":" + ordinal;
        }

        internal static int MatchChild(string name, int ordinal, int authoredCount, string[] actualNames)
        {
            int count = 0, match = -1;
            for (int i = 0; i < actualNames.Length; i++)
                if (string.Equals(actualNames[i], name, StringComparison.Ordinal))
                {
                    if (count == ordinal) match = i;
                    count++;
                }
            // If a same-name sibling was removed, assigning the next sibling
            // to its old slot corrupts identities. Fail closed for that group.
            return count == authoredCount ? match : -1;
        }
    }

    internal static class ReplicatedObjectState
    {
        private sealed class Node
        {
            internal Transform Transform;
            internal Rigidbody2D Body;
            internal SpriteRenderer Sprite;
            internal PhysicalBehaviour Physical;
            internal LimbBehaviour Limb;
            internal SkinMaterialHandler Skin;
            internal Collider2D[] Colliders;
            internal int InstanceId;
            internal byte Schema;
        }
        private sealed class Layout
        {
            internal PPGTogetherIdentity Identity;
            internal Node[] Nodes;
            internal uint Hash;
        }
        private static readonly Dictionary<ulong, Layout> layouts = new Dictionary<ulong, Layout>();
        private static readonly HashSet<int> replicaObjects = new HashSet<int>();
        private static readonly Dictionary<int, PPGTogetherIdentity> nodeOwners = new Dictionary<int, PPGTogetherIdentity>();

        // Native spawning and lifecycle recovery do not run at the same point in
        // Awake/Start on every peer. Only the local catalogue prefab defines wire
        // slots; runtime outlines, fire particles and gore must never add slots.
        // Retain the mapped references so later detached limbs keep their slots.
        internal static bool Prime(PPGTogetherIdentity identity)
        {
            if (identity == null || identity.NetId == 0) return false;
            Layout existing;
            if (layouts.TryGetValue(identity.NetId, out existing))
            {
                if (existing.Identity == identity) return true;
                if (existing.Identity != null) return false;
                Forget(identity.NetId);
            }
            SpawnableAsset asset = string.IsNullOrEmpty(identity.SpawnKey) ? null : ModAPI.FindSpawnable(identity.SpawnKey);
            if (asset == null || asset.Prefab == null) return false;
            Transform[] authored = asset.Prefab.GetComponentsInChildren<Transform>(true), transforms;
            if (!TryGetAuthoredTransforms(identity.transform, asset.Prefab.transform, out transforms)) return false;
            Layout layout = new Layout(); layout.Identity = identity; layout.Hash = 2166136261; layout.Nodes = new Node[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform t = transforms[i]; Node n = new Node(); layout.Nodes[i] = n;
                Transform template = authored[i];
                n.Transform = t;
                // The schema is authored as well: native effects may attach
                // components to an existing transform during Awake/Start.
                n.Schema = SchemaOf(template);
                if (t != null)
                {
                    n.Body = (n.Schema & 4) != 0 ? t.GetComponent<Rigidbody2D>() : null;
                    n.Sprite = (n.Schema & 8) != 0 ? t.GetComponent<SpriteRenderer>() : null;
                    n.Physical = (n.Schema & 16) != 0 ? t.GetComponent<PhysicalBehaviour>() : null;
                    n.Limb = (n.Schema & 32) != 0 ? t.GetComponent<LimbBehaviour>() : null;
                    n.Skin = (n.Schema & 64) != 0 ? t.GetComponent<SkinMaterialHandler>() : null;
                    n.InstanceId = t.gameObject.GetInstanceID();
                }
                Collider2D[] authoredColliders = template.GetComponents<Collider2D>();
                if (authoredColliders.Length > 32) return false;
                n.Colliders = MapAuthoredColliders(t, authoredColliders);
                // No remote path is ever resolved in the live hierarchy. Its
                // canonical fingerprint also ignores added sibling indices.
                string path = InitialPath(asset.Prefab.transform, template);
                for (int j = 0; j < path.Length; j++) layout.Hash = unchecked((layout.Hash ^ path[j]) * 16777619);
                layout.Hash = unchecked((layout.Hash ^ n.Schema) * 16777619);
                layout.Hash = unchecked((layout.Hash ^ (uint)n.Colliders.Length) * 16777619);
            }
            if (identity.ReplicatedSpawn) foreach (Node node in layout.Nodes) if (node.Transform != null) replicaObjects.Add(node.InstanceId);
            foreach (Node node in layout.Nodes) if (node.Transform != null) nodeOwners[node.InstanceId] = identity;
            layouts.Add(identity.NetId, layout); return true;
        }

        private static string InitialPath(Transform root, Transform child)
        {
            string path = string.Empty;
            while (child != root && child != null) { path = AuthoredNodePaths.Segment(child.name, SameNameOrdinal(child)) + path; child = child.parent; }
            return path.Length == 0 ? "/" : path;
        }

        private static int SameNameOrdinal(Transform child)
        {
            if (child.parent == null) return 0;
            int ordinal = 0;
            for (int i = 0; i < child.GetSiblingIndex(); i++)
                if (string.Equals(child.parent.GetChild(i).name, child.name, StringComparison.Ordinal)) ordinal++;
            return ordinal;
        }

        // Exposed internally for an in-engine regression fixture. Mapping is
        // entirely local and includes inactive and non-physical authored nodes.
        // An absent authored child remains null at its original slot rather than
        // shifting later nodes onto a different limb. This recovers old roots
        // safely; a limb detached before registration cannot be rediscovered.
        internal static bool TryGetAuthoredTransforms(Transform instance, Transform prefab, out Transform[] mapped)
        {
            mapped = null;
            if (instance == null || prefab == null) return false;
            Transform[] authored = prefab.GetComponentsInChildren<Transform>(true);
            if (authored.Length == 0 || authored.Length > ObjectStateCodec.MaximumNodes) return false;
            mapped = new Transform[authored.Length]; mapped[0] = instance;
            Dictionary<Transform, Transform> parents = new Dictionary<Transform, Transform>();
            parents[prefab] = instance;
            for (int i = 1; i < authored.Length; i++)
            {
                Transform source = authored[i], parent;
                if (!parents.TryGetValue(source.parent, out parent) || parent == null) { parents[source] = null; continue; }
                int authoredCount = 0;
                for (int j = 0; j < source.parent.childCount; j++)
                    if (string.Equals(source.parent.GetChild(j).name, source.name, StringComparison.Ordinal)) authoredCount++;
                string[] actualNames = new string[parent.childCount];
                for (int j = 0; j < actualNames.Length; j++) actualNames[j] = parent.GetChild(j).name;
                int match = AuthoredNodePaths.MatchChild(source.name, SameNameOrdinal(source), authoredCount, actualNames);
                if (match >= 0) mapped[i] = parent.GetChild(match);
                parents[source] = mapped[i];
            }
            return true;
        }

        private static byte SchemaOf(Transform t)
        {
            return (byte)((t.GetComponent<Rigidbody2D>() != null ? 4 : 0) | (t.GetComponent<SpriteRenderer>() != null ? 8 : 0) |
                (t.GetComponent<PhysicalBehaviour>() != null ? 16 : 0) | (t.GetComponent<LimbBehaviour>() != null ? 32 : 0) | (t.GetComponent<SkinMaterialHandler>() != null ? 64 : 0));
        }

        private static Collider2D[] MapAuthoredColliders(Transform instance, Collider2D[] authored)
        {
            Collider2D[] result = new Collider2D[authored.Length];
            if (instance == null) return result;
            Collider2D[] actual = instance.GetComponents<Collider2D>();
            bool[] used = new bool[actual.Length];
            for (int i = 0; i < authored.Length; i++)
                for (int j = 0; j < actual.Length; j++)
                    if (!used[j] && actual[j] != null && authored[i].GetType() == actual[j].GetType())
                    { result[i] = actual[j]; used[j] = true; break; }
            return result;
        }

        internal static bool IsReplicaComponent(Component component)
        {
            return component != null && replicaObjects.Contains(component.gameObject.GetInstanceID());
        }

        internal static PPGTogetherIdentity FindIdentity(Component component)
        {
            if (component == null) return null;
            PPGTogetherIdentity identity;
            if (nodeOwners.TryGetValue(component.gameObject.GetInstanceID(), out identity) && identity != null) return identity;
            return component.GetComponentInParent<PPGTogetherIdentity>();
        }

        internal static PhysicalBehaviour[] GetPhysicalParts(PPGTogetherIdentity identity)
        {
            Layout layout;
            if (identity == null) return new PhysicalBehaviour[0];
            if (!layouts.TryGetValue(identity.NetId, out layout) || layout.Identity != identity) return identity.GetComponentsInChildren<PhysicalBehaviour>(true);
            List<PhysicalBehaviour> result = new List<PhysicalBehaviour>();
            foreach (Node node in layout.Nodes) if (node.Physical != null) result.Add(node.Physical);
            return result.ToArray();
        }

        internal static bool TryGetCachedLayout(PPGTogetherIdentity identity, out uint hash, out int count)
        {
            hash = 0; count = 0; Layout layout;
            if (identity == null || !layouts.TryGetValue(identity.NetId, out layout) || layout.Identity != identity) return false;
            hash = layout.Hash; count = layout.Nodes.Length; return true;
        }

        internal static bool TryGetCachedSkin(PPGTogetherIdentity identity, uint hash, ushort index, out SkinMaterialHandler skin)
        {
            skin = null; Layout layout;
            if (identity == null || !layouts.TryGetValue(identity.NetId, out layout) || layout.Identity != identity || layout.Hash != hash || index >= layout.Nodes.Length) return false;
            skin = layout.Nodes[index].Skin; return skin != null;
        }

        internal static void Forget(ulong netId)
        {
            Layout layout;
            if (layouts.TryGetValue(netId, out layout))
                foreach (Node node in layout.Nodes)
                {
                    replicaObjects.Remove(node.InstanceId);
                    PPGTogetherIdentity owner;
                    if (nodeOwners.TryGetValue(node.InstanceId, out owner) && ReferenceEquals(owner, layout.Identity)) nodeOwners.Remove(node.InstanceId);
                }
            layouts.Remove(netId);
        }

        // Destroy(root) covers the original hierarchy. Clean up only cached
        // replica parts that native scripts may have detached since spawning;
        // never act on host objects or discover unrelated scene objects.
        internal static void DestroyReplicaParts(ulong netId)
        {
            Layout layout;
            if (!layouts.TryGetValue(netId, out layout) || layout.Identity == null || !layout.Identity.ReplicatedSpawn) return;
            Transform root = layout.Identity.transform;
            foreach (Node node in layout.Nodes)
                if (node.Transform != null && node.Transform != root && !node.Transform.IsChildOf(root))
                    UnityEngine.Object.Destroy(node.Transform.gameObject);
        }

        internal static void Clear() { layouts.Clear(); replicaObjects.Clear(); nodeOwners.Clear(); }

        internal static List<byte[]> Capture(PPGTogetherIdentity identity)
        {
            List<byte[]> chunks = new List<byte[]>();
            if (!Prime(identity)) return chunks;
            Layout layout = layouts[identity.NetId];
            for (int offset = 0; offset < layout.Nodes.Length; offset += ObjectStateCodec.NodesPerChunk)
            {
                ObjectStateChunk chunk = new ObjectStateChunk(); chunk.NetId = identity.NetId; chunk.Layout = layout.Hash; chunk.Total = (ushort)layout.Nodes.Length; chunk.Offset = (ushort)offset;
                chunk.Nodes = new ObjectStateNode[Math.Min(ObjectStateCodec.NodesPerChunk, layout.Nodes.Length - offset)];
                for (int i = 0; i < chunk.Nodes.Length; i++) chunk.Nodes[i] = CaptureNode(layout.Nodes[offset + i]);
                chunks.Add(ObjectStateCodec.Encode(chunk));
            }
            return chunks;
        }

        private static float Safe(float v, float min, float max) { return float.IsNaN(v) || float.IsInfinity(v) ? 0 : Mathf.Clamp(v, min, max); }
        private static ObjectStateNode CaptureNode(Node node)
        {
            ObjectStateNode n = new ObjectStateNode(); Transform t = node.Transform;
            if (t == null) return n;
            n.Flags = (byte)(1 | (t.gameObject.activeSelf ? 2 : 0) | node.Schema);
            n.Layer = (byte)t.gameObject.layer; n.ColliderCount = (byte)node.Colliders.Length;
            for (int i = 0; i < node.Colliders.Length; i++) if (node.Colliders[i] != null && node.Colliders[i].enabled) n.ColliderMask |= 1u << i;
            Vector3 p = t.position, s = t.localScale;
            n.X = Safe(p.x, -1000000, 1000000); n.Y = Safe(p.y, -1000000, 1000000); n.Z = Safe(p.z, -1000000, 1000000); n.Angle = Safe(Mathf.DeltaAngle(0, t.eulerAngles.z), -360, 360);
            n.ScaleX = Safe(s.x, -10000, 10000); n.ScaleY = Safe(s.y, -10000, 10000); n.ScaleZ = Safe(s.z, -10000, 10000);
            if (node.Sprite != null) { Color c = node.Sprite.color; n.Red = Safe(c.r, 0, 32); n.Green = Safe(c.g, 0, 32); n.Blue = Safe(c.b, 0, 32); n.Alpha = Safe(c.a, 0, 1); n.SpriteFlags = (byte)((node.Sprite.enabled ? 1 : 0) | (node.Sprite.flipX ? 2 : 0) | (node.Sprite.flipY ? 4 : 0)); }
            if (node.Physical != null) { PhysicalBehaviour p2 = node.Physical; n.Temperature = Safe(p2.Temperature, -1000000, 1000000000); n.Charge = Safe(p2.Charge, -1000000000, 1000000000); n.Burn = Safe(p2.BurnProgress, 0, 1); n.BurnIntensity = Safe(p2.BurnIntensity, 0, 1000000); n.Wetness = Safe(p2.Wetness, 0, 1000000); n.Fire = p2.OnFire; n.PhysicalFlags = (byte)(p2.IsWeightless ? 1 : 0); }
            if (node.Limb != null) { LimbBehaviour limb = node.Limb; n.Health = Safe(limb.Health, -1000000000, 1000000000); n.Numbness = Safe(limb.Numbness, 0, 1000000); n.BodyTemperature = Safe(limb.BodyTemperature, -1000000, 1000000000); n.LimbFlags = (byte)((limb.Broken ? 1 : 0) | (limb.Frozen ? 2 : 0) | (limb.IsDismembered ? 4 : 0) | (limb.Joint != null ? 8 : 0)); }
            if (node.Skin != null) { n.Rot = Safe(node.Skin.RottenProgress, 0, 1); n.Acid = Safe(node.Skin.AcidProgress, 0, 1); }
            return n;
        }

        // The caller must establish host authority, session/map, per-chunk
        // sequencing and registered identity before invoking this method.
        internal static bool Apply(PPGTogetherIdentity identity, ObjectStateChunk chunk)
        {
            if (identity == null || !identity.ReplicatedSpawn || !ObjectStateCodec.Valid(chunk) || identity.NetId != chunk.NetId || !Prime(identity)) return false;
            Layout layout = layouts[identity.NetId];
            if (chunk.Layout != layout.Hash || chunk.Total != layout.Nodes.Length || chunk.Nodes == null || chunk.Nodes.Length == 0 || chunk.Nodes.Length > ObjectStateCodec.NodesPerChunk || chunk.Offset + chunk.Nodes.Length > layout.Nodes.Length) return false;
            // Validate all local component signatures before any mutation.
            for (int i = 0; i < chunk.Nodes.Length; i++)
                if (chunk.Nodes[i] == null || ((chunk.Nodes[i].Flags & 1) != 0 && ((chunk.Nodes[i].Flags & 124) != layout.Nodes[chunk.Offset + i].Schema || chunk.Nodes[i].ColliderCount != layout.Nodes[chunk.Offset + i].Colliders.Length))) return false;
            for (int i = 0; i < chunk.Nodes.Length; i++) ApplyNode(layout.Nodes[chunk.Offset + i], chunk.Nodes[i], chunk.Offset + i == 0);
            return true;
        }

        private static void ApplyNode(Node node, ObjectStateNode n, bool root)
        {
            Transform t = node.Transform; if (t == null) return;
            if ((n.Flags & 1) == 0)
            {
                // Keep the cached object as a tombstone: delayed packets cannot
                // accidentally map this missing limb to another hierarchy slot.
                // Root removal remains the reliable Despawn protocol's job.
                if (!root) t.gameObject.SetActive(false);
                return;
            }
            t.gameObject.SetActive((n.Flags & 2) != 0);
            t.gameObject.layer = n.Layer;
            for (int i = 0; i < node.Colliders.Length; i++) if (node.Colliders[i] != null) node.Colliders[i].enabled = (n.ColliderMask & (1u << i)) != 0;
            t.position = new Vector3(n.X, n.Y, n.Z); t.rotation = Quaternion.Euler(0, 0, n.Angle); t.localScale = new Vector3(n.ScaleX, n.ScaleY, n.ScaleZ);
            if (node.Body != null) { node.Body.bodyType = RigidbodyType2D.Kinematic; node.Body.position = new Vector2(n.X, n.Y); node.Body.rotation = n.Angle; node.Body.velocity = Vector2.zero; node.Body.angularVelocity = 0; }
            if (node.Sprite != null) { node.Sprite.color = new Color(n.Red, n.Green, n.Blue, n.Alpha); node.Sprite.enabled = (n.SpriteFlags & 1) != 0; node.Sprite.flipX = (n.SpriteFlags & 2) != 0; node.Sprite.flipY = (n.SpriteFlags & 4) != 0; }
            if (node.Physical != null) { PhysicalBehaviour p = node.Physical; p.Temperature = n.Temperature; p.Charge = n.Charge; p.BurnProgress = n.Burn; p.Wetness = n.Wetness; p.IsWeightless = (n.PhysicalFlags & 1) != 0; if (p.OnFire != n.Fire) { if (n.Fire) p.Ignite(true); else p.Extinguish(); } p.BurnIntensity = n.BurnIntensity; }
            if (node.Limb != null) { LimbBehaviour limb = node.Limb; limb.Health = n.Health; limb.Numbness = n.Numbness; limb.BodyTemperature = n.BodyTemperature; limb.Broken = (n.LimbFlags & 1) != 0; limb.Frozen = (n.LimbFlags & 2) != 0; limb.IsDismembered = (n.LimbFlags & 4) != 0; if (limb.Joint != null) limb.Joint.enabled = false; }
            if (node.Skin != null) { node.Skin.RottenProgress = n.Rot; node.Skin.AcidProgress = n.Acid; }
        }
    }
}
