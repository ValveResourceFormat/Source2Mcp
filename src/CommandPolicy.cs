namespace Source2Mcp;

/// <summary>Refuses console commands that would leave the local session, or that have a tool of their own.</summary>
internal static class CommandPolicy
{
    /// <exception cref="ToolException">One of the commands is refused.</exception>
    public static void Check(string commands)
    {
        // The game client drops this input without a message, so a refusal is clearer than no output
        if (commands.Contains("jump", StringComparison.OrdinalIgnoreCase) && commands.Contains("attack", StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolException("The game drops console input that contains both 'jump' and 'attack'. Send them in two calls.");
        }

        foreach (var command in Split(commands))
        {
            var name = GetName(command);

            switch (name)
            {
                case "connect":
                case "retry":
                case "redirect":
                    throw new ToolException($"'{name}' connects to a server. This server allows the local session only.");

                case "quit":
                case "exit":
                case "_restart":
                    throw new ToolException($"'{name}' stops the game process. Use the quit tool.");

                case "map":
                case "changelevel":
                    throw new ToolException($"'{name}' loads a map. Use the map tool, which waits until the map is loaded.");

                case "playdemo":
                case "playcast":
                    if (command.Contains("://", StringComparison.Ordinal))
                    {
                        throw new ToolException($"'{name}' with a URL downloads from a server. This server allows local files only.");
                    }

                    break;
            }
        }
    }

    /// <summary>Splits the text at each ';' outside double quotes, as the console does.</summary>
    private static List<string> Split(string commands)
    {
        var result = new List<string>();
        var start = 0;
        var inQuotes = false;

        for (var i = 0; i < commands.Length; i++)
        {
            var c = commands[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if ((c == ';' || c == '\n') && !inQuotes)
            {
                result.Add(commands[start..i]);
                start = i + 1;
            }
        }

        result.Add(commands[start..]);
        return result;
    }

    /// <summary>The first word of a command, in lowercase, without quotes and without a leading '+'.</summary>
    private static string GetName(string command)
    {
        var text = command.AsSpan().Trim();
        var end = text.IndexOfAny(' ', '\t');

        if (end >= 0)
        {
            text = text[..end];
        }

        text = text.Trim('"').TrimStart('+');

        return text.ToString().ToLowerInvariant();
    }
}
