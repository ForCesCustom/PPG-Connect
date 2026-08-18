using System;
using System.Collections.Generic;

namespace PPGTogether.BepInEx
{
    internal static class ProtocolSmokeTests
    {
        private static int Main()
        {
            byte[] packet = Wire.Pack(WireMessage.Cursor, WireChannel.Cursor, 42UL, 7, 9, 11, new byte[] { 1, 2, 3 });
            Envelope envelope;
            if (!Wire.TryUnpack(packet, out envelope) || envelope.Nonce != 42UL || envelope.PeerId != 7 || envelope.Payload.Length != 3)
                return 1;

            byte[] cursorPacket = CursorPayloadCodec.Encode(76561198000000000UL, -12.25f, 4.5f, 120f, -33f, 1, true);
            CursorPayload cursor;
            if (!CursorPayloadCodec.TryDecode(cursorPacket, out cursor) || cursor.SteamId != 76561198000000000UL || cursor.X != -12.25f || cursor.Y != 4.5f || cursor.VelocityX != 120f || cursor.VelocityY != -33f || cursor.Buttons != 1 || !cursor.UiBusy)
                return 2;

            byte[] truncated = new byte[cursorPacket.Length - 1];
            Buffer.BlockCopy(cursorPacket, 0, truncated, 0, truncated.Length);
            if (CursorPayloadCodec.TryDecode(truncated, out cursor)) return 3;

            HashSet<uint> uniqueColours = new HashSet<uint>();
            for (ushort peer = 0; peer < 8; peer++) uniqueColours.Add(CursorColorPalette.ForPeer(peer).Packed);
            if (uniqueColours.Count != 8) return 4;
            if (!CursorSequence.IsNewer(2, 1) || CursorSequence.IsNewer(1, 2) || !CursorSequence.IsNewer(0, uint.MaxValue)) return 5;

            byte[] envelopedCursor = Wire.Pack(WireMessage.Cursor, WireChannel.Cursor, 19UL, 0, 4, 6, cursorPacket);
            if (!Wire.TryUnpack(envelopedCursor, out envelope) || envelope.Type != WireMessage.Cursor || !CursorPayloadCodec.TryDecode(envelope.Payload, out cursor)) return 6;

            byte[] botCursorPacket = CursorPayloadCodec.Encode(0xF000000000000001UL, 8f, -6f, 0.25f, -0.5f, 0x81, false);
            if (!CursorPayloadCodec.TryDecode(botCursorPacket, out cursor) || cursor.SteamId != 0xF000000000000001UL || cursor.Buttons != 0x81 || cursor.UiBusy) return 7;
            byte[] botModePacket = Wire.Pack(WireMessage.BotMode, WireChannel.Control, 20UL, 60000, 5, 7, new byte[] { 1, 3 });
            if (!Wire.TryUnpack(botModePacket, out envelope) || envelope.Type != WireMessage.BotMode || envelope.Channel != WireChannel.Control || envelope.PeerId != 60000 || envelope.Payload.Length != 2) return 8;
            byte[] spawnRequestPacket = Wire.Pack(WireMessage.SpawnRequest, WireChannel.World, 21UL, 2, 6, 8, new byte[] { 5, 0, 0, 0 });
            if (!Wire.TryUnpack(spawnRequestPacket, out envelope) || envelope.Type != WireMessage.SpawnRequest || envelope.Channel != WireChannel.World || envelope.PeerId != 2) return 9;

            byte[] hostSettingsPacket = Wire.Pack(WireMessage.HostSettings, WireChannel.Control, 22UL, 0, 7, 9, new byte[] { 8, 3, 20, 244, 1, 20, 1, 1, 1, 1, 1, 36 });
            if (!Wire.TryUnpack(hostSettingsPacket, out envelope) || envelope.Type != WireMessage.HostSettings || envelope.Channel != WireChannel.Control || envelope.Payload.Length != 12) return 10;
            byte[] actionDeniedPacket = Wire.Pack(WireMessage.ActionDenied, WireChannel.World, 23UL, 0, 8, 10, new byte[] { 4, 0, 110, 111, 112, 101 });
            if (!Wire.TryUnpack(actionDeniedPacket, out envelope) || envelope.Type != WireMessage.ActionDenied || envelope.Channel != WireChannel.World || envelope.Payload.Length != 6) return 11;
            byte[] interactionPacket = Wire.Pack(WireMessage.InteractionRequest, WireChannel.World, 24UL, 2, 9, 11, new byte[] { 1, 7, 0, 0, 0, 0, 0, 0, 0 });
            if (!Wire.TryUnpack(interactionPacket, out envelope) || envelope.Type != WireMessage.InteractionRequest || envelope.Channel != WireChannel.World || envelope.Payload.Length != 9) return 12;

            Random random = new Random(1729);
            for (int i = 0; i < 10000; i++)
            {
                byte[] junk = new byte[random.Next(0, 2048)];
                random.NextBytes(junk);
                try { Wire.TryUnpack(junk, out envelope); }
                catch { return 12; }
                try { CursorPayloadCodec.TryDecode(junk, out cursor); }
                catch { return 13; }
            }

            Writer writer = new Writer(8);
            writer.Float(float.NaN);
            Reader reader = new Reader(writer.ToArray());
            float value;
            if (reader.Float(out value)) return 14;

            HostActivationController activations = new HostActivationController();
            string denial;
            if (!activations.TryBegin(1, 99UL, 10, out denial) || !string.IsNullOrEmpty(denial)) return 15;
            if (activations.TryBegin(2, 99UL, 10, out denial) || string.IsNullOrEmpty(denial)) return 16;
            if (!activations.Renew(1, 99UL, 20) || activations.Renew(2, 99UL, 20)) return 17;
            int continuousCalls = 0;
            activations.FixedUpdate(21, delegate(ulong id) { if (id == 99UL) continuousCalls++; });
            if (continuousCalls != 1) return 18;
            activations.End(1, 99UL);
            activations.FixedUpdate(22, delegate(ulong id) { continuousCalls++; });
            if (continuousCalls != 1 || activations.IsActive(99UL)) return 19;
            return 0;
        }
    }
}
