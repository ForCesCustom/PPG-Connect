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
        private readonly Queue<ReceivedPacket> received = new Queue<ReceivedPacket>();
        private readonly object queueLock = new object();
        private HostSocket socket;
        private ClientConnection client;
        private bool hosting;

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
                socket.Receive(64, false);
            if (client != null)
                client.Receive(64, false);
        }

        internal bool TryDequeue(out ReceivedPacket packet)
        {
            lock (queueLock)
            {
                if (received.Count == 0)
                {
                    packet = new ReceivedPacket();
                    return false;
                }
                packet = received.Dequeue();
                return true;
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
                client.Close(false, 0, "Connect session closed");
                client = null;
            }
            if (socket != null)
            {
                socket.Close();
                socket = null;
            }
            hosting = false;
            lock (queueLock)
                received.Clear();
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
            lock (queueLock)
            {
                if (received.Count < 256)
                    received.Enqueue(new ReceivedPacket { Connection = connection, SteamId = (ulong)steamId, Data = copy });
                else
                    plugin.LogTransport("Dropped relay packet because the incoming queue is full.");
            }
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
