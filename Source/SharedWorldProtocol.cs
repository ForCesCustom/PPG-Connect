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
