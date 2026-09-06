using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPGTogether.BepInEx
{
    internal sealed class WoundPointState
    {
        internal float X, Y, Intensity, Age;
        internal byte Kind;
    }

    internal sealed class WoundState
    {
        internal ulong NetId;
        internal uint Layout;
        internal ushort NodeIndex;
        internal bool TrackAge;
        internal WoundPointState[] Points;
    }

    internal static class WoundStateCodec
    {
        internal const int MaximumPoints = 128;
        internal const int MaximumBytes = 2304;
        internal const float MaximumAge = 604800;
        internal static bool Valid(WoundState value)
        {
            if (value == null || value.NetId == 0 || value.NodeIndex >= ObjectStateCodec.MaximumNodes || value.Points == null || value.Points.Length > MaximumPoints) return false;
            foreach (WoundPointState p in value.Points)
                if (p == null || p.Kind > 5 || !Range(p.X, -10000, 10000) || !Range(p.Y, -10000, 10000) || !Range(p.Intensity, 0, 1000) || !Range(p.Age, 0, MaximumAge)) return false;
            return true;
        }

        private static bool Range(float v, float min, float max) { return !float.IsNaN(v) && !float.IsInfinity(v) && v >= min && v <= max; }
        internal static byte[] Encode(WoundState value)
        {
            if (!Valid(value)) throw new ArgumentException("Invalid wound state");
            Writer w = new Writer(MaximumBytes);
            w.ULong(value.NetId); w.UInt(value.Layout); w.UShort(value.NodeIndex); w.Bool(value.TrackAge); w.Byte((byte)value.Points.Length);
            foreach (WoundPointState p in value.Points) { w.Float(p.X); w.Float(p.Y); w.Float(p.Intensity); w.Byte(p.Kind); w.Float(p.Age); }
            return w.ToArray();
        }

        internal static bool TryDecode(byte[] bytes, out WoundState value)
        {
            value = null;
            if (bytes == null || bytes.Length < 16 || bytes.Length > MaximumBytes) return false;
            Reader r = new Reader(bytes); WoundState parsed = new WoundState(); byte count;
            if (!r.ULong(out parsed.NetId) || !r.UInt(out parsed.Layout) || !r.UShort(out parsed.NodeIndex) || !r.Bool(out parsed.TrackAge) || !r.Byte(out count) || count > MaximumPoints || r.Remaining != count * 17) return false;
            parsed.Points = new WoundPointState[count];
            for (int i = 0; i < count; i++)
            {
                WoundPointState p = new WoundPointState(); parsed.Points[i] = p;
                if (!r.Float(out p.X) || !r.Float(out p.Y) || !r.Float(out p.Intensity) || !r.Byte(out p.Kind) || !r.Float(out p.Age)) return false;
            }
            if (r.Remaining != 0 || !Valid(parsed)) return false;
            value = parsed; return true;
        }
    }

    internal static class ReplicatedWoundState
    {
        internal static List<byte[]> Capture(PPGTogetherIdentity identity)
        {
            List<byte[]> result = new List<byte[]>(); uint layout; int count;
            if (!ReplicatedObjectState.TryGetCachedLayout(identity, out layout, out count)) return result;
            float now = Time.time;
            for (ushort i = 0; i < count; i++)
            {
                SkinMaterialHandler skin;
                if (!ReplicatedObjectState.TryGetCachedSkin(identity, layout, i, out skin) || skin.damagePoints == null) continue;
                int pointCount = Math.Max(0, Math.Min(WoundStateCodec.MaximumPoints, Math.Min(skin.currentDamagePointCount, skin.damagePoints.Length)));
                WoundState state = new WoundState(); state.NetId = identity.NetId; state.Layout = layout; state.NodeIndex = i; state.TrackAge = skin.TrackWoundAge; state.Points = new WoundPointState[pointCount];
                for (int j = 0; j < pointCount; j++)
                {
                    Vector4 p = skin.damagePoints[j];
                    float age = skin.damagePointTimeStamps != null && j < skin.damagePointTimeStamps.Length ? now - skin.damagePointTimeStamps[j] : 0;
                    byte kind = !float.IsNaN(p.w) && p.w >= 0 && p.w <= 5 ? (byte)Mathf.FloorToInt(p.w) : (byte)5;
                    state.Points[j] = new WoundPointState { X = Safe(p.x, -10000, 10000), Y = Safe(p.y, -10000, 10000), Intensity = Safe(p.z, 0, 1000), Kind = kind, Age = Safe(age, 0, WoundStateCodec.MaximumAge) };
                }
                result.Add(WoundStateCodec.Encode(state));
            }
            return result;
        }

        private static float Safe(float v, float min, float max) { return float.IsNaN(v) || float.IsInfinity(v) ? min : Mathf.Clamp(v, min, max); }

        // Caller checks host authority/map and rejects stale ticks per
        // (NetId,NodeIndex). Full validation precedes any material mutation.
        internal static bool Apply(PPGTogetherIdentity identity, WoundState state)
        {
            SkinMaterialHandler skin;
            if (identity == null || !identity.ReplicatedSpawn || !WoundStateCodec.Valid(state) || identity.NetId != state.NetId || !ReplicatedObjectState.TryGetCachedSkin(identity, state.Layout, state.NodeIndex, out skin)) return false;
            if (skin.damagePoints == null || skin.damagePoints.Length != WoundStateCodec.MaximumPoints || skin.damagePointTimeStamps == null || skin.damagePointTimeStamps.Length != WoundStateCodec.MaximumPoints) return false;
            float now = Time.time;
            for (int i = 0; i < WoundStateCodec.MaximumPoints; i++)
            {
                if (i < state.Points.Length)
                {
                    WoundPointState p = state.Points[i]; skin.damagePoints[i] = new Vector4(p.X, p.Y, p.Intensity, p.Kind); skin.damagePointTimeStamps[i] = now - p.Age;
                }
                else { skin.damagePoints[i] = new Vector4(0, 0, 0, 5); skin.damagePointTimeStamps[i] = now; }
            }
            skin.TrackWoundAge = state.TrackAge; skin.currentDamagePointCount = state.Points.Length;
            // Verified 1.27.17 Sync only sets fixed shader vectors/count/ages.
            skin.Sync(); return true;
        }
    }
}
