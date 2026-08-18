using System;

namespace PPGTogether.BepInEx
{
    internal struct CursorPayload
    {
        internal ulong SteamId;
        internal float X;
        internal float Y;
        internal float VelocityX;
        internal float VelocityY;
        internal byte Buttons;
        internal bool UiBusy;
    }

    internal static class CursorPayloadCodec
    {
        internal static byte[] Encode(ulong steamId, float x, float y, float velocityX, float velocityY, byte buttons, bool uiBusy)
        {
            Writer writer = new Writer(40);
            writer.ULong(steamId);
            writer.Float(x);
            writer.Float(y);
            writer.Float(velocityX);
            writer.Float(velocityY);
            writer.Byte(buttons);
            writer.Bool(uiBusy);
            return writer.ToArray();
        }

        internal static bool TryDecode(byte[] bytes, out CursorPayload value)
        {
            value = new CursorPayload();
            if (bytes == null) return false;
            Reader reader = new Reader(bytes);
            return reader.ULong(out value.SteamId) &&
                   reader.Float(out value.X) && reader.Float(out value.Y) &&
                   reader.Float(out value.VelocityX) && reader.Float(out value.VelocityY) &&
                   reader.Byte(out value.Buttons) && reader.Bool(out value.UiBusy) && reader.Remaining == 0;
        }
    }

    internal static class CursorSequence
    {
        internal static bool IsNewer(uint received, uint current)
        {
            return received != current && (received - current) < 0x80000000u;
        }
    }

    internal struct CursorColorRgb
    {
        internal readonly byte R;
        internal readonly byte G;
        internal readonly byte B;

        internal CursorColorRgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
        internal uint Packed { get { return ((uint)R << 16) | ((uint)G << 8) | B; } }
    }

    internal static class CursorColorPalette
    {
        // Peer zero is the host. The lobby maximum is eight, so peer IDs 0..7
        // deliberately receive eight distinct high-contrast colours.
        private static readonly CursorColorRgb[] Colors =
        {
            new CursorColorRgb(255, 199, 54),  // host gold
            new CursorColorRgb(51, 225, 255),  // cyan
            new CursorColorRgb(255, 84, 157),  // pink
            new CursorColorRgb(146, 245, 92),  // lime
            new CursorColorRgb(183, 119, 255), // violet
            new CursorColorRgb(255, 148, 61),  // orange
            new CursorColorRgb(81, 140, 255),  // blue
            new CursorColorRgb(255, 92, 92)    // red
        };

        internal static CursorColorRgb ForPeer(ushort peerId)
        {
            return Colors[peerId % Colors.Length];
        }
    }
}
