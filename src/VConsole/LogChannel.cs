using System.Buffers.Binary;

namespace VConsole;

/// <summary>One log channel from CHAN.</summary>
/// <param name="Id">CRC-32 of the name with its NUL byte.</param>
internal sealed record LogChannel(uint Id, string Name)
{
    private const int EntrySize = 58;

    /// <summary>The ID of the channel <c>Console</c>, which has the output of <c>echo</c>.</summary>
    public const uint ConsoleId = 0x094098AD;

    /// <exception cref="InvalidDataException">The payload is shorter than its entry count specifies.</exception>
    public static List<LogChannel> ParseList(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
        {
            throw new InvalidDataException($"CHAN has {payload.Length} bytes. It must have at least 2 bytes.");
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(payload);
        var entries = payload[2..];

        if (entries.Length < count * EntrySize)
        {
            throw new InvalidDataException($"CHAN has {count} entries in {entries.Length} bytes. Each entry has {EntrySize} bytes.");
        }

        var channels = new List<LogChannel>(count);

        for (var i = 0; i < count; i++)
        {
            var entry = entries.Slice(i * EntrySize, EntrySize);
            channels.Add(new LogChannel(BinaryPrimitives.ReadUInt32BigEndian(entry), PayloadText.Read(entry.Slice(24, 32), out _)));
        }

        return channels;
    }
}
