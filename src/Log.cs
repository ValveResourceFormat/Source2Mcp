using System.Globalization;

namespace Source2Mcp;

/// <summary>Writes server messages to standard error. Standard output stays free for a host that reads it.</summary>
internal static class Log
{
    public static void Info(string message) => Write("info", message);

    public static void Error(string message) => Write("error", message);

    private static void Write(string level, string message)
    {
        var time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Console.Error.WriteLine($"{time} {level}: {message}");
    }
}
