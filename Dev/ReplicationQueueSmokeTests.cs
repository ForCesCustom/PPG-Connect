using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using PPGTogether.BepInEx;

internal static class ReplicationQueueSmokeTests
{
    private static int checks;
    private static string gameRoot;
    public static int Main(string[] args)
    {
        gameRoot = args.Length == 0 ? @"D:\SteamLibrary\steamapps\common\People Playground" : args[0];
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try { Run(); Console.WriteLine("Replication queue smoke tests passed: " + checks); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        foreach (string relative in new[] { @"People Playground_Data\Managed", @"BepInEx\core" })
        {
            string path = Path.Combine(Path.Combine(gameRoot, relative), name);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }
    private static void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
    private static ReceivedPacket Object(uint sequence, ulong root, ushort offset, uint epoch, uint layout, ulong sender, ulong nonce)
    {
        byte[] payload = ObjectStateCodec.Encode(new ObjectStateChunk { NetId = root, Layout = layout, Total = 256, Offset = offset, Nodes = new[] { new ObjectStateNode { Flags = 0 } } });
        return Packet(WireMessage.ObjectState, sequence, payload, epoch, sender, nonce);
    }
    private static ReceivedPacket Packet(WireMessage type, uint sequence, byte[] payload, uint epoch, ulong sender, ulong nonce)
    {
        Writer scoped = new Writer(payload.Length + 4); scoped.UInt(epoch); scoped.Raw(payload);
        return new ReceivedPacket { SteamId = sender, Data = Wire.Pack(type, WireChannel.Snapshot, nonce, 1, sequence, sequence, scoped.ToArray()) };
    }
    private static uint Sequence(ReceivedPacket packet) { return BitConverter.ToUInt32(packet.Data, 18); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        var queue = new TransientPacketBuffer(192);
        for (uint i = 1; i <= 10000; i++)
        {
            queue.Enqueue(Object(i, 1, 0, 1, 1, 1, 1));
            Check(queue.Count == 1, "Repeated pose must occupy one slot");
        }
        Check(Sequence(queue.Dequeue()) == 10000, "Newest pose retained");
        queue.Enqueue(Object(10, 1, 0, 1, 1, 1, 1));
        queue.Enqueue(Object(9, 1, 0, 1, 1, 1, 1));
        Check(queue.Count == 1 && Sequence(queue.Dequeue()) == 10, "Out-of-order pose cannot roll back");
        queue.Enqueue(Object(uint.MaxValue, 1, 0, 1, 1, 1, 1));
        queue.Enqueue(Object(0, 1, 0, 1, 1, 1, 1));
        Check(Sequence(queue.Dequeue()) == 0, "Sequence wrap accepted");
        queue.Enqueue(Object(1, 1, 0, 1, 1, 1, 1));
        queue.Enqueue(Object(2, 2, 0, 1, 1, 1, 1));
        queue.Enqueue(Object(3, 1, 0, 1, 1, 1, 1));
        Check(queue.Count == 2 && Sequence(queue.Dequeue()) == 3 && Sequence(queue.Dequeue()) == 2, "Replacement preserves FIFO fairness");
        queue.Enqueue(Object(1, 1, 0, 1, 1, 1, 1));
        queue.Enqueue(Object(2, 1, 24, 1, 1, 1, 1));
        queue.Enqueue(Object(3, 1, 0, 2, 1, 1, 1));
        queue.Enqueue(Object(4, 1, 0, 1, 2, 1, 1));
        queue.Enqueue(Object(5, 1, 0, 1, 1, 2, 1));
        queue.Enqueue(Object(6, 1, 0, 1, 1, 1, 2));
        Check(queue.Count == 6, "Part/epoch/layout/sender/nonce remain isolated"); queue.Clear();
        queue.Enqueue(Object(1, 1, 0, 1, 1, 1, 1));
        ReceivedPacket malformed = Object(2, 1, 0, 1, 1, 1, 1);
        malformed.Data[malformed.Data.Length - 1] = 255;
        queue.Enqueue(malformed);
        Check(queue.Count == 1 && Sequence(queue.Dequeue()) == 1, "Malformed replacement cannot evict valid state");
        ReceivedPacket initialMalformed = Object(10, 1, 0, 1, 1, 1, 1);
        initialMalformed.Data[initialMalformed.Data.Length - 1] = 255;
        queue.Enqueue(initialMalformed);
        queue.Enqueue(Object(9, 1, 0, 1, 1, 1, 1));
        ReceivedPacket recovered = queue.Dequeue();
        Check(Sequence(recovered) == 9 && recovered.Data[recovered.Data.Length - 1] == 0, "Malformed initial high sequence cannot suppress older valid state");
        queue.Enqueue(initialMalformed);
        queue.Enqueue(Object(10, 1, 0, 1, 1, 1, 1));
        recovered = queue.Dequeue();
        Check(Sequence(recovered) == 10 && recovered.Data[recovered.Data.Length - 1] == 0, "Malformed initial state can recover at equal sequence");
        byte[] wound = WoundStateCodec.Encode(new WoundState { NetId = 1, Layout = 1, NodeIndex = 0, Points = new WoundPointState[0] });
        queue.Enqueue(Packet(WireMessage.WoundState, 1, wound, 1, 1, 1));
        queue.Enqueue(Packet(WireMessage.WoundState, 2, wound, 1, 1, 1));
        queue.Enqueue(Object(3, 1, 0, 1, 1, 1, 1));
        Check(queue.Count == 2 && Sequence(queue.Dequeue()) == 2 && Sequence(queue.Dequeue()) == 3, "Wound replacement is separate from pose");
        byte[] device = DeviceStateCodec.Encode(new DeviceState { NetId = 1, Layout = 1, Offset = 0, Nodes = new[] { new DeviceNodeState { NodeIndex = 0, Kind = 1, Brightness = 1 } } });
        queue.Enqueue(Packet(WireMessage.DeviceState, 1, device, 1, 1, 1));
        queue.Enqueue(Packet(WireMessage.DeviceState, 2, device, 1, 1, 1));
        Check(queue.Count == 1 && Sequence(queue.Dequeue()) == 2, "Device chunks coalesce independently");
        // Unrecognised multipart transient payloads are intentionally not keyed.
        queue.Enqueue(Packet(WireMessage.WireVisual, 1, new byte[64], 1, 1, 1));
        queue.Enqueue(Packet(WireMessage.WireVisual, 2, new byte[64], 1, 1, 1));
        Check(queue.Count == 2, "Wire chunks cannot replace one another"); queue.Clear();
        for (uint i = 1; i <= 1000; i++) { queue.Enqueue(Object(i, i, 0, 1, 1, 1, 1)); Check(queue.Count <= 192, "Queue stays bounded"); }
        Check(queue.Count == 192 && Sequence(queue.Dequeue()) == 809, "Capacity evicts oldest disposable packet");
        queue.Clear(); Check(queue.Count == 0, "Reset empties index and FIFO");
        queue.Enqueue(Object(1, 1, 0, 1, 1, 1, 1)); Check(queue.Count == 1, "Reset permits same key again");
        for (int count = 0; count <= 600; count++)
        {
            int budget = SteamRelayTransport.ReceiveBudget(count);
            Check(budget >= 0 && budget <= 128 && (count >= 512 ? budget == 0 : count + budget <= 512), "Receive budget protects reliable capacity");
        }
    }
}
