using System;
using System.Collections.Generic;
using PPGTogether.BepInEx;

internal static class WorldLifecycleSmokeTests
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        WorldManifestData manifest;
        byte[] payload = WorldManifestProtocol.Encode(5, 9, new List<ulong> { 2, 7, 9 });
        Require(WorldManifestProtocol.TryDecode(payload, out manifest), "Valid manifest failed.");
        Require(manifest.Epoch == 5 && manifest.LiveIds.Count == 3, "Manifest fields changed.");
        Require(WorldManifestProtocol.IsAbsent(manifest, 8), "Orphan not removed.");
        Require(!WorldManifestProtocol.IsAbsent(manifest, 9), "Live root removed.");
        Require(!WorldManifestProtocol.IsAbsent(manifest, 10), "Spawn newer than captured manifest removed.");
        Require(!WorldManifestProtocol.IsAbsent(manifest, 0), "Unregistered root removed.");
        payload = WorldManifestProtocol.Encode(6, 9, new List<ulong>());
        Require(WorldManifestProtocol.TryDecode(payload, out manifest) && WorldManifestProtocol.IsAbsent(manifest, 9), "Empty-world tombstone failed.");
        List<ulong> maximum = new List<ulong>();
        for (ulong i = 1; i <= 1000; i++) maximum.Add(i);
        payload = WorldManifestProtocol.Encode(7, 1000, maximum);
        Require(WorldManifestProtocol.TryDecode(payload, out manifest) && manifest.LiveIds.Count == 1000, "Maximum-size manifest failed.");
        for (int length = 0; length < 14; length++)
            Require(!WorldManifestProtocol.TryDecode(new byte[length], out manifest), "Truncated manifest accepted.");
        payload = WorldManifestProtocol.Encode(7, 2, new List<ulong> { 1, 2 });
        Array.Copy(payload, 14, payload, 22, 8);
        Require(!WorldManifestProtocol.TryDecode(payload, out manifest), "Duplicate IDs accepted.");
        payload = WorldManifestProtocol.Encode(7, 2, new List<ulong> { 1, 2 });
        payload[22] = 3;
        Require(!WorldManifestProtocol.TryDecode(payload, out manifest), "ID beyond high water accepted.");
        payload = WorldManifestProtocol.Encode(7, 2, new List<ulong> { 1 });
        payload[0] = 0;
        Require(!WorldManifestProtocol.TryDecode(payload, out manifest), "Zero epoch accepted.");
        payload = WorldManifestProtocol.Encode(7, 2, new List<ulong> { 1 });
        Array.Resize(ref payload, payload.Length + 1);
        Require(!WorldManifestProtocol.TryDecode(payload, out manifest), "Trailing bytes accepted.");
        Console.WriteLine("World lifecycle manifest smoke tests passed.");
    }
}
