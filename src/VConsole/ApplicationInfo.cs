using System.Buffers.Binary;

namespace VConsole;

/// <summary>Application information from AINF.</summary>
/// <param name="ExecutableName">The executable base name, for example <c>cs2</c>.</param>
/// <param name="GameDirectory">The game directory name, for example <c>csgo</c>.</param>
internal sealed record ApplicationInfo(uint NameCrc, string ExecutableName, string GameDirectory)
{
    private const int MinimumLength = 76;

    /// <exception cref="InvalidDataException">The payload is too short.</exception>
    public static ApplicationInfo Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < MinimumLength)
        {
            throw new InvalidDataException($"AINF has {payload.Length} bytes. It must have at least {MinimumLength} bytes.");
        }

        return new ApplicationInfo(
            NameCrc: BinaryPrimitives.ReadUInt32BigEndian(payload),
            ExecutableName: PayloadText.Read(payload.Slice(12, 32), out _),
            GameDirectory: PayloadText.Read(payload.Slice(44, 32), out _));
    }
}
