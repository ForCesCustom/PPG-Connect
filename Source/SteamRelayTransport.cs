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

    internal sealed class SteamRelayTransport
    {
        private readonly PPGTogetherPlugin plugin;
        // A reliable Spawn/control packet must never sit behind a flood of
        // disposable physics/cursor snapshots. Keep the two classes separate
        // and always drain world/control work first.
        private readonly Queue<ReceivedPacket> reliableReceived = new Queue<ReceivedPacket>();
        private readonly Queue<ReceivedPacket> transientReceived = new Queue<ReceivedPacket>();
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
            if (socket != null)
                socket.Receive(128, false);
            if (client != null)
                client.Receive(128, false);
        }

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
                    if (transientReceived.Count >= 192)
                    {
                        // Keep the newest visual state. Retaining the oldest
                        // snapshots under pressure makes a newly-created root
                        // look permanently stale even though newer poses arrive.
                        transientReceived.Dequeue();
                        droppedTransientPackets++;
                        if (droppedTransientPackets == 1 || droppedTransientPackets % 256 == 0)
                            plugin.LogTransport("Coalesced " + droppedTransientPackets + " transient relay packet(s) to protect reliable world messages.");
                    }
                    transientReceived.Enqueue(packet);
                    return;
                }
                if (reliableReceived.Count < 512)
                {
                    reliableReceived.Enqueue(packet);
                    return;
                }
                plugin.LogTransport("Dropped a reliable relay packet because its protected queue is full.");
            }
        }

        private static bool IsTransientPacket(byte[] data)
        {
            if (data == null || data.Length < Wire.HeaderSize) return false;
            WireMessage type = (WireMessage)data[6];
            return type == WireMessage.Cursor || type == WireMessage.Snapshot || type == WireMessage.RigSnapshot ||
                type == WireMessage.ObjectState || type == WireMessage.GlobalState || type == WireMessage.WireVisual ||
                type == WireMessage.WoundState || type == WireMessage.GrabUpdate;
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
