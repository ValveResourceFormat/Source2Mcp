using System.Buffers;
using System.Buffers.Binary;

namespace VConsole;

/// <summary>One packet without its header.</summary>
internal readonly record struct Packet(uint Tag, ushort Version, ReadOnlyMemory<byte> Payload);

/// <summary>Reads and writes the 12-byte packet header.</summary>
internal static class PacketFormat
{
    public const int HeaderSize = 12;

    /// <summary>The largest packet that the server sends, header included.</summary>
    public const int MaxLength = 8 * 1024 * 1024;

    /// <summary>
    /// Removes the first complete packet from <paramref name="buffer"/>.
    /// Returns false when the buffer does not hold a complete packet yet.
    /// </summary>
    /// <exception cref="InvalidDataException">The length field is less than 12 or more than <see cref="MaxLength"/>.</exception>
    public static bool TryRead(ref ReadOnlySequence<byte> buffer, out Packet packet)
    {
        packet = default;

        if (buffer.Length < HeaderSize)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        buffer.Slice(0, HeaderSize).CopyTo(header);

        var tag = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var version = BinaryPrimitives.ReadUInt16BigEndian(header[4..]);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header[6..]);

        // The handle at offset 10 has no meaning, so it is not read
        if (length is < HeaderSize or > MaxLength)
        {
            throw new InvalidDataException($"Packet '{PacketTag.ToString(tag)}' has length {length}. The length must be from {HeaderSize} to {MaxLength}.");
        }

        if (buffer.Length < length)
        {
            return false;
        }

        var payload = buffer.Slice(HeaderSize, length - HeaderSize).ToArray();
        packet = new Packet(tag, version, payload);
        buffer = buffer.Slice(length);

        return true;
    }

    /// <summary>Returns a packet with its header, ready to send.</summary>
    public static byte[] Create(uint tag, ushort version, ReadOnlySpan<byte> payload)
    {
        var length = HeaderSize + payload.Length;

        if (length >= MaxLength)
        {
            throw new ArgumentException($"The payload is {payload.Length} bytes. A packet must be less than {MaxLength} bytes.", nameof(payload));
        }

        var packet = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, tag);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), version);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(6), (uint)length);

        // The handle at offset 10 stays 0
        payload.CopyTo(packet.AsSpan(HeaderSize));

        return packet;
    }
}
