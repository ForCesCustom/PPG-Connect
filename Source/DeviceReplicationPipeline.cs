using System.Collections.Generic;
using UnityEngine;

namespace PPGTogether.BepInEx
{
    public sealed partial class PPGTogetherPlugin
    {
        private readonly Queue<byte[]> pendingDevices = new Queue<byte[]>();
        private readonly Dictionary<ulong, Dictionary<ushort, uint>> deviceStateTicks = new Dictionary<ulong, Dictionary<ushort, uint>>();
        private int deviceRootCursor;
        private float nextDevicesAt;
        private readonly List<PPGTogetherIdentity> deviceRoots = new List<PPGTogetherIdentity>();
        private uint deviceRootsRevision;
        private bool deviceRootsValid;

        private List<PPGTogetherIdentity> GetDeviceRoots()
        {
            List<PPGTogetherIdentity> roots = GetReplicationRoots();
            if (!deviceRootsValid || deviceRootsRevision != registry.Revision)
            {
                deviceRoots.Clear();
                foreach (PPGTogetherIdentity root in roots) if (ReplicatedDeviceState.HasDevices(root)) deviceRoots.Add(root);
                deviceRootsRevision = registry.Revision; deviceRootsValid = true;
            }
            return deviceRoots;
        }

        private void PumpDeviceStates()
        {
            if (!IsHost || !sessionActive || Time.unscaledTime < nextDevicesAt) return;
            nextDevicesAt = Time.unscaledTime + .1f;
            if (!HasSnapshotRecipients()) { pendingDevices.Clear(); return; }
            List<PPGTogetherIdentity> roots = GetDeviceRoots();
            int captured = 0, sent = 0;
            while (sent < 24)
            {
                if (pendingDevices.Count == 0)
                {
                    if (roots.Count == 0 || captured >= Mathf.Min(24, roots.Count)) break;
                    deviceRootCursor %= roots.Count;
                    PPGTogetherIdentity identity = roots[deviceRootCursor++];
                    captured++;
                    foreach (byte[] packet in ReplicatedDeviceState.Capture(identity)) pendingDevices.Enqueue(packet);
                    if (pendingDevices.Count == 0) continue;
                }
                Broadcast(WireMessage.DeviceState, WireChannel.Snapshot, pendingDevices.Dequeue(), false);
                sent++;
            }
        }

        private void HandleDeviceState(Envelope envelope)
        {
            DeviceState state; PPGTogetherIdentity identity;
            if (!DeviceStateCodec.TryDecode(envelope.Payload, out state) || !registry.TryGet(state.NetId, out identity)) return;
            Dictionary<ushort, uint> ticks; uint previous;
            if (deviceStateTicks.TryGetValue(state.NetId, out ticks) && ticks.TryGetValue(state.Offset, out previous) && !CursorSequence.IsNewer(envelope.Tick, previous)) return;
            if (!ReplicatedDeviceState.Apply(identity, state)) return;
            if (ticks == null) { ticks = new Dictionary<ushort, uint>(); deviceStateTicks[state.NetId] = ticks; }
            ticks[state.Offset] = envelope.Tick;
        }

        private void ResetDeviceReplication()
        {
            pendingDevices.Clear(); deviceStateTicks.Clear();
            deviceRootCursor = 0; nextDevicesAt = 0;
            deviceRoots.Clear(); deviceRootsValid = false; deviceRootsRevision = 0;
            ReplicatedDeviceState.Clear();
        }
    }
}
