using System;
using System.Collections.Generic;

namespace PPGTogether.BepInEx
{
    internal struct SharedGlobalState
    {
        internal bool Paused, Slow, HasEnvironment, Lights, Rain, Snow, Fog;
        internal float SlowScale, Gravity, Lightning, Temperature;
    }
    internal static class SharedGlobalCodec
    {
        internal static byte[] Encode(SharedGlobalState s)
        {
            if (!IsValid(s)) throw new ArgumentException("Invalid global state");
            Writer w = new Writer(32); w.Bool(s.Paused); w.Bool(s.Slow); w.Float(s.SlowScale); w.Bool(s.HasEnvironment);
            w.Bool(s.Lights); w.Bool(s.Rain); w.Bool(s.Snow); w.Bool(s.Fog); w.Float(s.Gravity); w.Float(s.Lightning); w.Float(s.Temperature); return w.ToArray();
        }
        internal static bool TryDecode(byte[] data, out SharedGlobalState s)
        {
            s = new SharedGlobalState(); if (data == null || data.Length != 23) return false; Reader r = new Reader(data);
            return r.Bool(out s.Paused) && r.Bool(out s.Slow) && r.Float(out s.SlowScale) && s.SlowScale >= .01f && s.SlowScale <= 1 &&
                r.Bool(out s.HasEnvironment) && r.Bool(out s.Lights) && r.Bool(out s.Rain) && r.Bool(out s.Snow) && r.Bool(out s.Fog) &&
                r.Float(out s.Gravity) && Math.Abs(s.Gravity) <= 100000 && r.Float(out s.Lightning) && Math.Abs(s.Lightning) <= 100000 &&
                r.Float(out s.Temperature) && Math.Abs(s.Temperature) <= 1000000 && r.Remaining == 0 && IsValid(s);
        }
        internal static bool IsValid(SharedGlobalState s)
        {
            return Bounded(s.SlowScale, .01f, 1f) && Bounded(s.Gravity, -100000f, 100000f) &&
                Bounded(s.Lightning, 0f, 1f) && Bounded(s.Temperature, -1000000f, 1000000f);
        }
        internal static bool Bounded(float value, float minimum, float maximum)
        { return !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum && value <= maximum; }
    }
    internal sealed class WireVisualState
    {
        internal int Id; internal float Width, R, G, B, A; internal float[] Points;
    }
    // Each bounded packet carries part of one complete wire set. A receiver
    // must not treat an individual page as a deletion manifest.
    internal static class WireVisualSnapshotCodec
    {
        internal const int MaximumLines = 1000;
        internal const int HeaderBytes = 8;
        internal static List<byte[]> Encode(uint revision, List<WireVisualState> records)
        {
            if (records == null || records.Count > MaximumLines) throw new ArgumentException("Invalid wire snapshot size");
            HashSet<int> ids = new HashSet<int>();
            foreach (WireVisualState record in records)
                if (record == null || !ids.Add(record.Id)) throw new ArgumentException("Duplicate wire snapshot identity");
            List<byte[]> packets = new List<byte[]>();
            for (int offset = 0; offset < Math.Max(1, records.Count); offset += WireVisualCodec.MaximumLines)
            {
                int count = Math.Min(WireVisualCodec.MaximumLines, records.Count - offset);
                byte[] body = WireVisualCodec.Encode(records.GetRange(offset, count));
                Writer w = new Writer(HeaderBytes + body.Length);
                w.UInt(revision); w.UShort((ushort)records.Count); w.UShort((ushort)offset); w.Raw(body);
                packets.Add(w.ToArray());
            }
            return packets;
        }
        internal static bool TryDecode(byte[] payload, out uint revision, out int total, out int offset, out List<WireVisualState> records)
        {
            revision = 0; total = offset = 0; records = null;
            if (payload == null || payload.Length < HeaderBytes + 2 || payload.Length > Wire.MaxPacketBytes - Wire.HeaderSize - 4) return false;
            Reader r = new Reader(payload); ushort rawTotal, rawOffset;
            if (!r.UInt(out revision) || !r.UShort(out rawTotal) || !r.UShort(out rawOffset)) return false;
            total = rawTotal; offset = rawOffset;
            if (total > MaximumLines || offset % WireVisualCodec.MaximumLines != 0 || offset > total || (total != 0 && offset == total)) return false;
            byte[] body = new byte[payload.Length - HeaderBytes]; Array.Copy(payload, HeaderBytes, body, 0, body.Length);
            return WireVisualCodec.TryDecode(body, out records) && records.Count == Math.Min(WireVisualCodec.MaximumLines, total - offset);
        }
    }

    internal sealed class WireVisualSnapshotCollector
    {
        private bool started, completed;
        private uint revision;
        private int total, received;
        private WireVisualState[] lines;
        private bool[] pages;
        private readonly HashSet<int> ids = new HashSet<int>();

        internal bool Accept(byte[] payload, out List<WireVisualState> snapshot)
        {
            snapshot = null;
            uint incoming; int size, offset; List<WireVisualState> records;
            if (!WireVisualSnapshotCodec.TryDecode(payload, out incoming, out size, out offset, out records)) return false;
            if (started && incoming != revision && unchecked((int)(incoming - revision)) <= 0) return false;
            if (!started || incoming != revision)
            {
                started = true; completed = false; revision = incoming; total = size; received = 0;
                lines = new WireVisualState[size]; pages = new bool[Math.Max(1, (size + WireVisualCodec.MaximumLines - 1) / WireVisualCodec.MaximumLines)]; ids.Clear();
            }
            if (completed || size != total || pages[offset / WireVisualCodec.MaximumLines]) return false;
            // Validate cross-page identity uniqueness before mutating collection.
            foreach (WireVisualState record in records) if (ids.Contains(record.Id)) return false;
            foreach (WireVisualState record in records) ids.Add(record.Id);
            records.CopyTo(lines, offset); pages[offset / WireVisualCodec.MaximumLines] = true; received += records.Count;
            if (received != total) return false;
            completed = true; snapshot = new List<WireVisualState>(lines); return true;
        }
        internal void Clear()
        {
            started = completed = false; revision = 0; total = received = 0; lines = null; pages = null; ids.Clear();
        }
    }

    internal static class WireVisualCodec
    {
        internal const int MaximumLines = 128, MaximumPoints = 16;
        internal static byte[] Encode(List<WireVisualState> records)
        {
            if (records == null || records.Count > MaximumLines) throw new ArgumentException("Invalid wire visual count");
            Writer w = new Writer(4096); w.UShort((ushort)records.Count);
            HashSet<int> ids = new HashSet<int>();
            foreach (var s in records)
            {
                if (!IsValid(s) || !ids.Add(s.Id)) throw new ArgumentException("Invalid wire visual");
                w.UInt(unchecked((uint)s.Id)); w.Float(s.Width); w.Float(s.R); w.Float(s.G); w.Float(s.B); w.Float(s.A); w.Byte((byte)(s.Points.Length / 3));
                foreach (float p in s.Points) w.Float(p);
            }
            return w.ToArray();
        }
        internal static bool TryDecode(byte[] data, out List<WireVisualState> records)
        {
            records = null; if (data == null || data.Length > Wire.MaxPacketBytes - Wire.HeaderSize) return false;
            Reader r = new Reader(data); ushort count;
            if (!r.UShort(out count) || count > MaximumLines) return false;
            var result = new List<WireVisualState>(); var ids = new HashSet<int>();
            for (int n = 0; n < count; n++)
            {
                var s = new WireVisualState(); uint id; byte points;
                if (!r.UInt(out id) || !r.Float(out s.Width) || s.Width < .001f || s.Width > 2 ||
                    !r.Float(out s.R) || !r.Float(out s.G) || !r.Float(out s.B) || !r.Float(out s.A) ||
                    !r.Byte(out points) || points < 2 || points > MaximumPoints) return false;
                s.Id = unchecked((int)id); if (s.Id == 0 || !ids.Add(s.Id)) return false;
                s.Points = new float[points * 3];
                for (int i = 0; i < s.Points.Length; i++) if (!r.Float(out s.Points[i]) || Math.Abs(s.Points[i]) > 1000000) return false;
                if (!IsValid(s)) return false;
                result.Add(s);
            }
            if (r.Remaining != 0) return false; records = result; return true;
        }
        private static bool IsValid(WireVisualState s)
        {
            if (s == null || s.Id == 0 || s.Points == null || s.Points.Length < 6 || s.Points.Length > MaximumPoints * 3 || s.Points.Length % 3 != 0 ||
                !SharedGlobalCodec.Bounded(s.Width, .001f, 2f) || !SharedGlobalCodec.Bounded(s.R, 0f, 1f) || !SharedGlobalCodec.Bounded(s.G, 0f, 1f) ||
                !SharedGlobalCodec.Bounded(s.B, 0f, 1f) || !SharedGlobalCodec.Bounded(s.A, 0f, 1f)) return false;
            foreach (float point in s.Points) if (!SharedGlobalCodec.Bounded(point, -1000000f, 1000000f)) return false;
            return true;
        }
    }
}
