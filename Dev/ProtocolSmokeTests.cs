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

            Writer mapStatusWriter = new Writer(32);
            mapStatusWriter.Byte(3);
            mapStatusWriter.String("substructure");
            byte[] mapStatusPacket = Wire.Pack(WireMessage.ClientMapStatus, WireChannel.Control, 26UL, 2, 11, 13, mapStatusWriter.ToArray());
            if (!Wire.TryUnpack(mapStatusPacket, out envelope) || envelope.Type != WireMessage.ClientMapStatus || envelope.Channel != WireChannel.Control) return 13;
            Reader mapStatusReader = new Reader(envelope.Payload);
            byte mapStatus;
            string mapIdentity;
            if (!mapStatusReader.Byte(out mapStatus) || !mapStatusReader.String(out mapIdentity) || mapStatusReader.Remaining != 0 || mapStatus != 3 || mapIdentity != "substructure") return 14;

            Writer rigWriter = new Writer(96);
            rigWriter.ULong(77UL);
            rigWriter.Byte(2);
            rigWriter.String("0/2/1");
            rigWriter.Float(2.5f);
            rigWriter.Float(-3.5f);
            rigWriter.Float(90f);
            rigWriter.Float(4f);
            rigWriter.Float(-5f);
            rigWriter.Float(6f);
            rigWriter.Bool(true);
            rigWriter.Bool(false);
            rigWriter.String("1/0");
            rigWriter.Float(8f);
            rigWriter.Float(9f);
            rigWriter.Float(10f);
            rigWriter.Float(11f);
            rigWriter.Float(12f);
            rigWriter.Float(13f);
            rigWriter.Bool(false);
            rigWriter.Bool(true);
            byte[] rigPacket = Wire.Pack(WireMessage.RigSnapshot, WireChannel.Snapshot, 27UL, 0, 12, 14, rigWriter.ToArray());
            if (!Wire.TryUnpack(rigPacket, out envelope) || envelope.Type != WireMessage.RigSnapshot || envelope.Channel != WireChannel.Snapshot) return 15;
            Reader rigReader = new Reader(envelope.Payload);
            ulong rigId;
            byte rigCount;
            string rigPath;
            float rigX; float rigY; float rigRotation; float rigVx; float rigVy; float rigAngular; bool rigSimulated; bool rigSleeping;
            if (!rigReader.ULong(out rigId) || !rigReader.Byte(out rigCount) || !rigReader.String(out rigPath) || !rigReader.Float(out rigX) || !rigReader.Float(out rigY) || !rigReader.Float(out rigRotation) || !rigReader.Float(out rigVx) || !rigReader.Float(out rigVy) || !rigReader.Float(out rigAngular) || !rigReader.Bool(out rigSimulated) || !rigReader.Bool(out rigSleeping) || rigId != 77UL || rigCount != 2 || rigPath != "0/2/1" || rigX != 2.5f || rigY != -3.5f || rigRotation != 90f || rigVx != 4f || rigVy != -5f || rigAngular != 6f || !rigSimulated || rigSleeping) return 16;
            if (!rigReader.String(out rigPath) || !rigReader.Float(out rigX) || !rigReader.Float(out rigY) || !rigReader.Float(out rigRotation) || !rigReader.Float(out rigVx) || !rigReader.Float(out rigVy) || !rigReader.Float(out rigAngular) || !rigReader.Bool(out rigSimulated) || !rigReader.Bool(out rigSleeping) || rigReader.Remaining != 0 || rigPath != "1/0" || rigX != 8f || rigY != 9f || rigRotation != 10f || rigVx != 11f || rigVy != 12f || rigAngular != 13f || rigSimulated || !rigSleeping) return 16;

            Random random = new Random(1729);
            for (int i = 0; i < 10000; i++)
            {
                byte[] junk = new byte[random.Next(0, 2048)];
                random.NextBytes(junk);
                try { Wire.TryUnpack(junk, out envelope); }
                catch { return 17; }
                try { CursorPayloadCodec.TryDecode(junk, out cursor); }
                catch { return 18; }
            }

            Writer writer = new Writer(8);
            writer.Float(float.NaN);
            Reader reader = new Reader(writer.ToArray());
            float value;
            if (reader.Float(out value)) return 19;

            HostActivationController activations = new HostActivationController();
            string denial;
            if (!activations.TryBegin(1, 99UL, 10, out denial) || !string.IsNullOrEmpty(denial)) return 20;
            if (activations.TryBegin(2, 99UL, 10, out denial) || string.IsNullOrEmpty(denial)) return 21;
            if (!activations.Renew(1, 99UL, 20) || activations.Renew(2, 99UL, 20)) return 22;
            int continuousCalls = 0;
            activations.FixedUpdate(21, delegate(ulong id) { if (id == 99UL) continuousCalls++; });
            if (continuousCalls != 1) return 23;
            activations.End(1, 99UL);
            activations.FixedUpdate(22, delegate(ulong id) { continuousCalls++; });
            if (continuousCalls != 1 || activations.IsActive(99UL)) return 24;
            return 0;
        }
    }
}
