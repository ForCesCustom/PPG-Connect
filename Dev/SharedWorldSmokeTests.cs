using System;
using System.Collections.Generic;
using PPGTogether.BepInEx;

internal static class SharedWorldSmokeTests
{
    private static int checks;
    private static void Check(bool condition, string label) { checks++; if (!condition) throw new Exception(label); }
    private static bool GlobalDecodes(byte[] bytes) { SharedGlobalState value; return SharedGlobalCodec.TryDecode(bytes, out value); }
    private static bool WiresDecode(byte[] bytes) { List<WireVisualState> value; return WireVisualCodec.TryDecode(bytes, out value); }
    private static void RejectFloat(byte[] good, int offset, float value, bool global, string label)
    {
        byte[] corrupt = (byte[])good.Clone(); Array.Copy(BitConverter.GetBytes(value), 0, corrupt, offset, 4);
        Check(!(global ? GlobalDecodes(corrupt) : WiresDecode(corrupt)), label);
    }
    private static void CheckTruncations(byte[] good, bool global)
    {
        for (int length = 0; length < good.Length; length++)
        {
            byte[] truncated = new byte[length]; Array.Copy(good, truncated, length);
            Check(!(global ? GlobalDecodes(truncated) : WiresDecode(truncated)), "Reject truncation " + length);
        }
        byte[] trailing = new byte[good.Length + 1]; Array.Copy(good, trailing, good.Length);
        Check(!(global ? GlobalDecodes(trailing) : WiresDecode(trailing)), "Reject trailing bytes");
    }
    private static WireVisualState Line(int id)
    {
        return new WireVisualState { Id = id, Width = .02f, R = .1f, G = .2f, B = .3f, A = 1f, Points = new float[] { -10f, 20f, 0f, 100f, -200f, 1f } };
    }
    private static void Throws(Action action, string label)
    {
        bool threw = false; try { action(); } catch (ArgumentException) { threw = true; }
        Check(threw, label);
    }
    private static void Main()
    {
        SharedGlobalState original = new SharedGlobalState { Paused = true, Slow = true, SlowScale = .2f, HasEnvironment = true, Lights = true, Rain = true, Snow = false, Fog = true, Gravity = -9.81f, Lightning = .5f, Temperature = 22f };
        byte[] global = SharedGlobalCodec.Encode(original); SharedGlobalState decoded;
        Check(global.Length == 23 && SharedGlobalCodec.TryDecode(global, out decoded), "Global roundtrip accepted");
        SharedGlobalCodec.TryDecode(global, out decoded);
        Check(decoded.Paused && decoded.Slow && decoded.HasEnvironment && decoded.Lights && decoded.Rain && !decoded.Snow && decoded.Fog && decoded.SlowScale == .2f && decoded.Gravity == -9.81f && decoded.Lightning == .5f && decoded.Temperature == 22f, "Global roundtrip retains every field");
        Check(!GlobalDecodes(null), "Null global rejected"); CheckTruncations(global, true);
        foreach (int offset in new[] { 0, 1, 6, 7, 8, 9, 10 })
        { byte[] invalid = (byte[])global.Clone(); invalid[offset] = 2; Check(!GlobalDecodes(invalid), "Noncanonical bool " + offset); }
        foreach (int offset in new[] { 2, 11, 15, 19 })
        {
            RejectFloat(global, offset, float.NaN, true, "Global NaN");
            RejectFloat(global, offset, float.PositiveInfinity, true, "Global infinity");
        }
        RejectFloat(global, 2, 0f, true, "Zero slow scale"); RejectFloat(global, 2, 1.1f, true, "Excessive slow scale");
        RejectFloat(global, 11, 100001f, true, "Excessive gravity"); RejectFloat(global, 15, -1f, true, "Invalid lightning");
        RejectFloat(global, 19, 1000001f, true, "Excessive temperature");
        original.SlowScale = float.NaN; Throws(delegate { SharedGlobalCodec.Encode(original); }, "Host global NaN rejected");

        List<WireVisualState> lines = new List<WireVisualState> { Line(-71), Line(99) };
        byte[] wires = WireVisualCodec.Encode(lines); List<WireVisualState> parsed;
        Check(WireVisualCodec.TryDecode(wires, out parsed), "Wire roundtrip accepted");
        Check(parsed.Count == 2 && parsed[0].Id == -71 && parsed[1].Id == 99 && parsed[0].Width == .02f && parsed[0].R == .1f && parsed[0].G == .2f && parsed[0].B == .3f && parsed[0].A == 1f && parsed[0].Points[4] == -200f, "Wire roundtrip values");
        CheckTruncations(wires, false); Check(!WiresDecode(null), "Null wires rejected");
        Check(!WiresDecode(new byte[Wire.MaxPacketBytes]), "Oversize wire packet");
        Check(WiresDecode(WireVisualCodec.Encode(new List<WireVisualState>())), "Empty wire set clears replicas");
        byte[] corrupt = (byte[])wires.Clone(); corrupt[0] = 129; Check(!WiresDecode(corrupt), "Wire count bounded");
        corrupt = (byte[])wires.Clone(); Array.Clear(corrupt, 2, 4); Check(!WiresDecode(corrupt), "Zero wire ID");
        corrupt = (byte[])wires.Clone(); Array.Copy(corrupt, 2, corrupt, 51, 4); Check(!WiresDecode(corrupt), "Duplicate wire IDs");
        corrupt = (byte[])wires.Clone(); corrupt[26] = 1; Check(!WiresDecode(corrupt), "Too few points");
        corrupt = (byte[])wires.Clone(); corrupt[26] = 17; Check(!WiresDecode(corrupt), "Too many points");
        foreach (int offset in new[] { 6, 10, 14, 18, 22, 27, 31, 35, 39, 43, 47 })
        { RejectFloat(wires, offset, float.NaN, false, "Wire NaN"); RejectFloat(wires, offset, float.NegativeInfinity, false, "Wire infinity"); }
        RejectFloat(wires, 6, 0f, false, "Zero width"); RejectFloat(wires, 6, 2.1f, false, "Excessive width");
        foreach (int offset in new[] { 10, 14, 18, 22 })
        { RejectFloat(wires, offset, -.01f, false, "Negative color"); RejectFloat(wires, offset, 1.01f, false, "HDR color rejected"); }
        RejectFloat(wires, 27, 1000001f, false, "Excessive coordinate");
        Throws(delegate { WireVisualCodec.Encode(null); }, "Null input encode");
        Throws(delegate { WireVisualCodec.Encode(new List<WireVisualState> { null }); }, "Null line encode");
        Throws(delegate { WireVisualCodec.Encode(new List<WireVisualState> { Line(1), Line(1) }); }, "Duplicate encode");
        lines[0].A = float.NaN; Throws(delegate { WireVisualCodec.Encode(lines); }, "Host invalid color encode");
        lines = new List<WireVisualState>();
        for (int i = 0; i < WireVisualCodec.MaximumLines; i++) { WireVisualState line = Line(i + 1); line.Points = new float[WireVisualCodec.MaximumPoints * 3]; lines.Add(line); }
        byte[] maximum = WireVisualCodec.Encode(lines);
        Check(maximum.Length < Wire.MaxPacketBytes - Wire.HeaderSize && WiresDecode(maximum), "Maximum geometry fits packet budget");
        lines.Add(Line(1000)); Throws(delegate { WireVisualCodec.Encode(lines); }, "Too many lines encode");
        Random random = new Random(73);
        for (int i = 0; i < 1000; i++) { byte[] malformed = new byte[random.Next(0, 400)]; random.NextBytes(malformed); GlobalDecodes(malformed); WiresDecode(malformed); checks++; }
        CheckWireSnapshots();
        Console.WriteLine("Shared world smoke tests passed: " + checks);
    }

    private static void CheckWireSnapshots()
    {
        List<WireVisualState> source = new List<WireVisualState>();
        for (int i = 0; i < WireVisualSnapshotCodec.MaximumLines; i++)
        {
            WireVisualState record = Line(i + 1); record.Points = new float[WireVisualCodec.MaximumPoints * 3]; source.Add(record);
        }
        List<byte[]> packets = WireVisualSnapshotCodec.Encode(10, source);
        Check(packets.Count == 8, "1000 wires span eight bounded pages");
        WireVisualSnapshotCollector collector = new WireVisualSnapshotCollector(); List<WireVisualState> complete;
        for (int i = packets.Count - 1; i >= 0; i--)
        {
            Check(packets[i].Length + 4 < Wire.MaxPacketBytes - Wire.HeaderSize, "Paged wires fit envelope plus epoch");
            Check(collector.Accept(packets[i], out complete) == (i == 0), "Out-of-order pages apply atomically");
            if (i == 0) Check(complete.Count == 1000 && complete[0].Id == 1 && complete[999].Id == 1000, "Every wire beyond former128 cap retained");
        }
        Check(!collector.Accept(packets[0], out complete), "Completed revision replay ignored");
        List<byte[]> next = WireVisualSnapshotCodec.Encode(11, source);
        Check(!collector.Accept(next[0], out complete) && complete == null, "Incomplete new snapshot does not delete previous wire set");
        Check(!collector.Accept(packets[7], out complete), "Old revision cannot complete new snapshot");
        Check(!collector.Accept(next[0], out complete), "Duplicate page cannot inflate completion count");
        List<byte[]> recovery = WireVisualSnapshotCodec.Encode(12, source);
        for (int i = 0; i < recovery.Count; i++) Check(collector.Accept(recovery[i], out complete) == (i == recovery.Count - 1), "Fresh complete snapshot recovers dropped page");
        byte[] empty = WireVisualSnapshotCodec.Encode(13, new List<WireVisualState>())[0];
        Check(collector.Accept(empty, out complete) && complete.Count == 0, "Explicit complete empty revision clears all wires");
        Check(!collector.Accept(recovery[0], out complete), "Delayed data cannot resurrect cleared wires");
        collector.Clear();
        Check(collector.Accept(WireVisualSnapshotCodec.Encode(uint.MaxValue, new List<WireVisualState>())[0], out complete), "Revision maximum accepted after reset");
        Check(collector.Accept(WireVisualSnapshotCodec.Encode(0, new List<WireVisualState>())[0], out complete), "Revision wrap accepted");
        Check(!collector.Accept(WireVisualSnapshotCodec.Encode(uint.MaxValue, new List<WireVisualState>())[0], out complete), "Old prewrap revision rejected");
        uint revision; int total, offset; List<WireVisualState> page;
        for (int length = 0; length < packets[0].Length; length++)
        {
            byte[] truncated = new byte[length]; Array.Copy(packets[0], truncated, length);
            Check(!WireVisualSnapshotCodec.TryDecode(truncated, out revision, out total, out offset, out page), "Truncated wire page rejected " + length);
        }
        byte[] invalid = (byte[])packets[0].Clone(); invalid[4] = 233; invalid[5] = 3;
        Check(!WireVisualSnapshotCodec.TryDecode(invalid, out revision, out total, out offset, out page), "Snapshot total capped1000");
        invalid = (byte[])packets[0].Clone(); invalid[6] = 1;
        Check(!WireVisualSnapshotCodec.TryDecode(invalid, out revision, out total, out offset, out page), "Unaligned page rejected");
        invalid = new byte[packets[0].Length + 1]; Array.Copy(packets[0], invalid, packets[0].Length);
        Check(!WireVisualSnapshotCodec.TryDecode(invalid, out revision, out total, out offset, out page), "Trailing snapshot bytes rejected");
        collector.Clear(); collector.Accept(packets[0], out complete);
        invalid = (byte[])packets[1].Clone(); Array.Copy(packets[0], 10, invalid, 10, 4);
        Check(!collector.Accept(invalid, out complete), "Cross-page duplicate identity rejected");
        for (int i = 1; i < packets.Count; i++) Check(collector.Accept(packets[i], out complete) == (i == packets.Count - 1), "Rejected duplicate page does not corrupt pending snapshot");
        source.Add(Line(1001)); Throws(delegate { WireVisualSnapshotCodec.Encode(1, source); }, "Oversize snapshot never silently truncated");
        source.RemoveAt(1000); source[999] = source[0]; Throws(delegate { WireVisualSnapshotCodec.Encode(1, source); }, "Cross-page duplicate rejected by sender");
    }
}
