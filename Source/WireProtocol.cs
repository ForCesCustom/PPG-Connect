using System;
using System.Text;

namespace PPGTogether.BepInEx
{
    internal enum WireMessage : byte
    {
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        Cursor = 4,
        GrabBegin = 5,
        GrabGranted = 6,
        GrabDenied = 7,
        GrabUpdate = 8,
        GrabEnd = 9,
        Snapshot = 10,
        SpawnRequest = 11,
        Spawn = 12,
        Despawn = 13,
        SessionStarted = 14,
        SessionEnding = 15,
        Ping = 16,
        Pong = 17,
        // Reliable host-to-client notification. Bot cursor movement remains on
        // the existing sequenced Cursor channel.
        BotMode = 18,
        // Reliable host-to-client settings snapshot. It lets clients inspect
        // the active server policy without granting them authority to change it.
        HostSettings = 19,
        // A request was declined but the relay session remains healthy. This is
        // deliberately distinct from Reject, which terminates a bad handshake.
        ActionDenied = 20,
        // Client requests a bounded, host-validated vanilla interaction on one
        // registered object: Activate or Delete.
        InteractionRequest = 21,
        // Reliable host-to-client map command.  The identity comes from the
        // host's local MapLoaderBehaviour and is resolved only against the
        // client's already-installed map catalogue.
        MapLoad = 22
    }

    internal enum WireChannel : byte
    {
        Control = 0,
        World = 1,
        Snapshot = 2,
        Cursor = 3
    }

    internal static class Wire
    {
        internal const uint Magic = 0x54475050;
        internal const ushort ProtocolVersion = 4;
        internal const int HeaderSize = 30;
        internal const int MaxPacketBytes = 49152;
        internal const int MaxStringBytes = 256;

        internal static byte[] Pack(WireMessage type, WireChannel channel, ulong nonce, ushort peerId, uint sequence, uint tick, byte[] payload)
        {
            if (payload == null)
                payload = new byte[0];
            if (payload.Length > MaxPacketBytes - HeaderSize)
                throw new InvalidOperationException("Packet payload exceeds limit.");
            Writer writer = new Writer(HeaderSize + payload.Length);
            writer.UInt(Magic);
            writer.UShort(ProtocolVersion);
            writer.Byte((byte)type);
            writer.Byte((byte)channel);
            writer.ULong(nonce);
            writer.UShort(peerId);
            writer.UInt(sequence);
            writer.UInt(tick);
            writer.UInt((uint)payload.Length);
            writer.Raw(payload);
            return writer.ToArray();
        }

        internal static bool TryUnpack(byte[] bytes, out Envelope value)
        {
            value = new Envelope();
            if (bytes == null || bytes.Length < HeaderSize || bytes.Length > MaxPacketBytes)
                return false;
            Reader reader = new Reader(bytes);
            uint magic;
            ushort version;
            byte type;
            byte channel;
            uint length;
            if (!reader.UInt(out magic) || magic != Magic || !reader.UShort(out version) || version != ProtocolVersion ||
                !reader.Byte(out type) || !reader.Byte(out channel) || !reader.ULong(out value.Nonce) ||
                !reader.UShort(out value.PeerId) || !reader.UInt(out value.Sequence) || !reader.UInt(out value.Tick) ||
                !reader.UInt(out length) || length != reader.Remaining || !Enum.IsDefined(typeof(WireMessage), type) ||
                !Enum.IsDefined(typeof(WireChannel), channel))
                return false;
            value.Type = (WireMessage)type;
            value.Channel = (WireChannel)channel;
            return reader.Raw((int)length, out value.Payload) && reader.Remaining == 0;
        }
    }

    internal struct Envelope
    {
        internal WireMessage Type;
        internal WireChannel Channel;
        internal ulong Nonce;
        internal ushort PeerId;
        internal uint Sequence;
        internal uint Tick;
        internal byte[] Payload;
    }

    internal sealed class Writer
    {
        private byte[] buffer;
        private int position;

        internal Writer(int size)
        {
            buffer = new byte[Math.Max(64, size)];
        }

        internal void Byte(byte value) { Ensure(1); buffer[position++] = value; }
        internal void UShort(ushort value) { Ensure(2); buffer[position++] = (byte)value; buffer[position++] = (byte)(value >> 8); }
        internal void UInt(uint value) { Ensure(4); buffer[position++] = (byte)value; buffer[position++] = (byte)(value >> 8); buffer[position++] = (byte)(value >> 16); buffer[position++] = (byte)(value >> 24); }
        internal void ULong(ulong value) { UInt((uint)value); UInt((uint)(value >> 32)); }
        internal void Float(float value) { Raw(BitConverter.GetBytes(value)); }
        internal void Bool(bool value) { Byte(value ? (byte)1 : (byte)0); }

        internal void String(string value)
        {
            if (value == null)
                value = string.Empty;
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            if (utf8.Length > Wire.MaxStringBytes)
                throw new InvalidOperationException("String exceeds protocol limit.");
            UShort((ushort)utf8.Length);
            Raw(utf8);
        }

        internal void Raw(byte[] value)
        {
            if (value == null)
                return;
            Ensure(value.Length);
            Buffer.BlockCopy(value, 0, buffer, position, value.Length);
            position += value.Length;
        }

        internal byte[] ToArray()
        {
            byte[] copy = new byte[position];
            Buffer.BlockCopy(buffer, 0, copy, 0, position);
            return copy;
        }

        private void Ensure(int count)
        {
            if (count < 0 || position > Wire.MaxPacketBytes - count)
                throw new InvalidOperationException("Packet exceeds limit.");
            int required = position + count;
            if (required <= buffer.Length)
                return;
            int next = Math.Min(Wire.MaxPacketBytes, Math.Max(required, buffer.Length * 2));
            byte[] replacement = new byte[next];
            Buffer.BlockCopy(buffer, 0, replacement, 0, position);
            buffer = replacement;
        }
    }

    internal sealed class Reader
    {
        private readonly byte[] buffer;
        private int position;

        internal Reader(byte[] buffer) { this.buffer = buffer; }
        internal int Remaining { get { return buffer.Length - position; } }

        internal bool Byte(out byte value)
        {
            value = 0;
            if (Remaining < 1) return false;
            value = buffer[position++]; return true;
        }
        internal bool UShort(out ushort value)
        {
            value = 0;
            if (Remaining < 2) return false;
            value = (ushort)(buffer[position] | (buffer[position + 1] << 8)); position += 2; return true;
        }
        internal bool UInt(out uint value)
        {
            value = 0;
            if (Remaining < 4) return false;
            value = (uint)(buffer[position] | (buffer[position + 1] << 8) | (buffer[position + 2] << 16) | (buffer[position + 3] << 24)); position += 4; return true;
        }
        internal bool ULong(out ulong value)
        {
            value = 0; uint low; uint high;
            if (!UInt(out low) || !UInt(out high)) return false;
            value = low | ((ulong)high << 32); return true;
        }
        internal bool Float(out float value)
        {
            value = 0f;
            if (Remaining < 4) return false;
            value = BitConverter.ToSingle(buffer, position); position += 4;
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
        internal bool Bool(out bool value)
        {
            value = false; byte raw;
            if (!Byte(out raw) || raw > 1) return false;
            value = raw != 0; return true;
        }
        internal bool String(out string value)
        {
            value = string.Empty; ushort length;
            if (!UShort(out length) || length > Wire.MaxStringBytes || Remaining < length) return false;
            value = Encoding.UTF8.GetString(buffer, position, length); position += length; return true;
        }
        internal bool Raw(int count, out byte[] value)
        {
            value = null;
            if (count < 0 || count > Remaining) return false;
            value = new byte[count];
            if (count > 0) Buffer.BlockCopy(buffer, position, value, 0, count);
            position += count; return true;
        }
    }
}
