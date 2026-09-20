using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;

namespace PPGTogether.BepInEx
{
    internal struct ReceivedPacket
    {
        internal ulong SteamId;
        internal Connection Connection;
        internal byte[] Data;
    }

    // Only independently replaceable chunks are keyed. In particular, multipart
    // world/wire manifests must remain distinct FIFO entries.
    internal sealed class TransientPacketBuffer
    {
        private struct Key : IEquatable<Key>
        {
            internal ulong Sender, Nonce, Root;
            internal uint Connection, Epoch, Layout;
            internal ushort Peer, Part;
            internal byte Type;
            public bool Equals(Key other) { return Sender == other.Sender && Connection == other.Connection && Nonce == other.Nonce && Root == other.Root && Epoch == other.Epoch && Layout == other.Layout && Peer == other.Peer && Part == other.Part && Type == other.Type; }
            public override bool Equals(object other) { return other is Key && Equals((Key)other); }
            public override int GetHashCode() { unchecked { return Sender.GetHashCode() * 397 ^ (int)Connection ^ Nonce.GetHashCode() ^ Root.GetHashCode() ^ (int)Epoch ^ (int)Layout ^ (Peer << 16) ^ Part ^ Type; } }
        }
        private sealed class Entry { internal ReceivedPacket Packet; internal bool Keyed; internal Key Key; }
        private readonly int capacity;
        private readonly LinkedList<Entry> packets = new LinkedList<Entry>();
        private readonly Dictionary<Key, LinkedListNode<Entry>> byKey = new Dictionary<Key, LinkedListNode<Entry>>();
        internal TransientPacketBuffer(int capacity) { if (capacity < 1) throw new ArgumentOutOfRangeException("capacity"); this.capacity = capacity; }
        internal int Count { get { return packets.Count; } }

        // Returns true when an obsolete packet was replaced/discarded.
        internal bool Enqueue(ReceivedPacket packet)
        {
            Key key; bool keyed = TryKey(packet, out key);
            LinkedListNode<Entry> existing;
            if (keyed && byKey.TryGetValue(key, out existing))
            {
                uint next = BitConverter.ToUInt32(packet.Data, 18), previous = BitConverter.ToUInt32(existing.Value.Packet.Data, 18);
                // The first entry was only header-checked. Its untrusted sequence
                // must not suppress a valid older/equal candidate if its payload
                // is malformed. Check the queued payload only on this fallback.
                if ((unchecked((int)(next - previous)) > 0 || !ValidReplacement(existing.Value.Packet.Data)) && ValidReplacement(packet.Data))
                    existing.Value.Packet = packet;
                // Retain the FIFO position: a constantly moving limb must not
                // push a wound/chunk forever to the back of the receive queue.
                return true;
            }
            bool discarded = packets.Count >= capacity;
            if (discarded) Dequeue();
            var node = packets.AddLast(new Entry { Packet = packet, Keyed = keyed, Key = key });
            if (keyed) byKey.Add(key, node);
            return discarded;
        }
        internal ReceivedPacket Dequeue()
        {
            Entry entry = packets.First.Value; packets.RemoveFirst();
            if (entry.Keyed) byKey.Remove(entry.Key);
            return entry.Packet;
        }
        internal void Clear() { packets.Clear(); byKey.Clear(); }

        private static bool TryKey(ReceivedPacket packet, out Key key)
        {
            key = new Key(); byte[] data = packet.Data;
            if (data == null || data.Length < Wire.HeaderSize + 4 + 16 || data.Length > Wire.MaxPacketBytes ||
                BitConverter.ToUInt32(data, 0) != Wire.Magic || BitConverter.ToUInt16(data, 4) != Wire.ProtocolVersion ||
                data[7] != (byte)WireChannel.Snapshot || BitConverter.ToUInt32(data, 26) != data.Length - Wire.HeaderSize) return false;
            WireMessage type = (WireMessage)data[6];
            if (type != WireMessage.ObjectState && type != WireMessage.WoundState && type != WireMessage.DeviceState) return false;
            int start = Wire.HeaderSize + 4;
            key.Sender = packet.SteamId; key.Connection = packet.Connection.Id;
            key.Nonce = BitConverter.ToUInt64(data, 8); key.Peer = BitConverter.ToUInt16(data, 16); key.Type = data[6];
            key.Epoch = BitConverter.ToUInt32(data, Wire.HeaderSize); key.Root = BitConverter.ToUInt64(data, start);
            key.Layout = BitConverter.ToUInt32(data, start + 8);
            key.Part = BitConverter.ToUInt16(data, start + (type == WireMessage.ObjectState ? 14 : 12));
            return key.Root != 0;
        }
        private static bool ValidReplacement(byte[] data)
        {
            // Validate the complete candidate only on a collision, not for every
            // queued packet. Malformed newer data cannot evict a valid state.
            int start = Wire.HeaderSize + 4;
            byte[] payload = new byte[data.Length - start];
            Buffer.BlockCopy(data, start, payload, 0, payload.Length);
            if ((WireMessage)data[6] == WireMessage.ObjectState) { ObjectStateChunk chunk; return ObjectStateCodec.TryDecode(payload, out chunk); }
            if ((WireMessage)data[6] == WireMessage.DeviceState) { DeviceState device; return DeviceStateCodec.TryDecode(payload, out device); }
            WoundState wound; return WoundStateCodec.TryDecode(payload, out wound);
        }
    }

    internal sealed class SteamRelayTransport
    {
        private readonly PPGTogetherPlugin plugin;
        // A reliable Spawn/control packet must never sit behind a flood of
        // disposable physics/cursor snapshots. Keep the two classes separate
        // and always drain world/control work first.
        private readonly Queue<ReceivedPacket> reliableReceived = new Queue<ReceivedPacket>();
        private readonly TransientPacketBuffer transientReceived = new TransientPacketBuffer(192);
        private readonly object queueLock = new object();
        private HostSocket socket;
        private ClientConnection client;
        private bool hosting;
        private int droppedTransientPackets;

        internal bool Hosting { get { return hosting; } }
        internal bool Connected { get { return client != null && client.Connection.Id != 0 && client.Connected; } }

        internal SteamRelayTransport(PPGTogetherPlugin plugin)
        {
            this.plugin = plugin;
        }

        internal void StartHost()
        {
            Close();
            socket = SteamNetworkingSockets.CreateRelaySocket<HostSocket>(0);
            socket.Owner = this;
            hosting = true;
            plugin.LogTransport("Steam relay listen socket opened on virtual port 0.");
        }

        internal void ConnectToHost(SteamId hostSteamId)
        {
            Close();
            client = SteamNetworkingSockets.ConnectRelay<ClientConnection>(hostSteamId, 0);
            client.Owner = this;
            hosting = false;
            plugin.LogTransport("Connecting to host through Steam relay (connection " + client.Connection.Id + ", host " + (ulong)hostSteamId + ").");
        }

        internal void Pump()
        {
            int receiveLimit;
            lock (queueLock) receiveLimit = ReceiveBudget(reliableReceived.Count);
            // Do not consume messages we cannot retain. Steam keeps reliable
            // messages queued until the application catches up next frame.
            if (receiveLimit <= 0) return;
            if (socket != null)
                socket.Receive(receiveLimit, false);
            if (client != null)
                client.Receive(receiveLimit, false);
        }

        internal static int ReceiveBudget(int protectedCount) { return Math.Max(0, Math.Min(128, 512 - protectedCount)); }

        internal bool TryDequeue(out ReceivedPacket packet)
        {
            lock (queueLock)
            {
                if (reliableReceived.Count > 0)
                {
                    packet = reliableReceived.Dequeue();
                    return true;
                }
                if (transientReceived.Count > 0)
                {
                    packet = transientReceived.Dequeue();
                    return true;
                }
                packet = new ReceivedPacket();
                return false;
            }
        }

        internal void SendToClient(Connection connection, byte[] bytes, bool reliable)
        {
            if (connection.Id == 0 || bytes == null)
                return;
            Result result = connection.SendMessage(bytes, reliable ? SendType.Reliable : SendType.Unreliable, 0);
            if (result != Result.OK)
                plugin.LogTransport("Relay send to client " + connection.Id + " failed: " + result + ".");
        }

        internal void SendToHost(byte[] bytes, bool reliable)
        {
            if (client == null || client.Connection.Id == 0 || bytes == null)
                return;
            Result result = client.Connection.SendMessage(bytes, reliable ? SendType.Reliable : SendType.Unreliable, 0);
            if (result != Result.OK)
                plugin.LogTransport("Relay send to host " + client.Connection.Id + " failed: " + result + ".");
        }

        internal void Close()
        {
            if (client != null)
            {
                try { client.Close(false, 0, "Connect session closed"); }
                catch (Exception exception) { plugin.LogTransport("Relay client close skipped: " + exception.GetType().Name + "."); }
                client = null;
            }
            if (socket != null)
            {
                try { socket.Close(); }
                catch (Exception exception) { plugin.LogTransport("Relay host close skipped: " + exception.GetType().Name + "."); }
                socket = null;
            }
            hosting = false;
            lock (queueLock)
            {
                reliableReceived.Clear();
                transientReceived.Clear();
                droppedTransientPackets = 0;
            }
        }

        internal void OnIncomingConnection(Connection connection, ConnectionInfo info)
        {
            SteamId steamId = info.Identity.SteamId;
            if (!info.Identity.IsSteamId || !plugin.IsLobbyMember(steamId))
            {
                connection.Close(false, 4001, "Not a member of the active Connect lobby");
                plugin.LogTransport("Rejected relay connection " + connection.Id + ": identity=" + (ulong)steamId + ", isSteamId=" + info.Identity.IsSteamId + ", lobbyMember=" + plugin.IsLobbyMember(steamId) + ".");
                return;
            }
            Result result = connection.Accept();
            plugin.LogTransport("Accepted relay connection " + connection.Id + " from lobby member " + (ulong)steamId + "; Accept=" + result + ".");
        }

        internal void Enqueue(Connection connection, SteamId steamId, IntPtr data, int size)
        {
            if (data == IntPtr.Zero || size <= 0 || size > Wire.MaxPacketBytes)
                return;
            byte[] copy = new byte[size];
            Marshal.Copy(data, copy, 0, size);
            ReceivedPacket packet = new ReceivedPacket { Connection = connection, SteamId = (ulong)steamId, Data = copy };
            bool transient = IsTransientPacket(copy);
            lock (queueLock)
            {
                if (transient)
                {
                    // Snapshot/cursor packets have a newer replacement within
                    // milliseconds. Dropping an old one is correct; dropping
                    // a reliable Spawn is not.
                    if (transientReceived.Enqueue(packet))
                    {
                        // Keep the newest visual state. Retaining the oldest
                        // snapshots under pressure makes a newly-created root
                        // look permanently stale even though newer poses arrive.
                        droppedTransientPackets++;
                        if (droppedTransientPackets == 1 || droppedTransientPackets % 256 == 0)
                            plugin.LogTransport("Coalesced " + droppedTransientPackets + " transient relay packet(s) to protect reliable world messages.");
                    }
                    return;
                }
                if (reliableReceived.Count < 512)
                {
                    reliableReceived.Enqueue(packet);
                    return;
                }
                // Should be unreachable with Pump backpressure. If a concurrent
                // callback violates that invariant, fail explicitly rather than
                // continue a world after silently losing its Spawn/Despawn.
                plugin.LogTransport("Reliable relay queue overflow; closing connection " + connection.Id + " to require a clean world resync.");
            }
            connection.Close(false, 4002, "Reliable queue overloaded; reconnect to resynchronise");
        }

        private static bool IsTransientPacket(byte[] data)
        {
            if (data == null || data.Length < Wire.HeaderSize) return false;
            WireMessage type = (WireMessage)data[6];
            return type == WireMessage.Cursor || type == WireMessage.Snapshot || type == WireMessage.RigSnapshot ||
                type == WireMessage.ObjectState || type == WireMessage.GlobalState || type == WireMessage.WireVisual ||
                type == WireMessage.WoundState || type == WireMessage.DeviceState || type == WireMessage.GrabUpdate;
        }

        private sealed class HostSocket : SocketManager
        {
            internal SteamRelayTransport Owner;

            public override void OnConnecting(Connection connection, ConnectionInfo info)
            {
                if (Owner != null) Owner.OnIncomingConnection(connection, info);
            }

            public override void OnConnected(Connection connection, ConnectionInfo info)
            {
                // SocketManager.OnConnected assigns the connection to its poll group.
                // Without this base call SocketManager.Receive() can never see client
                // messages: the lobby join succeeds but Hello/cursor/map packets vanish.
                base.OnConnected(connection, info);
                if (Owner != null) Owner.plugin.LogTransport("Relay client connected: connection=" + connection.Id + ", identity=" + (ulong)info.Identity.SteamId + ".");
            }

            public override void OnDisconnected(Connection connection, ConnectionInfo info)
            {
                if (Owner != null)
                {
                    Owner.plugin.LogTransport("Relay client disconnected: connection=" + connection.Id + ", identity=" + (ulong)info.Identity.SteamId + ", state=" + info.State + ", reason=" + info.EndReason + ".");
                    Owner.plugin.OnTransportDisconnected((ulong)info.Identity.SteamId);
                }
            }

            public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
            {
                if (Owner != null)
                {
                    if (!identity.IsSteamId)
                        Owner.plugin.LogTransport("Received relay message with a non-Steam identity on connection " + connection.Id + ".");
                    Owner.Enqueue(connection, identity.SteamId, data, size);
                }
            }
        }

        private sealed class ClientConnection : ConnectionManager
        {
            internal SteamRelayTransport Owner;

            public override void OnConnected(ConnectionInfo info)
            {
                if (Owner != null)
                {
                    Owner.plugin.LogTransport("Relay client-side connection established: connection=" + Connection.Id + ", host=" + (ulong)info.Identity.SteamId + ".");
                    Owner.plugin.OnRelayClientConnected((ulong)info.Identity.SteamId);
                }
            }

            public override void OnDisconnected(ConnectionInfo info)
            {
                if (Owner != null)
                {
                    Owner.plugin.LogTransport("Relay host connection disconnected: connection=" + Connection.Id + ", state=" + info.State + ", reason=" + info.EndReason + ".");
                    Owner.plugin.OnHostTransportDisconnected();
                }
            }

            public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
            {
                if (Owner != null) Owner.Enqueue(Connection, ConnectionInfo.Identity.SteamId, data, size);
            }
        }
    }
}
