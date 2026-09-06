using System;
using PPGTogether.BepInEx;
internal static class WoundStateSmokeTests
{
    private static int checks;
    private static void Check(bool v, string name) { checks++; if (!v) throw new Exception(name); }
    private static bool Decode(byte[] data) { WoundState state; return WoundStateCodec.TryDecode(data, out state); }
    private static void Main()
    {
        WoundState state = new WoundState { NetId = 9, Layout = 123, NodeIndex = 255, TrackAge = true, Points = new[] { new WoundPointState { X = -.1f, Y = .5f, Intensity = 3, Kind = 1, Age = 99 } } };
        byte[] bytes = WoundStateCodec.Encode(state); WoundState parsed;
        Check(WoundStateCodec.TryDecode(bytes, out parsed) && parsed.Points[0].X == -.1f && parsed.Points[0].Kind == 1 && parsed.Points[0].Age == 99 && parsed.NodeIndex == 255, "bullet wound roundtrip");
        for (int i = 0; i < bytes.Length; i++) { byte[] cut = new byte[i]; Array.Copy(bytes, cut, i); Check(!Decode(cut), "truncated packet " + i); }
        byte[] bad = (byte[])bytes.Clone(); bad[14] = 2; Check(!Decode(bad), "invalid boolean");
        bad = (byte[])bytes.Clone(); bad[15] = 129; Check(!Decode(bad), "excess count");
        bad = (byte[])bytes.Clone(); bad[12] = 0; bad[13] = 1; Check(!Decode(bad), "excess slot");
        bad = (byte[])bytes.Clone(); Array.Clear(bad, 0, 8); Check(!Decode(bad), "zero id");
        bad = (byte[])bytes.Clone(); bad[28] = 6; Check(!Decode(bad), "unknown damage type");
        bad = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, bad, 16, 4); Check(!Decode(bad), "nan local position");
        bad = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(-1f), 0, bad, 29, 4); Check(!Decode(bad), "negative age");
        bad = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(WoundStateCodec.MaximumAge + 1), 0, bad, 29, 4); Check(!Decode(bad), "excess age");
        bad = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(float.PositiveInfinity), 0, bad, 24, 4); Check(!Decode(bad), "infinite intensity");
        bad = new byte[bytes.Length + 1]; Array.Copy(bytes, bad, bytes.Length); Check(!Decode(bad), "trailing bytes");
        Check(!Decode(null) && !Decode(new byte[WoundStateCodec.MaximumBytes + 1]), "null and oversized packets");
        state.Points = new WoundPointState[128]; for (int i = 0; i < 128; i++) state.Points[i] = new WoundPointState { Kind = (byte)(i % 6), Age = WoundStateCodec.MaximumAge };
        Check(WoundStateCodec.Encode(state).Length <= WoundStateCodec.MaximumBytes && Decode(WoundStateCodec.Encode(state)), "128 native damage points bounded");
        state.Points = new WoundPointState[0]; Check(Decode(WoundStateCodec.Encode(state)), "zero point clears old wounds");
        state.Points = new WoundPointState[] { null }; bool threw = false; try { WoundStateCodec.Encode(state); } catch (ArgumentException) { threw = true; } Check(threw, "encoder rejects null point");
        Console.WriteLine("WoundState smoke tests passed: " + checks);
    }
}
