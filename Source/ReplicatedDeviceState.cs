using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPGTogether.BepInEx
{
    // A closed presentation-only schema. No method names, component names,
    // reflection, activation callbacks, audio events or materials cross the wire.
    internal sealed class DeviceNodeState
    {
        internal ushort NodeIndex;
        internal byte Kind;
        internal float Brightness;
        internal bool Activated;
    }

    internal sealed class DeviceState
    {
        internal ulong NetId;
        internal uint Layout;
        internal ushort Offset;
        internal DeviceNodeState[] Nodes;
    }

    internal static class DeviceStateCodec
    {
        internal const int NodesPerChunk = 24;
        internal const int MaximumBytes = 15 + NodesPerChunk * 8;
        internal const float MaximumBrightness = 1000000;

        internal static bool Valid(DeviceState value)
        {
            if (value == null || value.NetId == 0 || value.Offset >= ObjectStateCodec.MaximumNodes || value.Offset % NodesPerChunk != 0 || value.Nodes == null || value.Nodes.Length == 0 || value.Nodes.Length > NodesPerChunk) return false;
            int previous = -1;
            foreach (DeviceNodeState n in value.Nodes)
            {
                if (n == null || n.NodeIndex < value.Offset || n.NodeIndex >= value.Offset + NodesPerChunk || n.NodeIndex >= ObjectStateCodec.MaximumNodes || n.NodeIndex <= previous || n.Kind == 0 || n.Kind > 3 || float.IsNaN(n.Brightness) || float.IsInfinity(n.Brightness) || n.Brightness < 0 || n.Brightness > MaximumBrightness) return false;
                // Canonical zero fields prevent alternate encodings of the same state.
                if ((n.Kind & 1) == 0 && n.Brightness != 0 || (n.Kind & 2) == 0 && n.Activated) return false;
                previous = n.NodeIndex;
            }
            return true;
        }

        internal static byte[] Encode(DeviceState value)
        {
            if (!Valid(value)) throw new ArgumentException("Invalid device state");
            Writer w = new Writer(MaximumBytes);
            w.ULong(value.NetId); w.UInt(value.Layout); w.UShort(value.Offset); w.Byte((byte)value.Nodes.Length);
            foreach (DeviceNodeState n in value.Nodes) { w.UShort(n.NodeIndex); w.Byte(n.Kind); w.Float(n.Brightness); w.Bool(n.Activated); }
            return w.ToArray();
        }

        internal static bool TryDecode(byte[] bytes, out DeviceState value)
        {
            value = null;
            if (bytes == null || bytes.Length < 23 || bytes.Length > MaximumBytes) return false;
            Reader r = new Reader(bytes); DeviceState parsed = new DeviceState(); byte count;
            if (!r.ULong(out parsed.NetId) || !r.UInt(out parsed.Layout) || !r.UShort(out parsed.Offset) || !r.Byte(out count) || count == 0 || count > NodesPerChunk || r.Remaining != count * 8) return false;
            parsed.Nodes = new DeviceNodeState[count];
            for (int i = 0; i < count; i++)
            {
                DeviceNodeState n = new DeviceNodeState(); parsed.Nodes[i] = n;
                if (!r.UShort(out n.NodeIndex) || !r.Byte(out n.Kind) || !r.Float(out n.Brightness) || !r.Bool(out n.Activated)) return false;
            }
            if (r.Remaining != 0 || !Valid(parsed)) return false;
            value = parsed; return true;
        }
    }

    internal static class ReplicatedDeviceState
    {
        private sealed class Slot
        {
            internal byte Kind;
            internal LightSprite Light;
            internal SpriteRenderer LightRenderer;
            internal SingleFloodlightBehaviour Floodlight;
            internal MaterialPropertyBlock Block;
        }
        private sealed class Layout
        {
            internal PPGTogetherIdentity Identity;
            internal uint BaseHash, Hash;
            internal Slot[] Slots;
            internal readonly List<int> ActiveOffsets = new List<int>();
        }
        private static readonly Dictionary<ulong, Layout> layouts = new Dictionary<ulong, Layout>();
        private static readonly int GlowIntensity = Shader.PropertyToID("_GlowIntensity");

        private static bool Prime(PPGTogetherIdentity identity, out Layout layout)
        {
            layout = null; uint baseHash; int count;
            if (!ReplicatedObjectState.TryGetCachedLayout(identity, out baseHash, out count)) return false;
            if (layouts.TryGetValue(identity.NetId, out layout) && layout.Identity == identity && layout.BaseHash == baseHash) return true;
            SpawnableAsset asset = ModAPI.FindSpawnable(identity.SpawnKey);
            if (asset == null || asset.Prefab == null) return false;
            Transform[] authored = asset.Prefab.GetComponentsInChildren<Transform>(true);
            if (authored.Length != count) return false;
            layout = new Layout { Identity = identity, BaseHash = baseHash, Hash = baseHash, Slots = new Slot[count] };
            for (ushort i = 0; i < count; i++)
            {
                Slot slot = new Slot(); layout.Slots[i] = slot;
                LightSprite sourceLight = authored[i].GetComponent<LightSprite>();
                SingleFloodlightBehaviour sourceFlood = authored[i].GetComponent<SingleFloodlightBehaviour>();
                slot.Kind = (byte)((sourceLight != null && sourceLight.GetType() == typeof(LightSprite) ? 1 : 0) | (sourceFlood != null && sourceFlood.GetType() == typeof(SingleFloodlightBehaviour) ? 2 : 0));
                layout.Hash = unchecked((layout.Hash ^ slot.Kind) * 16777619);
                int offset = i / DeviceStateCodec.NodesPerChunk * DeviceStateCodec.NodesPerChunk;
                if (slot.Kind != 0 && (layout.ActiveOffsets.Count == 0 || layout.ActiveOffsets[layout.ActiveOffsets.Count - 1] != offset)) layout.ActiveOffsets.Add(offset);
                Transform instance;
                if (slot.Kind == 0 || !ReplicatedObjectState.TryGetCachedTransform(identity, baseHash, i, out instance)) continue;
                if ((slot.Kind & 1) != 0)
                {
                    slot.Light = instance.GetComponent<LightSprite>();
                    // Serialized references can point outside their component's
                    // hierarchy. Never trust such a reference with a renderer
                    // write: only a renderer owned by this identity is eligible.
                    if (slot.Light != null && ReplicatedObjectState.FindIdentity(slot.Light.SpriteRenderer) == identity) slot.LightRenderer = slot.Light.SpriteRenderer;
                }
                if ((slot.Kind & 2) != 0) slot.Floodlight = instance.GetComponent<SingleFloodlightBehaviour>();
            }
            layouts[identity.NetId] = layout; return true;
        }

        internal static List<byte[]> Capture(PPGTogetherIdentity identity)
        {
            List<byte[]> result = new List<byte[]>(); Layout layout;
            if (!Prime(identity, out layout)) return result;
            foreach (int offset in layout.ActiveOffsets)
            {
                List<DeviceNodeState> nodes = new List<DeviceNodeState>();
                for (int i = offset; i < Math.Min(layout.Slots.Length, offset + DeviceStateCodec.NodesPerChunk); i++)
                {
                    Slot slot = layout.Slots[i];
                    if (slot.Kind == 0) continue;
                    float brightness = slot.Light != null ? slot.Light.Brightness : 0;
                    if (float.IsNaN(brightness) || float.IsInfinity(brightness)) brightness = 0;
                    nodes.Add(new DeviceNodeState { NodeIndex = (ushort)i, Kind = slot.Kind, Brightness = Mathf.Clamp(brightness, 0, DeviceStateCodec.MaximumBrightness), Activated = slot.Floodlight != null && slot.Floodlight.Activated });
                }
                if (nodes.Count != 0) result.Add(DeviceStateCodec.Encode(new DeviceState { NetId = identity.NetId, Layout = layout.Hash, Offset = (ushort)offset, Nodes = nodes.ToArray() }));
            }
            return result;
        }

        internal static bool HasDevices(PPGTogetherIdentity identity)
        {
            Layout layout;
            return Prime(identity, out layout) && layout.ActiveOffsets.Count != 0;
        }

        // Caller authenticates current host/map and discards stale packets per
        // (NetId, Offset). Validate the entire chunk before changing any component.
        internal static bool Apply(PPGTogetherIdentity identity, DeviceState state)
        {
            Layout layout;
            if (identity == null || !identity.ReplicatedSpawn || !DeviceStateCodec.Valid(state) || identity.NetId != state.NetId || !Prime(identity, out layout) || layout.Hash != state.Layout) return false;
            foreach (DeviceNodeState node in state.Nodes)
                if (node.NodeIndex >= layout.Slots.Length || layout.Slots[node.NodeIndex].Kind != node.Kind) return false;
            foreach (DeviceNodeState node in state.Nodes)
            {
                Slot slot = layout.Slots[node.NodeIndex];
                // Never call Use/UpdateActivation: those invoke sound and native
                // activation. Native Update reads this plain field for lamp glow;
                // ObjectState already carries the authored on/off child activity.
                if (slot.Floodlight != null) slot.Floodlight.Activated = node.Activated;
                if (slot.Light != null && slot.LightRenderer != null)
                {
                    // The native Brightness setter dereferences a private block
                    // not yet initialized on inactive lamps. Set only the fixed
                    // local renderer property; never access sharedMaterial.
                    if (slot.Block == null) slot.Block = new MaterialPropertyBlock();
                    slot.LightRenderer.GetPropertyBlock(slot.Block);
                    slot.Block.SetFloat(GlowIntensity, node.Brightness);
                    slot.LightRenderer.SetPropertyBlock(slot.Block);
                }
            }
            return true;
        }

        internal static void Forget(ulong netId) { layouts.Remove(netId); }
        internal static void Clear() { layouts.Clear(); }
    }
}
