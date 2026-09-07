using System;
using PPGTogether.BepInEx;

internal static class ObjectStateSmokeTests
{
    private static int checks;
    private static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    private static bool Decodes(byte[] bytes) { ObjectStateChunk parsed; return ObjectStateCodec.TryDecode(bytes, out parsed); }
    private static void Main()
    {
        string[] nativeRuntime = { "Outline", "Head", "BloodParticle(Clone)", "Body", "Fire" };
        Check(AuthoredNodePaths.MatchChild("Head", 0, 1, nativeRuntime) == 1, "runtime effect before authored child ignored");
        Check(AuthoredNodePaths.MatchChild("Body", 0, 1, nativeRuntime) == 3, "runtime effect between authored children ignored");
        Check(AuthoredNodePaths.MatchChild("Missing", 0, 1, nativeRuntime) == -1, "destroyed authored child leaves tombstone");
        Check(AuthoredNodePaths.MatchChild("Limb", 1, 2, new[] { "Limb", "Outline", "Limb" }) == 2, "duplicate authored names retain ordinal");
        Check(AuthoredNodePaths.MatchChild("Limb", 0, 2, new[] { "Limb" }) == -1, "missing duplicate does not steal another limb identity");
        Check(AuthoredNodePaths.MatchChild("Head", 0, 1, new[] { "Head", "Head" }) == -1, "ambiguous runtime duplicate fails closed");
        Check(AuthoredNodePaths.MatchChild("head", 0, 1, nativeRuntime) == -1, "authored name matching is case sensitive");
        Check(AuthoredNodePaths.Segment("Limb", 0) != AuthoredNodePaths.Segment("Limb", 1), "duplicate path slots distinct");
        Check(AuthoredNodePaths.Segment("A/1:B", 0) != AuthoredNodePaths.Segment("A", 0) + AuthoredNodePaths.Segment("B", 0), "path segment escaping is unambiguous");
        string[] largeRuntime = new string[400];
        for (int i = 0; i < largeRuntime.Length; i++) largeRuntime[i] = "TransientEffect" + i;
        largeRuntime[321] = "Head";
        Check(AuthoredNodePaths.MatchChild("Head", 0, 1, largeRuntime) == 321, "effects beyond packet node limit do not affect authored matching");
        ObjectStateNode full = new ObjectStateNode { Flags = 127, X = 2, Y = -15, Angle = -90, ScaleX = -2, ScaleY = 3, Temperature = 200, Charge = 17, Burn = .4f, BurnIntensity = 2, Fire = true, Health = -8, Numbness = .2f, BodyTemperature = 37, LimbFlags = 5, Rot = .7f, Acid = .3f, SpriteFlags = 7, Red = .1f, Blue = .5f, Alpha = .8f };
        ObjectStateChunk source = new ObjectStateChunk { NetId = 7, Layout = 0xDEADBEEF, Total = 3, Offset = 1, Nodes = new[] { full, new ObjectStateNode() } };
        byte[] good = ObjectStateCodec.Encode(source); ObjectStateChunk parsed;
        Check(ObjectStateCodec.TryDecode(good, out parsed), "valid complex state");
        Check(parsed.NetId == 7 && parsed.Layout == source.Layout && parsed.Offset == 1 && parsed.Nodes[0].Fire && parsed.Nodes[0].Health == -8 && parsed.Nodes[0].ScaleX == -2 && parsed.Nodes[1].Flags == 0, "roundtrip material and destroyed node");
        for (int i = 0; i < good.Length; i++) { byte[] truncated = new byte[i]; Array.Copy(good, truncated, i); Check(!Decodes(truncated), "reject truncation at " + i); }
        byte[] trailing = new byte[good.Length + 1]; Array.Copy(good, trailing, good.Length); Check(!Decodes(trailing), "reject trailing bytes");
        byte[] corrupt = (byte[])good.Clone(); Array.Clear(corrupt, 0, 8); Check(!Decodes(corrupt), "zero id");
        corrupt = (byte[])good.Clone(); corrupt[12] = 1; corrupt[13] = 1; Check(!Decodes(corrupt), "maximum total");
        corrupt = (byte[])good.Clone(); corrupt[14] = 3; Check(!Decodes(corrupt), "chunk outside root");
        corrupt = (byte[])good.Clone(); corrupt[16] = 25; Check(!Decodes(corrupt), "too many nodes");
        corrupt = (byte[])good.Clone(); corrupt[17] = 128; Check(!Decodes(corrupt), "unknown flags");
        corrupt = (byte[])good.Clone(); corrupt[17] = 2; Check(!Decodes(corrupt), "absent cannot be active");
        corrupt = (byte[])good.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, corrupt, 18, 4); Check(!Decodes(corrupt), "NaN pose");
        corrupt = (byte[])good.Clone(); Array.Copy(BitConverter.GetBytes(2000000f), 0, corrupt, 18, 4); Check(!Decodes(corrupt), "excessive pose");
        corrupt = (byte[])good.Clone(); corrupt[46] = 32; Check(!Decodes(corrupt), "invalid Unity layer");
        corrupt = (byte[])good.Clone(); corrupt[47] = 33; Check(!Decodes(corrupt), "excessive collider count");
        corrupt = (byte[])good.Clone(); corrupt[48] = 1; Check(!Decodes(corrupt), "collider mask cannot address nonexistent collider");
        corrupt = (byte[])good.Clone(); corrupt[68] = 8; Check(!Decodes(corrupt), "invalid sprite flags");
        corrupt = (byte[])good.Clone(); corrupt[89] = 2; Check(!Decodes(corrupt), "noncanonical fire boolean");
        corrupt = (byte[])good.Clone(); corrupt[90] = 2; Check(!Decodes(corrupt), "unknown physical flags");
        corrupt = (byte[])good.Clone(); corrupt[103] = 16; Check(!Decodes(corrupt), "unknown limb flags");
        corrupt = (byte[])good.Clone(); Array.Copy(BitConverter.GetBytes(1.01f), 0, corrupt, 104, 4); Check(!Decodes(corrupt), "rot outside material range");
        Check(!Decodes(null), "null packet");
        Check(!Decodes(new byte[ObjectStateCodec.MaximumChunkBytes + 1]), "oversized packet");
        ObjectStateNode[] maximum = new ObjectStateNode[ObjectStateCodec.NodesPerChunk]; for (int i = 0; i < maximum.Length; i++) maximum[i] = full;
        source.Total = ObjectStateCodec.MaximumNodes; source.Offset = 232; source.Nodes = maximum;
        Check(ObjectStateCodec.Encode(source).Length <= ObjectStateCodec.MaximumChunkBytes && Decodes(ObjectStateCodec.Encode(source)), "full final chunk bounded");
        full.ColliderCount = 32; full.ColliderMask = uint.MaxValue;
        Check(Decodes(ObjectStateCodec.Encode(source)), "32 collider bitmask does not overflow shift");
        full.Health = float.PositiveInfinity; bool threw = false; try { ObjectStateCodec.Encode(source); } catch (ArgumentException) { threw = true; } Check(threw, "encoder rejects nonfinite host data");
        Console.WriteLine("ObjectState smoke tests passed: " + checks);
    }
}
