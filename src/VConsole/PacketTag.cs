using System.Buffers.Binary;
using System.Text;

namespace VConsole;

/// <summary>
/// The packet tags that this library sends or reads. A tag is 4 ASCII characters, stored in a <see cref="uint"/> with the first character in the low byte.
/// </summary>
internal static class PacketTag
{
    public const uint Ainf = 'A' | ('I' << 8) | ('N' << 16) | ('F' << 24);
    public const uint Chan = 'C' | ('H' << 8) | ('A' << 16) | ('N' << 24);
    public const uint Prnt = 'P' | ('R' << 8) | ('N' << 16) | ('T' << 24);
    public const uint Cvrb = 'C' | ('V' << 8) | ('R' << 16) | ('B' << 24);
    public const uint Cmnd = 'C' | ('M' << 8) | ('N' << 16) | ('D' << 24);
    public const uint Ping = 'P' | ('I' << 8) | ('N' << 16) | ('G' << 24);
    public const uint Pong = 'P' | ('O' << 8) | ('N' << 16) | ('G' << 24);
    public const uint Bugs = 'B' | ('U' << 8) | ('G' << 16) | ('S' << 24);

    /// <summary>Returns the 4 characters of a tag, for logs and error messages.</summary>
    public static string ToString(uint tag)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, tag);

        return Encoding.Latin1.GetString(bytes);
    }
}

/// <summary>The version that each tag must have in the packet header.</summary>
internal static class PacketVersion
{
    /// <summary>The version of most tags in current builds. CMND uses the version of the AINF that the game sends.</summary>
    public const ushort Default = 212;

    public const ushort Ping = 1;

    public const ushort Bugs = 3;
}
