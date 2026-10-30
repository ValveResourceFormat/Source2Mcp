using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using VConsole;

namespace Source2Mcp;

// Replies serialize these types. The serializer omits an empty List or Dictionary, but not an empty IReadOnlyList.

/// <summary>The result of <see cref="GameSession.RestoreConVarsAsync"/>.</summary>
internal sealed record ConVarRestore(List<string> Restored, List<LeftChangedConVar> LeftChanged);

/// <summary>A cvar that this server changed and did not restore.</summary>
/// <param name="Value">The value that the game reports, or null when it was not read.</param>
internal sealed record LeftChangedConVar(string Name, string Original, string? Value);

/// <summary>The result of <see cref="GameSession.SetUpCompareAsync"/>.</summary>
/// <param name="Values">The value of each cvar after the setup, or null for a cvar that the game does not have or hides.</param>
/// <param name="Noclip">True when noclip is on, or null when noclip was not run or the game did not report its state.</param>
internal sealed record CompareSetup(Dictionary<string, string?> Values, bool? Noclip);

/// <summary>One game process that this server started, and its VConsole2 connection.</summary>
internal sealed class GameSession : IAsyncDisposable
{
    // The client writes this line when its connection to its local server is complete and the player is in the game.
    // The client connects to the local server at 127.0.0.1, not to loopback.
    private const string ReadyLine = "CL:  Signon traffic \"";

    private const int StopLogLines = 10;

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

    // Cvars that keep the game rendering at full rate while its window has no focus. engine_no_focus_sleep is archived,
    // so it is set through the console and restored, not on the command line, which the game also saves to the config.
    private static readonly ConVarSet[] BackgroundRenderConVars =
    [
        new("engine_no_focus_sleep", "0"),
    ];

    // The two render cvars are hidden in CS2 without -tools, so the game can lack them
    private static readonly ConVarSet[] CompareConVars =
    [
        new("cl_drawhud", "0"),
        new("r_drawviewmodel", "0"),
        new("r_drawpanorama", "0"),
        new("r_always_render_all_windows", "1"),
        new("r_vconsole_foregroundforcerender", "1"),
    ];

    private bool _hideoutIntroRemoved;

    // The value of each cvar before this server first changed it
    private readonly Dictionary<string, string> _originalValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _originalValuesLock = new();

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

        if (tools || profile.CheatsNeedTools)
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

    /// <summary>Throws when the game process exited or the connection closed. The message has the last log lines.</summary>
    public void ThrowIfStopped()
    {
        var reason = GetStopReason();

        if (reason == null)
        {
            return;
        }

        var message = new StringBuilder(reason);
        message.Append(" Use launch to start it again.");

        var lastLines = GetLastLines();

        if (lastLines.Count > 0)
        {
            message.Append(" The last log lines:");

            foreach (var line in lastLines)
            {
                message.Append('\n').Append(line);
            }
        }

        throw new ToolException(message.ToString());
    }

    /// <summary>Why the session cannot run commands, or null while the game runs and the connection is open.</summary>
    public string? GetStopReason()
    {
        if (Process.HasExited)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The game exited with code {Process.ExitCode}.");
        }

        if (!Client.IsConnected)
        {
            return $"The VConsole2 connection to the game closed: {Client.CloseReason}";
        }

        return null;
    }

    /// <summary>The text of the newest log lines of this connection, oldest first.</summary>
    public List<string> GetLastLines()
    {
        var lines = Client.Log.GetLast(StopLogLines, static line => !line.IsHistory);
        var texts = new List<string>(lines.Count);

        foreach (var line in lines)
        {
            texts.Add(line.Text);
        }

        return texts;
    }

    /// <summary>The cvars that this server changed and did not restore, with their original values.</summary>
    public List<LeftChangedConVar> GetChangedConVars()
    {
        var changed = new List<LeftChangedConVar>();

        lock (_originalValuesLock)
        {
            foreach (var (name, original) in _originalValues)
            {
                changed.Add(new LeftChangedConVar(name, original, null));
            }
        }

        return changed;
    }

    /// <summary>
    /// Reads cvar values with console queries. A cvar that prints no value, for example a hidden or unknown one, is not in the result.
    /// </summary>
    public async Task<Dictionary<string, string>> QueryConVarsAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (names.Count == 0)
        {
            return values;
        }

        var lines = await RunAsync(string.Join("; ", names), cancellationToken).ConfigureAwait(false);

        foreach (var name in names)
        {
            if (ConVarText.FindValue(lines, name) is { } value)
            {
                values[name] = value;
            }
        }

        return values;
    }

    /// <summary>
    /// Stores the current value of each cvar that this server did not change before, so that <see cref="RestoreConVarsAsync"/> can
    /// set it again. A name that is not in the cvar list is stored only when the game prints a value for it. A name that is
    /// neither in the list nor prints a value is no cvar of the game, or a hidden one, which the game does not change.
    /// </summary>
    public async Task RecordOriginalValuesAsync(IEnumerable<string> names, CancellationToken cancellationToken)
    {
        var newNames = new List<string>();

        lock (_originalValuesLock)
        {
            foreach (var name in names)
            {
                if (!_originalValues.ContainsKey(name) && !newNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    newNames.Add(name);
                }
            }
        }

        if (newNames.Count == 0)
        {
            return;
        }

        // The query also reads cvars without change notifications, so it comes before the value in the list
        var values = await QueryConVarsAsync(newNames, cancellationToken).ConfigureAwait(false);

        lock (_originalValuesLock)
        {
            foreach (var name in newNames)
            {
                if (values.TryGetValue(name, out var value))
                {
                    _originalValues[name] = value;
                }
                else if (Client.ConVars.TryGetValue(name, out var conVar) && conVar.Value != null)
                {
                    _originalValues[name] = conVar.Value;
                }
            }
        }
    }

    /// <summary>
    /// Reads the cvars after a change. A cvar that has its original value again is no longer reported as changed.
    /// </summary>
    public async Task<Dictionary<string, string>> ReadBackAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        var values = await QueryConVarsAsync(names, cancellationToken).ConfigureAwait(false);

        lock (_originalValuesLock)
        {
            foreach (var (name, value) in values)
            {
                if (_originalValues.TryGetValue(name, out var original) && ConVarText.ValuesEqual(original, value))
                {
                    _originalValues.Remove(name);
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Records the original values, sets the cvars that the game has, and reads them back. A cvar that the game does not have
    /// is skipped, and its value in the result is null.
    /// </summary>
    public async Task<Dictionary<string, string?>> SetConVarsAsync(IReadOnlyList<ConVarSet> sets, CancellationToken cancellationToken)
    {
        var names = new List<string>(sets.Count);

        foreach (var set in sets)
        {
            names.Add(set.Name);
        }

        await RecordOriginalValuesAsync(names, cancellationToken).ConfigureAwait(false);

        var commands = new List<string>();
        var known = new List<string>();

        lock (_originalValuesLock)
        {
            foreach (var set in sets)
            {
                // Only a cvar that the game has gets a recorded value
                if (_originalValues.ContainsKey(set.Name))
                {
                    commands.Add($"{set.Name} {set.Value}");
                    known.Add(set.Name);
                }
            }
        }

        var effective = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            effective[name] = null;
        }

        if (commands.Count == 0)
        {
            return effective;
        }

        await RunAsync(string.Join("; ", commands), cancellationToken).ConfigureAwait(false);

        var values = await ReadBackAsync(known, cancellationToken).ConfigureAwait(false);

        foreach (var name in known)
        {
            effective[name] = GetReadValue(values, name);
        }

        return effective;
    }

    /// <summary>Sets the cvars that keep the game rendering while its window has no focus. <see cref="RestoreConVarsAsync"/> undoes it.</summary>
    public Task<Dictionary<string, string?>> SetBackgroundRenderAsync(CancellationToken cancellationToken) => SetConVarsAsync(BackgroundRenderConVars, cancellationToken);

    /// <summary>
    /// Hides the HUD, the view model and Panorama, keeps the game rendering in the background, and sets the exposure and the time step
    /// when they are given. With <paramref name="hasPlayer"/>, it also turns noclip on. <see cref="RestoreConVarsAsync"/> undoes the cvars.
    /// </summary>
    public async Task<CompareSetup> SetUpCompareAsync(bool hasPlayer, float? exposure, float? hostFramerate, CancellationToken cancellationToken)
    {
        List<ConVarSet> sets = [.. CompareConVars];

        if (exposure is { } exposureValue && Profile.HasTonemapForce)
        {
            var text = McpTools.Format(exposureValue);
            sets.Add(new ConVarSet("mat_tonemap_force_min", text));
            sets.Add(new ConVarSet("mat_tonemap_force_max", text));
        }

        if (hostFramerate is { } rate)
        {
            sets.Add(new ConVarSet("host_framerate", McpTools.Format(rate)));
        }

        var values = await SetConVarsAsync(sets, cancellationToken).ConfigureAwait(false);
        bool? noclip = null;

        if (hasPlayer && Profile.HasNoclip)
        {
            noclip = await TurnOnNoclipAsync(cancellationToken).ConfigureAwait(false);
        }

        return new CompareSetup(values, noclip);
    }

    /// <summary>Returns true when noclip is on, or null when the game does not report the state.</summary>
    private async Task<bool?> TurnOnNoclipAsync(CancellationToken cancellationToken)
    {
        // noclip toggles, so a second run turns it on again when the first run turned it off
        for (var i = 0; i < 2; i++)
        {
            var lines = await RunAsync("noclip", cancellationToken).ConfigureAwait(false);

            if (ContainsText(lines, "noclip ON"))
            {
                return true;
            }

            if (!ContainsText(lines, "noclip OFF"))
            {
                return null;
            }
        }

        return false;
    }

    /// <summary>
    /// Sets each changed cvar to its original value and reads it back. A cvar that does not get its original value again
    /// stays in the list of changed cvars.
    /// </summary>
    public async Task<ConVarRestore> RestoreConVarsAsync(CancellationToken cancellationToken)
    {
        var commands = new List<string>();
        var names = new List<string>();
        var leftChanged = new List<LeftChangedConVar>();
        Dictionary<string, string> originals;

        lock (_originalValuesLock)
        {
            originals = new Dictionary<string, string>(_originalValues, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var (name, original) in originals)
        {
            // The console has no escape for a quote inside a quoted value
            if (original.Contains('"', StringComparison.Ordinal))
            {
                leftChanged.Add(new LeftChangedConVar(name, original, null));
                continue;
            }

            commands.Add($"{name} \"{original}\"");
            names.Add(name);
        }

        var restored = new List<string>();

        if (commands.Count == 0)
        {
            return new ConVarRestore(restored, leftChanged);
        }

        await RunAsync(string.Join("; ", commands), cancellationToken).ConfigureAwait(false);

        var values = await ReadBackAsync(names, cancellationToken).ConfigureAwait(false);

        foreach (var name in names)
        {
            var original = originals[name];
            var value = GetReadValue(values, name);

            if (value != null && ConVarText.ValuesEqual(original, value))
            {
                restored.Add(name);

                lock (_originalValuesLock)
                {
                    _originalValues.Remove(name);
                }
            }
            else
            {
                leftChanged.Add(new LeftChangedConVar(name, original, value));
            }
        }

        return new ConVarRestore(restored, leftChanged);
    }

    /// <summary>The value from a query, or from the list for a cvar that prints no value, for example a hidden one.</summary>
    private string? GetReadValue(Dictionary<string, string> values, string name)
    {
        if (values.TryGetValue(name, out var value))
        {
            return value;
        }

        if (Client.ConVars.TryGetValue(name, out var conVar) && !conVar.IsCommand)
        {
            return conVar.Value;
        }

        return null;
    }

    /// <summary>Loads a map, from an addon when <paramref name="addon"/> is given, and waits until the player is in the game.</summary>
    public async Task LoadMapAsync(string map, string? addon, CancellationToken cancellationToken)
    {
        string command;

        if (addon == null)
        {
            Profile.ThrowIfNoMap(GamePath, map);
            command = "map " + map;
        }
        else
        {
            command = Profile.GetAddonMapCommand(GamePath, addon, map);

            var listConVar = Profile.Addons?.EnabledListConVar;

            // quit and restore_cvars set the list of enabled addons back
            if (listConVar != null)
            {
                await RecordOriginalValuesAsync([listConVar], cancellationToken).ConfigureAwait(false);
            }
        }

        // The ready line of the previous map is already in the log
        var afterSequence = Client.Log.LastSequence;

        await Client.SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
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
            case Game.Steamvr:
            case Game.DeskJob:
            case Game.RobotRepair:
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
        var sent = false;

        if (!Process.HasExited && Client.IsConnected)
        {
            try
            {
                await Client.SendCommandAsync("quit", cancellationToken).ConfigureAwait(false);
                sent = true;
            }
            catch (IOException)
            {
                // The connection closed. The process is stopped below.
            }
        }

        if (!sent && !Process.HasExited)
        {
            Log.Info($"Process {Process.Id} has no VConsole2 connection for quit. Stopping it.");
            Stop(Process);
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
    /// cannot connect to its local server, or a game cannot mount the map of an addon, the game loads an empty or a start map,
    /// and that map also writes the ready line.
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
            throw new ToolException($"The game loaded '{loadedMap}', not '{map}'. Read get_log for errors, for example a map that the game could not mount or a client that did not connect to its local server.");
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
