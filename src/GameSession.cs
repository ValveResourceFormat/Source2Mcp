using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using VConsole;

namespace Source2Mcp;

/// <summary>One game process that this server started, and its VConsole2 connection.</summary>
internal sealed class GameSession : IAsyncDisposable
{
    // The client writes this line when its connection to its local server is complete and the player is in the game.
    // The client connects to the local server at 127.0.0.1, not to loopback. The line ends with "server" in CS2, Deadlock
    // and Dota 2, and with "client" in Half-Life: Alyx.
    private const string ReadyLine = "CL:  Signon traffic \"";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan FirstPacketTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan CameraStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IntroTimeout = TimeSpan.FromSeconds(5);

    private static readonly string[] CounterStrikeSetupCommands =
    [
        // Set this before the bots go, because the kick of the last bot of a team ends the round
        "mp_ignore_round_win_conditions 1",
        "bot_quota 0",
        "bot_kick",
        "mp_autokick 0",
        "mp_freezetime 0",
        "mp_team_intro_time 0",
        "mp_respawn_on_death_t 1",
        "mp_respawn_on_death_ct 1",
        "sv_regeneration_force_on 1",
        "sv_falldamage_scale 0",
        "jointeam 2 2",
        "mp_warmup_end",

        // The restart puts the player at a spawn point, with the new settings
        "mp_restartgame 1",
    ];

    private static readonly string[] DotaSetupCommands =
    [
        "dota_creeps_no_spawning 1",

        // The fog of war darkens the map
        "dota_all_vision 1",

        // The player is on no team, so the game starts with no hero
        "dota_dev forcegamestart",
    ];

    private bool _hideoutIntroRemoved;

    private GameSession(GameProfile profile, string gamePath, Process process, int port, string commandLine, VConsoleClient client)
    {
        Profile = profile;
        GamePath = gamePath;
        Process = process;
        Port = port;
        CommandLine = commandLine;
        Client = client;
    }

    public GameProfile Profile { get; }

    /// <summary>The <c>game</c> folder of the installation.</summary>
    public string GamePath { get; }

    public Process Process { get; }

    public int Port { get; }

    public string CommandLine { get; }

    public VConsoleClient Client { get; }

    /// <summary>Starts the game and connects to VConsole2. Use <see cref="WaitUntilReadyAsync"/> next.</summary>
    public static async Task<GameSession> StartAsync(GameProfile profile, string? map, int width, int height, bool tools, CancellationToken cancellationToken)
    {
        var gamePath = profile.GetGamePath();
        var executable = profile.GetExecutablePath(gamePath);

        if (!File.Exists(executable))
        {
            throw new ToolException($"{executable} does not exist. Verify the game files in Steam.");
        }

        if (map != null)
        {
            profile.ThrowIfNoMap(gamePath, map);
        }

        var running = Process.GetProcessesByName(profile.ExecutableName);

        if (running.Length > 0)
        {
            var id = running[0].Id;

            foreach (var process in running)
            {
                process.Dispose();
            }

            throw new ToolException($"{profile.ExecutableName}.exe already runs as process {id}. Close it first.");
        }

        var port = FindFreePort(SocketType.Stream, ProtocolType.Tcp);

        // Every game uses UDP 27015 by default. A second game on that port takes the connection of the client to its local server.
        var hostPort = FindFreePort(SocketType.Dgram, ProtocolType.Udp);

        List<string> arguments =
        [
            "-insecure",
            "-sw",
            "-w", width.ToString(CultureInfo.InvariantCulture),
            "-h", height.ToString(CultureInfo.InvariantCulture),
            "-vconport", port.ToString(CultureInfo.InvariantCulture),
            "+hostport", hostPort.ToString(CultureInfo.InvariantCulture),
            "+sv_cheats", "1",
        ];

        arguments.AddRange(profile.ExtraArguments);

        if (tools)
        {
            arguments.Add("-tools");

            // Tools mode stops rendering while the game window has no focus
            arguments.Add("+r_always_render_all_windows");
            arguments.Add("1");
        }

        if (map != null)
        {
            arguments.Add("+map");
            arguments.Add(map);
        }

        var commandLine = string.Join(' ', arguments);
        var startInfo = new ProcessStartInfo(executable, arguments) { WorkingDirectory = Path.GetDirectoryName(executable) };
        var game = Process.Start(startInfo) ?? throw new ToolException($"{executable} did not start.");

        Log.Info($"Started {profile.ExecutableName} as process {game.Id}: {commandLine}");

        try
        {
            var client = await ConnectAsync(game, port, cancellationToken).ConfigureAwait(false);

            return new GameSession(profile, gamePath, game, port, commandLine, client);
        }
        catch
        {
            Stop(game);
            game.Dispose();
            throw;
        }
    }

    /// <summary>Throws when the game process exited or the connection closed.</summary>
    public void ThrowIfStopped()
    {
        if (Process.HasExited)
        {
            throw new ToolException($"The game exited with code {Process.ExitCode}. Use launch to start it again.");
        }

        if (!Client.IsConnected)
        {
            throw new ToolException("The VConsole2 connection to the game closed. Use quit, then launch.");
        }
    }

    /// <summary>Loads a map and waits until the player is in the game.</summary>
    public async Task LoadMapAsync(string map, CancellationToken cancellationToken)
    {
        Profile.ThrowIfNoMap(GamePath, map);

        // The ready line of the previous map is already in the log
        var afterSequence = Client.Log.LastSequence;

        await Client.SendCommandAsync("map " + map, cancellationToken).ConfigureAwait(false);
        await WaitUntilReadyAsync(map, afterSequence, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until the game runs commands. With a <paramref name="map"/>, it first waits for the ready line of the profile
    /// after the log line <paramref name="afterSequence"/>, which shows that the map is loaded.
    /// </summary>
    public async Task WaitUntilReadyAsync(string? map, long afterSequence, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ReadyTimeout);

        try
        {
            if (map != null)
            {
                await WaitForLineAsync(ReadyLine, afterSequence, timeoutSource.Token).ConfigureAwait(false);
                ThrowIfOtherMap(map, afterSequence);
            }

            // The marker round trip shows that the main thread runs frames and processes commands
            await RunAsync("echo source2mcp ready", timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowIfStopped();
            throw new ToolException($"The game did not become ready in {ReadyTimeout.TotalMinutes} minutes. Read the log for errors.");
        }
    }

    /// <summary>True when <see cref="RunSetupAsync"/> has setup steps for the next map.</summary>
    public bool HasSetup()
    {
        switch (Profile.Game)
        {
            case Game.Cs2:
            case Game.Dota2:
                return true;

            case Game.Hlvr:
                return false;

            case Game.Deadlock:
                // The hideout intro plays only one time in each process
                return !_hideoutIntroRemoved;

            default:
                throw new UnreachableException();
        }
    }

    /// <summary>Makes a stable test environment on the map. Use it only when <see cref="HasSetup"/> is true.</summary>
    public async Task RunSetupAsync(CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(SetupTimeout);

        try
        {
            switch (Profile.Game)
            {
                case Game.Cs2:
                    await SetUpCounterStrikeAsync(timeoutSource.Token).ConfigureAwait(false);
                    break;

                case Game.Deadlock:
                    await RemoveHideoutIntroAsync(timeoutSource.Token).ConfigureAwait(false);
                    break;

                case Game.Dota2:
                    await SetUpDotaAsync(timeoutSource.Token).ConfigureAwait(false);
                    break;
            }

            // The marker round trip shows that the frame with the last setup change is complete
            await RunAsync("echo source2mcp setup done", timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowIfStopped();
            throw new ToolException($"The setup did not complete in {SetupTimeout.TotalSeconds} s. Read get_log for errors. To skip the setup, set 'setup' to false.");
        }
    }

    /// <summary>Sends <c>quit</c>, waits for the process to exit, and stops it after a timeout. Returns the exit code.</summary>
    public async Task<int> QuitAsync(CancellationToken cancellationToken)
    {
        if (!Process.HasExited && Client.IsConnected)
        {
            try
            {
                await Client.SendCommandAsync("quit", cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The connection closed. The timeout below stops the process.
            }
        }

        try
        {
            await Process.WaitForExitAsync(cancellationToken).WaitAsync(QuitTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Log.Info($"Process {Process.Id} did not exit in {QuitTimeout.TotalSeconds} s. Stopping it.");
            Stop(Process);
            await Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }

        return Process.ExitCode;
    }

    /// <summary>Stops the process at once and closes the connection. Use it when the start fails.</summary>
    public async ValueTask AbortAsync()
    {
        Stop(Process);
        await DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync().ConfigureAwait(false);
        Process.Dispose();
    }

    /// <summary>Removes the bots, stops the round end, freeze time and fall damage, and puts the player alive on the T team.</summary>
    private async Task SetUpCounterStrikeAsync(CancellationToken cancellationToken)
    {
        // The log can already have the restart lines from the map load, so only lines after this sequence number count
        var afterSequence = Client.Log.LastSequence;

        await RunAsync(string.Join("; ", CounterStrikeSetupCommands), cancellationToken).ConfigureAwait(false);

        // jointeam also starts a match, so only the BeginMatch line after the restart message shows the end of the restart
        var restart = await WaitForLineAsync("The game will restart in", afterSequence, cancellationToken).ConfigureAwait(false);
        await WaitForLineAsync("BeginMatch", restart.Sequence, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops the creep spawns, removes the fog of war, and starts the game with no hero, hero selection or pregame time.</summary>
    private async Task SetUpDotaAsync(CancellationToken cancellationToken)
    {
        var afterSequence = Client.Log.LastSequence;

        await RunAsync(string.Join("; ", DotaSetupCommands), cancellationToken).ConfigureAwait(false);

        // forcegamestart also works while the game rules wait for the players to load
        await WaitForLineAsync("C:Gamerules: entering state 'DOTA_GAMERULES_STATE_GAME_IN_PROGRESS'", afterSequence, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The map dl_hideout gives the hero an intro modifier when the hero loads, about 1 s after the ready line. While the hero has it,
    /// the game shows a title view, and getpos reports the title camera. When the modifier goes, the client moves the camera
    /// to the hero over about 2 s. Other maps do not give the modifier.
    /// </summary>
    private async Task RemoveHideoutIntroAsync(CancellationToken cancellationToken)
    {
        const string Modifier = "modifier_citadel_hideout_intro";

        // No log line shows the new modifier, so the setup asks for the modifier list until it has the modifier
        var introStart = Stopwatch.GetTimestamp();

        while (!ContainsText(await RunAsync("modifier_dump player", cancellationToken).ConfigureAwait(false), Modifier))
        {
            if (Stopwatch.GetElapsedTime(introStart) > IntroTimeout)
            {
                return;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        await RunAsync($"modifier_remove {Modifier} player", cancellationToken).ConfigureAwait(false);
        _hideoutIntroRemoved = true;

        // The camera has stopped when two readings are the same
        var start = Stopwatch.GetTimestamp();
        var previous = await ReadCameraAsync(cancellationToken).ConfigureAwait(false);

        while (Stopwatch.GetElapsedTime(start) < CameraStopTimeout)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            var current = await ReadCameraAsync(cancellationToken).ConfigureAwait(false);

            if (current == previous)
            {
                return;
            }

            previous = current;
        }
    }

    /// <summary>Runs getpos and reads its output, for example <c>setpos 1.0 2.0 3.0;setang 0.0 90.0 0.0</c>.</summary>
    public async Task<(Vector3 Position, Vector3 Angles)> ReadCameraAsync(CancellationToken cancellationToken)
    {
        foreach (var line in await RunAsync("getpos", cancellationToken).ConfigureAwait(false))
        {
            if (!line.Text.StartsWith("setpos ", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Text.Split([' ', ';'], StringSplitOptions.RemoveEmptyEntries);

            // The parts are: setpos x y z setang pitch yaw roll
            if (parts.Length >= 8 && parts[4] == "setang")
            {
                return (
                    new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])),
                    new Vector3(ParseFloat(parts[5]), ParseFloat(parts[6]), ParseFloat(parts[7])));
            }
        }

        throw new ToolException("getpos gave no position. Make sure that a map is loaded and sv_cheats is 1.");
    }

    private static float ParseFloat(string text)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new ToolException($"getpos gave '{text}', which is not a number.");
        }

        return value;
    }

    /// <summary>
    /// Throws when the game reports a different map after the log line <paramref name="afterSequence"/>. When the Deadlock client
    /// cannot connect to its local server, it loads an empty map, and that map also writes the ready line.
    /// </summary>
    private void ThrowIfOtherMap(string map, long afterSequence)
    {
        const string Prefix = "Map: \"";

        var lines = Client.Log.GetLast(1, line => line.Sequence > afterSequence && line.Text.StartsWith(Prefix, StringComparison.Ordinal));

        if (lines.Count == 0)
        {
            return;
        }

        var loadedMap = lines[0].Text[Prefix.Length..].TrimEnd('"');

        if (!string.Equals(loadedMap, map, StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolException($"The game loaded '{loadedMap}', not '{map}'. The client did not connect to its local server. Read get_log for errors.");
        }
    }

    private static bool ContainsText(IReadOnlyList<LogLine> lines, string text)
    {
        foreach (var line in lines)
        {
            if (line.Text.Contains(text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Runs one command and returns its output. Throws when the game does not run the command in time.</summary>
    public async Task<IReadOnlyList<LogLine>> RunAsync(string command, CancellationToken cancellationToken)
    {
        var result = await Client.ExecuteAsync(command, CommandTimeout, TimeSpan.Zero, cancellationToken).ConfigureAwait(false);

        if (!result.Completed)
        {
            throw new ToolException($"The game did not run '{command}' in {CommandTimeout.TotalSeconds} s.");
        }

        return result.Lines;
    }

    /// <summary>Waits for a log line after <paramref name="afterSequence"/> that contains the text. The game exit closes the connection and stops the wait.</summary>
    private async Task<LogLine> WaitForLineAsync(string text, long afterSequence, CancellationToken cancellationToken)
    {
        try
        {
            return await Client.WaitForLineAsync(line => line.Sequence > afterSequence && line.Text.Contains(text, StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            ThrowIfStopped();
            throw;
        }
    }

    private static async Task<VConsoleClient> ConnectAsync(Process game, int port, CancellationToken cancellationToken)
    {
        var endPoint = new IPEndPoint(IPAddress.Loopback, port);
        var start = Stopwatch.GetTimestamp();

        while (true)
        {
            if (game.HasExited)
            {
                throw new ToolException($"The game exited with code {game.ExitCode} before VConsole2 accepted a connection.");
            }

            try
            {
                return await VConsoleClient.ConnectAsync(endPoint, FirstPacketTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException) when (Stopwatch.GetElapsedTime(start) < ConnectTimeout)
            {
                // The game does not listen yet
            }
            catch (SocketException e)
            {
                throw new ToolException($"VConsole2 did not accept a connection on port {port} in {ConnectTimeout.TotalSeconds} s: {e.Message}");
            }

            await Task.Delay(RetryInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Returns a port that no process holds. The game shows modal error dialogs when its VConsole2 port is held.</summary>
    private static int FindFreePort(SocketType socketType, ProtocolType protocolType)
    {
        // The game binds to 0.0.0.0, so the check binds to the same address
        using var socket = new Socket(AddressFamily.InterNetwork, socketType, protocolType);
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));

        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited before the kill
        }
    }
}
