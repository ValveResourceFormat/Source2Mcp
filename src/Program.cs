using System.Globalization;
using Source2Mcp;

var port = 13339;

if (args.Length > 0)
{
    var isPortOption = args.Length == 2 && args[0] == "--port";

    if (!isPortOption || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
    {
        Console.Error.WriteLine("Usage: source2mcp [--port <port>]");
        return 2;
    }
}

var tools = new McpTools();

await using (tools.ConfigureAwait(false))
{
    using var server = new McpServer(port, tools);

    var exit = new TaskCompletionSource();

    Console.CancelKeyPress += (_, e) =>
    {
        // Stop the server in an orderly way. The game continues to run.
        e.Cancel = true;
        exit.TrySetResult();
    };

    await exit.Task.ConfigureAwait(false);
}

return 0;
