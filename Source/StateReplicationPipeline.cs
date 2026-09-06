using System.Collections.Generic;
using UnityEngine;

namespace PPGTogether.BepInEx
{
    public sealed partial class PPGTogetherPlugin
    {
        private readonly Queue<byte[]> pendingObjectChunks = new Queue<byte[]>();
        private readonly Dictionary<ulong, Dictionary<ushort, uint>> objectStateTicks = new Dictionary<ulong, Dictionary<ushort, uint>>();
        private readonly HashSet<ulong> mismatchedLayouts = new HashSet<ulong>();
        private readonly Queue<byte[]> pendingWounds = new Queue<byte[]>();
        private readonly Dictionary<ulong, Dictionary<ushort, uint>> woundTicks = new Dictionary<ulong, Dictionary<ushort, uint>>();
        private int woundRootCursor;
        private float nextWoundsAt;

        private void PumpWoundStates()
        {
            if (!IsHost || !sessionActive || Time.unscaledTime < nextWoundsAt) return;
            nextWoundsAt = Time.unscaledTime + .2f;
            List<PPGTogetherIdentity> roots = new List<PPGTogetherIdentity>(registry.All());
            roots.Sort(delegate(PPGTogetherIdentity a, PPGTogetherIdentity b) { return a.NetId.CompareTo(b.NetId); });
            int captured = 0, sent = 0, bytes = 0;
            while (sent < 24 && bytes < 48000)
            {
                if (pendingWounds.Count == 0)
                {
                    if (roots.Count == 0 || captured >= roots.Count) break;
                    woundRootCursor %= roots.Count;
                    foreach (byte[] payload in ReplicatedWoundState.Capture(roots[woundRootCursor++])) pendingWounds.Enqueue(payload);
                    captured++;
                    if (pendingWounds.Count == 0) continue;
                }
                byte[] packet = pendingWounds.Dequeue();
                Broadcast(WireMessage.WoundState, WireChannel.Snapshot, packet, false);
                sent++; bytes += packet.Length;
            }
        }

        private void HandleWoundState(Envelope envelope)
        {
            WoundState state; PPGTogetherIdentity identity;
            if (!WoundStateCodec.TryDecode(envelope.Payload, out state) || !registry.TryGet(state.NetId, out identity)) return;
            Dictionary<ushort, uint> ticks; uint previous;
            if (woundTicks.TryGetValue(state.NetId, out ticks) && ticks.TryGetValue(state.NodeIndex, out previous) &&
                !CursorSequence.IsNewer(envelope.Tick, previous)) return;
            if (!ReplicatedWoundState.Apply(identity, state)) return;
            if (ticks == null) { ticks = new Dictionary<ushort, uint>(); woundTicks[state.NetId] = ticks; }
            ticks[state.NodeIndex] = envelope.Tick;
        }

        private void BroadcastObjectStates()
        {
            List<PPGTogetherIdentity> roots = new List<PPGTogetherIdentity>(registry.All());
            roots.Sort(delegate(PPGTogetherIdentity a, PPGTogetherIdentity b) { return a.NetId.CompareTo(b.NetId); });
            int rootsCaptured = 0, chunksSent = 0, bytesSent = 0;
            // Resume a large root next tick; never restart a partial batch and starve
            // its later limbs. Other roots get a turn once this batch is drained.
            while (chunksSent < 24 && bytesSent < 48000)
            {
                if (pendingObjectChunks.Count == 0)
                {
                    if (roots.Count == 0 || rootsCaptured >= Mathf.Min(24, roots.Count)) break;
                    snapshotRootCursor %= roots.Count;
                    PPGTogetherIdentity root = roots[snapshotRootCursor++];
                    rootsCaptured++;
                    if (root == null) continue;
                    foreach (byte[] chunk in ReplicatedObjectState.Capture(root)) pendingObjectChunks.Enqueue(chunk);
                    if (pendingObjectChunks.Count == 0) continue;
                }
                byte[] payload = pendingObjectChunks.Dequeue();
                Broadcast(WireMessage.ObjectState, WireChannel.Snapshot, payload, false);
                chunksSent++; bytesSent += payload.Length;
            }
        }

        private void HandleObjectState(Envelope envelope)
        {
            ObjectStateChunk chunk; PPGTogetherIdentity identity;
            if (!ObjectStateCodec.TryDecode(envelope.Payload, out chunk) || !registry.TryGet(chunk.NetId, out identity)) return;
            Dictionary<ushort, uint> ticks; uint previous;
            if (objectStateTicks.TryGetValue(chunk.NetId, out ticks) && ticks.TryGetValue(chunk.Offset, out previous) &&
                !CursorSequence.IsNewer(envelope.Tick, previous)) return;
            if (!ReplicatedObjectState.Apply(identity, chunk))
            {
                if (mismatchedLayouts.Add(chunk.NetId))
                    SetStatus("Object layout differs: " + SafeName(identity.SpawnKey) + ". Use matching game/mod versions and spawn a fresh object.");
                return;
            }
            if (ticks == null) { ticks = new Dictionary<ushort, uint>(); objectStateTicks[chunk.NetId] = ticks; }
            ticks[chunk.Offset] = envelope.Tick;
        }

        private void ResetObjectReplication()
        {
            pendingObjectChunks.Clear(); objectStateTicks.Clear(); mismatchedLayouts.Clear();
            pendingWounds.Clear(); woundTicks.Clear(); woundRootCursor = 0; nextWoundsAt = 0;
            ReplicatedObjectState.Clear(); snapshotRootCursor = 0;
            ResetSharedWorld();
        }

        private static byte[] ScopeWorldPayload(WireChannel channel, uint epoch, byte[] payload)
        {
            if (channel != WireChannel.World && channel != WireChannel.Snapshot) return payload;
            Writer w = new Writer(4 + (payload == null ? 0 : payload.Length));
            w.UInt(epoch); w.Raw(payload); return w.ToArray();
        }

        private void LateUpdate() { ApplyReceivedGlobalState(); }
        internal bool ConnectMenuBlocksTools { get { return menuVisible; } }
        internal bool ConnectMenuContainsCursor
        {
            get
            {
                if (!menuVisible) return false;
                float scale = ConnectMenuScale();
                return new Rect(menuPosition.x, menuPosition.y, ConnectMenuWidth * scale, ConnectMenuHeight() * scale)
                    .Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            }
        }
    }
}
