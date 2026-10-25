using System.Text;

namespace VConsole;

/// <summary>Reads and writes the text fields of payloads. Invalid UTF-8 becomes U+FFFD and does not throw.</summary>
internal static class PayloadText
{
    /// <summary>
    /// Reads a NUL-terminated string from the start of <paramref name="data"/>. Without a NUL, the string ends at the end of the data.
    /// <paramref name="consumed"/> includes the NUL. In a fixed-size field, the bytes after the NUL can be garbage.
    /// </summary>
    public static string Read(ReadOnlySpan<byte> data, out int consumed)
    {
        var end = data.IndexOf((byte)0);

        if (end < 0)
        {
            consumed = data.Length;
            return Encoding.UTF8.GetString(data);
        }

        consumed = end + 1;
        return Encoding.UTF8.GetString(data[..end]);
    }

    /// <summary>Returns the text as UTF-8 with a NUL at the end.</summary>
    public static byte[] ToNullTerminated(string text)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(text) + 1];
        Encoding.UTF8.GetBytes(text, bytes);

        return bytes;
    }
}
