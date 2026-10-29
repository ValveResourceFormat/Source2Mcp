using System.Globalization;
using VConsole;

namespace Source2Mcp;

/// <summary>A cvar that a console command sets.</summary>
/// <param name="Value">The requested value, or null when the command changes the value in another way, as toggle does.</param>
internal sealed record ConVarSet(string Name, string? Value);

/// <summary>Reads cvar sets from console commands, and cvar values and errors from console output.</summary>
internal static class ConVarText
{
    private const string UnknownCommandPrefix = "Unknown command";

    /// <summary>
    /// Finds the commands that set a cvar: <c>name value</c>, <c>toggle name</c> and <c>incrementvar name</c>. A name that is not
    /// in <paramref name="conVars"/> is also returned, because the game does not send hidden cvars. When the list is empty,
    /// a cvar cannot be told apart from a command, so nothing is returned.
    /// </summary>
    public static List<ConVarSet> FindSets(string commands, IReadOnlyDictionary<string, ConVar> conVars)
    {
        var sets = new List<ConVarSet>();

        if (conVars.Count == 0)
        {
            return sets;
        }

        foreach (var command in CommandPolicy.Split(commands))
        {
            var (name, arguments) = SplitName(command);

            if (name.Length == 0 || arguments.Length == 0 || name[0] is '+' or '-')
            {
                continue;
            }

            if (name.Equals("toggle", StringComparison.OrdinalIgnoreCase) || name.Equals("incrementvar", StringComparison.OrdinalIgnoreCase))
            {
                var (target, _) = SplitName(arguments);

                if (IsPossibleConVar(target, conVars))
                {
                    sets.Add(new ConVarSet(target, null));
                }

                continue;
            }

            if (IsPossibleConVar(name, conVars))
            {
                sets.Add(new ConVarSet(name, Unquote(arguments)));
            }
        }

        return sets;
    }

    /// <summary>
    /// Finds the value of a cvar in the output of a query, which is the cvar name alone. The game prints
    /// <c>name = value</c>, or <c>"name" = "value" ( def. "default" ) ...</c>.
    /// </summary>
    public static string? FindValue(IReadOnlyList<LogLine> lines, string name)
    {
        var plainPrefix = name + " = ";
        var quotedPrefix = "\"" + name + "\" = \"";

        foreach (var line in lines)
        {
            var text = line.Text;

            if (text.StartsWith(plainPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return text[plainPrefix.Length..].Trim();
            }

            if (text.StartsWith(quotedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = text[quotedPrefix.Length..];
                var end = value.IndexOf('"', StringComparison.Ordinal);

                return end >= 0 ? value[..end] : value;
            }
        }

        return null;
    }

    /// <summary>The name in a line such as <c>Unknown command: name</c> or <c>Unknown command 'name'!</c>, or null for another line.</summary>
    public static string? GetUnknownCommand(string text)
    {
        if (!text.StartsWith(UnknownCommandPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var name = text[UnknownCommandPrefix.Length..].TrimStart(':', ' ').TrimEnd('!', ' ').Trim('\'');

        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// True when two values are the same cvar value. The game prints numbers with more decimals than the request,
    /// and booleans as true and false.
    /// </summary>
    public static bool ValuesEqual(string a, string b)
    {
        if (TryParseNumber(a, out var numberA) && TryParseNumber(b, out var numberB))
        {
            var tolerance = 1e-4 * Math.Max(1, Math.Abs(numberA));
            return Math.Abs(numberA - numberB) <= tolerance;
        }

        return string.Equals(Unquote(a), Unquote(b), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseNumber(string text, out double value)
    {
        var trimmed = Unquote(text);

        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            value = 1;
            return true;
        }

        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            value = 0;
            return true;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>A hidden cvar is not in the list, so a name that the list does not have can still be a cvar.</summary>
    private static bool IsPossibleConVar(string name, IReadOnlyDictionary<string, ConVar> conVars)
    {
        if (name.Length == 0)
        {
            return false;
        }

        return !conVars.TryGetValue(name, out var conVar) || !conVar.IsCommand;
    }

    /// <summary>The first word of a command without quotes, and the rest of the command.</summary>
    private static (string Name, string Arguments) SplitName(string command)
    {
        var text = command.Trim();
        var end = text.IndexOfAny([' ', '\t']);

        if (end < 0)
        {
            return (text.Trim('"'), string.Empty);
        }

        return (text[..end].Trim('"'), text[(end + 1)..].Trim());
    }

    private static string Unquote(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }
}
