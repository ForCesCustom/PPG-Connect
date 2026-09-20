using System;
using PPGTogether.BepInEx;

internal static class DeviceStateSmokeTests
{
    private static int checks;
    private static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    private static bool Decode(byte[] bytes) { DeviceState state; return DeviceStateCodec.TryDecode(bytes, out state); }
    private static void Main()
    {
        DeviceState state = new DeviceState { NetId = 99, Layout = 123, Offset = 0, Nodes = new[] { new DeviceNodeState { NodeIndex = 2, Kind = 3, Brightness = 12.5f, Activated = true } } };
        byte[] bytes = DeviceStateCodec.Encode(state); DeviceState parsed;
        Check(DeviceStateCodec.TryDecode(bytes, out parsed) && parsed.NetId == 99 && parsed.Layout == 123 && parsed.Nodes[0].NodeIndex == 2 && parsed.Nodes[0].Kind == 3 && parsed.Nodes[0].Brightness == 12.5f && parsed.Nodes[0].Activated, "both typed devices roundtrip");
        for (int i = 0; i < bytes.Length; i++) { byte[] cut = new byte[i]; Array.Copy(bytes, cut, i); Check(!Decode(cut), "truncation " + i); }
        byte[] bad = (byte[])bytes.Clone(); bad[22] = 2; Check(!Decode(bad), "invalid boolean");
        bad = (byte[])bytes.Clone(); bad[17] = 4; Check(!Decode(bad), "unknown device type");
        bad = (byte[])bytes.Clone(); bad[17] = 0; Check(!Decode(bad), "empty device type");
        bad = (byte[])bytes.Clone(); bad[14] = 25; Check(!Decode(bad), "too many entries");
        bad = (byte[])bytes.Clone(); bad[14] = 0; Check(!Decode(bad), "empty chunk");
        bad = (byte[])bytes.Clone(); bad[12] = 1; Check(!Decode(bad), "unaligned offset");
        bad = (byte[])bytes.Clone(); Array.Clear(bad, 0, 8); Check(!Decode(bad), "zero identity");
        bad = (byte[])bytes.Clone(); bad[15] = 24; Check(!Decode(bad), "node outside chunk");
        bad = (byte[])bytes.Clone(); bad[16] = 1; Check(!Decode(bad), "node outside canonical limit");
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1, DeviceStateCodec.MaximumBrightness + 1 })
        { bad = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(value), 0, bad, 18, 4); Check(!Decode(bad), "invalid brightness"); }
        bad = new byte[bytes.Length + 1]; Array.Copy(bytes, bad, bytes.Length); Check(!Decode(bad), "trailing byte");
        Check(!Decode(null) && !Decode(new byte[DeviceStateCodec.MaximumBytes + 1]), "null and oversized");
        state.Nodes[0].Kind = 1; Check(!DeviceStateCodec.Valid(state), "light cannot carry activation");
        state.Nodes[0].Activated = false; Check(Decode(DeviceStateCodec.Encode(state)), "light only");
        state.Nodes[0].Kind = 2; Check(!DeviceStateCodec.Valid(state), "floodlight cannot carry brightness");
        state.Nodes[0].Brightness = 0; Check(Decode(DeviceStateCodec.Encode(state)), "floodlight only off");
        state.Nodes[0].Activated = true; Check(Decode(DeviceStateCodec.Encode(state)), "floodlight only on");
        state.Nodes = new DeviceNodeState[24];
        for (int i = 0; i < 24; i++) state.Nodes[i] = new DeviceNodeState { NodeIndex = (ushort)i, Kind = 1, Brightness = DeviceStateCodec.MaximumBrightness };
        Check(DeviceStateCodec.Encode(state).Length == DeviceStateCodec.MaximumBytes && Decode(DeviceStateCodec.Encode(state)), "maximum bounded chunk");
        state.Nodes[1].NodeIndex = 0; Check(!DeviceStateCodec.Valid(state), "duplicate node");
        state.Nodes[1].NodeIndex = 2; Check(!DeviceStateCodec.Valid(state), "unordered duplicate node");
        state.Offset = 240; state.Nodes = new[] { new DeviceNodeState { NodeIndex = 255, Kind = 2 } };
        Check(Decode(DeviceStateCodec.Encode(state)), "last canonical slot");
        state.Nodes = new DeviceNodeState[] { null }; Check(!DeviceStateCodec.Valid(state), "null entry");
        bool threw = false; try { DeviceStateCodec.Encode(state); } catch (ArgumentException) { threw = true; } Check(threw, "encoder rejects invalid value");
        Random random = new Random(481);
        for (int i = 0; i < 10000; i++) { bad = new byte[random.Next(0, DeviceStateCodec.MaximumBytes + 2)]; random.NextBytes(bad); DeviceState decoded; bool accepted = DeviceStateCodec.TryDecode(bad, out decoded); Check(!accepted || DeviceStateCodec.Valid(decoded), "malformed fuzz " + i); }
        Console.WriteLine("DeviceState smoke tests passed: " + checks);
    }
}
