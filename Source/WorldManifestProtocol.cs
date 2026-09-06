using System;
using System.Collections.Generic;

namespace PPGTogether.BepInEx
{
    internal sealed class WorldManifestData
    {
        internal uint Epoch;
        internal ulong HighWater;
        internal readonly HashSet<ulong> LiveIds = new HashSet<ulong>();
    }

    internal static class WorldManifestProtocol
    {
        internal const int MaximumRoots = 1000;

        internal static byte[] Encode(uint epoch, ulong highWater, IList<ulong> liveIds)
        {
            if (epoch == 0 || liveIds == null || liveIds.Count > MaximumRoots)
                throw new ArgumentException("Invalid world manifest.");
            Writer writer = new Writer(14 + liveIds.Count * 8);
            writer.UInt(epoch);
            writer.ULong(highWater);
            writer.UShort((ushort)liveIds.Count);
            HashSet<ulong> seen = new HashSet<ulong>();
            for (int i = 0; i < liveIds.Count; i++)
            {
                ulong id = liveIds[i];
                if (id == 0 || id > highWater || !seen.Add(id)) throw new ArgumentException("Invalid manifest identity.");
                writer.ULong(id);
            }
            return writer.ToArray();
        }

        internal static bool TryDecode(byte[] payload, out WorldManifestData manifest)
        {
            manifest = null;
            if (payload == null) return false;
            Reader reader = new Reader(payload);
            WorldManifestData candidate = new WorldManifestData();
            ushort count;
            if (!reader.UInt(out candidate.Epoch) || candidate.Epoch == 0 || !reader.ULong(out candidate.HighWater) ||
                !reader.UShort(out count) || count > MaximumRoots || reader.Remaining != count * 8) return false;
            for (int i = 0; i < count; i++)
            {
                ulong id;
                if (!reader.ULong(out id) || id == 0 || id > candidate.HighWater || !candidate.LiveIds.Add(id)) return false;
            }
            manifest = candidate;
            return true;
        }

        internal static bool IsAbsent(WorldManifestData manifest, ulong id)
        {
            return manifest != null && id != 0 && id <= manifest.HighWater && !manifest.LiveIds.Contains(id);
        }
    }
}
