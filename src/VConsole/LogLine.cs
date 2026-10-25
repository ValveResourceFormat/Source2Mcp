using System.Buffers.Binary;

namespace VConsole;

/// <summary>The severity of a log line.</summary>
internal enum LogSeverity
{
    MoreDetailed = 0,
    Detailed = 1,
    Message = 2,
    Warning = 3,
    Assert = 4,
    Error = 5,
}

/// <summary>One log line from PRNT.</summary>
/// <param name="Sequence">Position of the line in the stream of this connection, from 1.</param>
/// <param name="Channel">The channel name, or the channel ID in hexadecimal when CHAN did not name it.</param>
/// <param name="Time">Milliseconds since the game process started.</param>
/// <param name="Text">The line without its last newline.</param>
/// <param name="IsHistory">The line is part of the history replay, so it is older than the connection.</param>
internal sealed record LogLine(long Sequence, uint ChannelId, string Channel, LogSeverity Severity, uint Time, string Text, bool IsHistory);

/// <summary>The fields of a PRNT payload that this library uses.</summary>
internal readonly record struct PrintMessage(uint ChannelId, LogSeverity Severity, uint Time, string Text)
{
    private const int TextOffset = 28;

    /// <exception cref="InvalidDataException">The payload is too short.</exception>
    public static PrintMessage Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < TextOffset)
        {
            throw new InvalidDataException($"PRNT has {payload.Length} bytes. It must have at least {TextOffset} bytes.");
        }

        // Offset 20 is the metadata size and offset 24 can be garbage. The metadata follows the text, so the text is read up to its NUL.
        var text = PayloadText.Read(payload[TextOffset..], out _);

        if (text.EndsWith('\n'))
        {
            text = text[..^1];
        }

        return new PrintMessage(
            ChannelId: BinaryPrimitives.ReadUInt32BigEndian(payload),
            Severity: (LogSeverity)BinaryPrimitives.ReadInt32BigEndian(payload[8..]),
            Time: BinaryPrimitives.ReadUInt32BigEndian(payload[16..]),
            Text: text);
    }
}
