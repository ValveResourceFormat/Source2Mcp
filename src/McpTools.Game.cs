using System.Buffers;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using SkiaSharp;
using VConsole;

namespace Source2Mcp;

internal sealed partial class McpTools
{
    private const int LogLimit = 1000;

    private const string SetupDescription = "After the map is loaded, make a stable test environment. In CS2, the setup removes the bots, stops the round end, freeze time and fall damage, and puts the player alive on the T team. On the Deadlock map dl_hideout, the setup removes the intro view, so that the camera tool can move the camera. The intro view shows only on the first load of dl_hideout. On other Deadlock maps, the setup looks for the intro view for 5 seconds, then stops. In Dota 2, the setup stops the creep spawns, removes the fog of war, and starts the game with no hero. Half-Life: Alyx has no setup.";

    // FCVAR_DEVELOPMENTONLY and FCVAR_DEFENSIVE
    private const ulong HiddenFlags = 0x2 | 0x1_0000_0000;

    // getpos prints six decimals, and angles come back with float rounding near 1e-4
    private const float SettledTolerance = 0.01f;
    private const float NoteTolerance = 0.5f;

    private static readonly TimeSpan ScreenshotTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CameraSettleTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CameraPollInterval = TimeSpan.FromMilliseconds(16);
    private static readonly SearchValues<char> MapNameCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-/");

    private volatile GameSession? _session;

    private sealed record LaunchReply(Game Game, int ProcessId, int Port, string? Map, bool Setup, string CommandLine, float Seconds);

    private sealed record MapReply(string Map, bool Setup, float Seconds);

    /// <param name="Severity">Null for an ordinary message.</param>
    private sealed record OutputLine(string Channel, string Text, LogSeverity? Severity);

    private sealed record ConsoleReply(bool Completed, IReadOnlyList<OutputLine> Lines, string? Note);

    private sealed record CameraReply(Vector3 Position, Vector3 Angles, float? Fov, string? Note);

    private sealed record ScreenshotReply(string Path, int Width, int Height);

    private sealed record ConVarReply(string Name, string? Value, string? Default, string? Min, string? Max, string Flags, bool? Command, bool? Hidden, string? Help, IReadOnlyList<string> EnumValues);

    private sealed record CvarsReply(int Total, int Count, IReadOnlyList<ConVarReply> Entries);

    /// <param name="Time">Milliseconds since the game process started.</param>
    /// <param name="History">True for a line from the history replay, which is older than the connection.</param>
    private sealed record LogEntry(long Sequence, uint Time, string Channel, string Text, LogSeverity? Severity, bool? History);

    private sealed record StatusReply(
        bool Running,
        Game? Game = null,
        int? ProcessId = null,
        int? Port = null,
        bool? Connected = null,
        string? Executable = null,
        string? GameDirectory = null,
        string? CommandLine = null,
        double? PingMs = null,
        int? ConVars = null,
        int? Channels = null,
        int? InvalidPackets = null,
        string? LastInvalidPacket = null);

    private sealed record QuitReply(int ExitCode);

    public async ValueTask DisposeAsync()
    {
        // The game continues to run. Only the connection closes.
        var session = _session;
        _session = null;

        if (session != null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        _callLock.Dispose();
    }

    [Tool("Start a game and connect to its VConsole2 port. The game starts with -insecure and sv_cheats 1, in a window. With 'map', the call returns when the map is loaded and the setup is complete. Without 'map', it returns when the game runs console commands. Only one game can run at a time.")]
    private async Task<object> Launch(
        [Description("The game to start.")] Game game,
        [Description("The local map to load, for example de_dust2.")] string? map = null,
        [Description("Window width in pixels.")][Range(320, 7680)] int width = 1280,
        [Description("Window height in pixels.")][Range(240, 4320)] int height = 720,
        [Description(SetupDescription)] bool setup = true,
        CancellationToken cancellationToken = default)
    {
        var profile = GameProfile.Get(game);

        if (map != null)
        {
            ThrowIfInvalidMapName(map);
        }

        if (_session is { } current)
        {
            if (!current.Process.HasExited)
            {
                throw new ToolException($"{current.Profile.Game} already runs as process {current.Process.Id}. Use quit first.");
            }

            _session = null;
            await current.DisposeAsync().ConfigureAwait(false);
        }

        var start = Stopwatch.GetTimestamp();
        var session = await GameSession.StartAsync(profile, map, width, height, cancellationToken).ConfigureAwait(false);

        // get_log and get_status can read the session while the game loads
        _session = session;
        var runSetup = map != null && setup && session.HasSetup();

        try
        {
            await session.WaitUntilReadyAsync(map, afterSequence: 0, cancellationToken).ConfigureAwait(false);

            if (runSetup)
            {
                await session.RunSetupAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            _session = null;
            await session.AbortAsync().ConfigureAwait(false);
            throw;
        }

        var seconds = (float)Stopwatch.GetElapsedTime(start).TotalSeconds;
        return new LaunchReply(game, session.Process.Id, session.Port, map, runSetup, session.CommandLine, seconds);
    }

    [Tool("Load a local map in the running game and wait until the player is in the game. Then run the setup, as launch does. The reply does not contain the log of the map load. To read that log, use get_log.")]
    private async Task<object> Map(
        [Description("The local map to load, for example de_mirage.")] string map,
        [Description(SetupDescription)] bool setup = true,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidMapName(map);

        var session = GetSession();
        var runSetup = setup && session.HasSetup();
        var start = Stopwatch.GetTimestamp();

        await session.LoadMapAsync(map, cancellationToken).ConfigureAwait(false);

        if (runSetup)
        {
            await session.RunSetupAsync(cancellationToken).ConfigureAwait(false);
        }

        var seconds = (float)Stopwatch.GetElapsedTime(start).TotalSeconds;
        return new MapReply(map, runSetup, seconds);
    }

    [Tool("Run console commands in the game and return their output. Separate commands with ';'. The game does not link output to a command, so the reply can include lines from other sources. The server refuses commands that connect to a server, map commands (use the map tool), and quit.")]
    private async Task<object> Console(
        [Description("One or more console commands, separated by ';'.")] string commands,
        [Description("Maximum number of seconds to wait for the game to run the commands. The game runs commands only while it renders frames.")][Range(1, 600)] int timeout = 15,
        [Description("After the commands complete, continue to collect output until no line arrives for this number of seconds. Use it for commands that complete in a later frame.")][Range(0, 60)] int quietSeconds = 0,
        [Description("Maximum number of lines to return. The first lines are returned.")][Range(1, LogLimit)] int maxLines = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commands))
        {
            throw new ArgumentException("'commands' must not be empty.");
        }

        CommandPolicy.Check(commands);

        var session = GetSession();
        var result = await session.Client.ExecuteAsync(commands, TimeSpan.FromSeconds(timeout), TimeSpan.FromSeconds(quietSeconds), cancellationToken).ConfigureAwait(false);

        var count = Math.Min(result.Lines.Count, maxLines);
        var lines = new List<OutputLine>(count);

        for (var i = 0; i < count; i++)
        {
            var line = result.Lines[i];
            lines.Add(new OutputLine(line.Channel, line.Text, ToReplySeverity(line.Severity)));
        }

        string? note = null;

        if (!result.Completed)
        {
            note = $"The game did not run the commands in {timeout} s. The lines show the output so far. Read later output with get_log.";
        }

        if (result.Lines.Count > maxLines)
        {
            var lastSequence = result.Lines[count - 1].Sequence;
            var cutNote = $"The reply shows the first {count} of {result.Lines.Count} lines. To read the other lines, use get_log with 'after' {lastSequence}, or set a larger 'max_lines'.";
            note = note == null ? cutNote : note + " " + cutNote;
        }

        return new ConsoleReply(result.Completed, lines, note);
    }

    [Tool("Move the player camera at once, then read the camera back from the game. 'position' is the eye position, as in the vrf server. It needs sv_cheats 1, which launch sets. Run noclip one time first, so that gravity does not move the camera. Do not crouch, because crouch moves the eye down. The game moves the camera in a later frame, so the tool reads the camera again until it matches the request, for a maximum of 1 second. With no arguments, it only reads the camera. In Deadlock, the game reports the third-person camera, not the eye. That camera is behind, right of and above the hero, and it turns with the yaw. Thus in Deadlock the reply does not match 'position'. On the Deadlock map dl_hideout, the game shows an intro view until the setup removes it. While the intro view shows, the game reports the intro camera, and the camera tool cannot move the camera. In Dota 2, the tool only reads the top-down game camera.")]
    private async Task<object> Camera(
        [Description("World position of the eye.")] Vector3? position = null,
        [Description("[pitch, yaw, roll] degrees to face, pitch positive downwards.")] Vector3? angles = null,
        [Description("Horizontal field of view in degrees at a 4:3 aspect ratio. A wider window shows more at the sides. The vrf camera uses the same value, so give the same value to the two servers. The game default is 90. CS2 only.")][Range(1d, 170d)] float? fov = null,
        CancellationToken cancellationToken = default)
    {
        var session = GetSession();
        var profile = session.Profile;

        if (profile.Game == Game.Dota2 && (position != null || angles != null))
        {
            throw new ArgumentException("In Dota 2, setpos and setang do not move the camera. Use console with dota_camera_set_lookatpos <x> <y> and dota_camera_distance <units>. The pitch is always 60 and the yaw is always 90.");
        }

        if (fov != null && profile.FovCommand == null)
        {
            throw new ArgumentException($"No console command for the field of view of {profile.Game} is known. Leave out 'fov'.");
        }

        if (position is { } eye)
        {
            // setpos sets the feet position. getpos reports the eye position in CS2 and the third-person camera in Deadlock.
            await session.RunAsync($"setpos {Format(eye.X)} {Format(eye.Y)} {Format(eye.Z - profile.EyeHeight)}", cancellationToken).ConfigureAwait(false);
        }

        if (angles is { } face)
        {
            await session.RunAsync($"setang {Format(face.X)} {Format(face.Y)} {Format(face.Z)}", cancellationToken).ConfigureAwait(false);
        }

        if (fov is { } fieldOfView)
        {
            await session.RunAsync($"{profile.FovCommand} {Format(fieldOfView)}", cancellationToken).ConfigureAwait(false);
        }

        // The Deadlock readback is the third-person camera, which never matches the requested eye position
        var expectedPosition = profile.Game == Game.Deadlock ? null : position;

        // setpos and setang run on the server, so the client getpos shows them only after the next tick.
        // After noclip or a crouch, the client also moves the eye to its new height over about 0.3 s.
        var settleStart = Stopwatch.GetTimestamp();
        var (readPosition, readAngles) = await session.ReadCameraAsync(cancellationToken).ConfigureAwait(false);

        while (!CameraMatches(expectedPosition, angles, readPosition, readAngles, SettledTolerance) && Stopwatch.GetElapsedTime(settleStart) < CameraSettleTimeout)
        {
            // Wait about one tick between readings, so that the log does not fill with getpos output
            await Task.Delay(CameraPollInterval, cancellationToken).ConfigureAwait(false);
            (readPosition, readAngles) = await session.ReadCameraAsync(cancellationToken).ConfigureAwait(false);
        }

        float? readFov = null;

        if (profile.FovCommand != null && session.Client.ConVars.TryGetValue(profile.FovCommand, out var fovConVar)
            && float.TryParse(fovConVar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            // fov_cs_debug 0 keeps the default field of view, which is 90
            readFov = value > 0 ? value : 90;
        }

        string? note = null;

        if (!CameraMatches(expectedPosition, null, readPosition, readAngles, NoteTolerance))
        {
            note = "The game reports a different position. A wall or the floor can block the move, or the player crouches. Run noclip one time, then try again.";
        }
        else if (!CameraMatches(null, angles, readPosition, readAngles, NoteTolerance))
        {
            note = "The game reports different angles. The game limits the pitch, and roll can stay 0.";
        }

        return new CameraReply(readPosition, readAngles, readFov, note);
    }

    [Tool("Take a screenshot of what the game renders, with the user interface, and save it to a PNG file. The game sends a JPEG image with quality 90 and the size of the window, and the server stores it as PNG without more loss. The game renders only while its window is visible. When another window covers the game window, the image shows an old frame.")]
    private async Task<object> Screenshot(
        [Description("Absolute path of the .png file to write. The default is a new file in the source2mcp folder in the temporary folder.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        if (path != null && (!Path.IsPathFullyQualified(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"'path' must be an absolute path of a .png file, got '{path}'.");
        }

        var session = GetSession();
        byte[] jpeg;

        try
        {
            jpeg = await session.Client.RequestScreenshotAsync(ScreenshotTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new ToolException($"The game sent no screenshot in {ScreenshotTimeout.TotalSeconds} s. The game must render frames.");
        }

        if (path == null)
        {
            var name = "source2mcp_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".png";
            path = Path.Combine(Path.GetTempPath(), "source2mcp", name);
        }

        using var bitmap = SKBitmap.Decode(jpeg) ?? throw new ToolException($"The game sent {jpeg.Length} bytes that are not a valid image.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (var stream = File.Create(path))
        {
            bitmap.Encode(stream, SKEncodedImageFormat.Png, quality: 100);
        }

        return new ScreenshotReply(path, bitmap.Width, bitmap.Height);
    }

    [Tool("Find cvars and commands in the list that the game sends over VConsole2. Returns the value, default, range, flags and help text. A hidden entry is development-only (flag 0x2) or defensive (flag 0x100000000). The game can ignore a hidden command without an error message. Test a hidden command with console before you use it. CS2 does not send hidden entries. Deadlock and Dota 2 send them, and the reply marks them with 'hidden'. Half-Life: Alyx sends no list.", Concurrent = true)]
    private CvarsReply Cvars(
        [Description("Text to find in the name. Case does not matter. An empty text matches all entries.")] string query = "",
        [Description("Also find the text in the help text.")] bool searchHelp = false,
        [Description("Return only entries that have all these FCVAR flag bits.")][Range(0L, long.MaxValue)] long flags = 0,
        [Description("Maximum number of entries to return.")][Range(1, 1000)] int limit = 50)
    {
        var session = GetSession();
        var matches = new List<ConVar>();
        var conVars = session.Client.ConVars;

        foreach (var conVar in conVars.Values)
        {
            if (((long)conVar.Flags & flags) != flags)
            {
                continue;
            }

            var isMatch = conVar.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (searchHelp && conVar.HelpText.Contains(query, StringComparison.OrdinalIgnoreCase));

            if (isMatch)
            {
                matches.Add(conVar);
            }
        }

        // An exact name comes first, then the names in alphabetical sequence
        matches.Sort((a, b) =>
        {
            var aExact = string.Equals(a.Name, query, StringComparison.OrdinalIgnoreCase);
            var bExact = string.Equals(b.Name, query, StringComparison.OrdinalIgnoreCase);

            if (aExact != bExact)
            {
                return aExact ? -1 : 1;
            }

            return string.CompareOrdinal(a.Name, b.Name);
        });

        var entries = new List<ConVarReply>();

        for (var i = 0; i < matches.Count && i < limit; i++)
        {
            entries.Add(ToReply(matches[i]));
        }

        return new CvarsReply(matches.Count, entries.Count, entries);
    }

    [Tool("Return recent log lines of the game, oldest first. The server keeps the last 20000 lines of this connection. On connect, the game also sends the history of older lines.", Concurrent = true)]
    private List<LogEntry> GetLog(
        [Description("Return only lines of this channel, for example Console. Case does not matter.")] string? channel = null,
        [Description("Return only lines that contain this text. Case does not matter.")] string? contains = null,
        [Description("Return only lines with this severity or higher.")] LogSeverity minSeverity = LogSeverity.MoreDetailed,
        [Description("Include the history lines that the game sent on connect.")] bool includeHistory = false,
        [Description("Return only lines after this sequence number, from an earlier reply.")][Range(0L, long.MaxValue)] long after = 0,
        [Description("Maximum number of lines to return. The newest lines are returned.")][Range(1, LogLimit)] int last = 100)
    {
        var session = GetSession();

        bool Matches(LogLine line)
        {
            if (line.Sequence <= after || line.Severity < minSeverity || (line.IsHistory && !includeHistory))
            {
                return false;
            }

            if (channel != null && !string.Equals(line.Channel, channel, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return contains == null || line.Text.Contains(contains, StringComparison.OrdinalIgnoreCase);
        }

        var lines = session.Client.Log.GetLast(last, Matches);
        var entries = new List<LogEntry>(lines.Count);

        foreach (var line in lines)
        {
            bool? history = line.IsHistory ? true : null;
            entries.Add(new LogEntry(line.Sequence, line.Time, line.Channel, line.Text, ToReplySeverity(line.Severity), history));
        }

        return entries;
    }

    [Tool("Report the game process and the VConsole2 connection.", Concurrent = true)]
    private async Task<object> GetStatus(CancellationToken cancellationToken = default)
    {
        var session = _session;

        if (session == null)
        {
            return new StatusReply(Running: false);
        }

        var client = session.Client;
        var info = client.ApplicationInfo;
        var running = !session.Process.HasExited;
        double? pingMs = null;

        if (running && client.IsConnected)
        {
            try
            {
                pingMs = (await client.PingAsync(PingTimeout, cancellationToken).ConfigureAwait(false)).TotalMilliseconds;
            }
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                // The reply shows no ping
            }
        }

        return new StatusReply(
            Running: running,
            Game: session.Profile.Game,
            ProcessId: session.Process.Id,
            Port: session.Port,
            Connected: client.IsConnected,
            Executable: info?.ExecutableName,
            GameDirectory: info?.GameDirectory,
            CommandLine: session.CommandLine,
            PingMs: pingMs,
            ConVars: client.ConVars.Count,
            Channels: client.Channels.Count,
            InvalidPackets: client.InvalidPacketCount,
            LastInvalidPacket: client.LastInvalidPacket);
    }

    [Tool("Stop the game with the quit command. The server stops the process if it does not exit in 30 seconds. Returns the exit code.")]
    private async Task<object> Quit(CancellationToken cancellationToken = default)
    {
        var session = _session ?? throw new ToolException("No game runs.");
        var exitCode = await session.QuitAsync(cancellationToken).ConfigureAwait(false);

        _session = null;
        await session.DisposeAsync().ConfigureAwait(false);

        return new QuitReply(exitCode);
    }

    private GameSession GetSession()
    {
        var session = _session ?? throw new ToolException("No game runs. Use launch first.");
        session.ThrowIfStopped();

        return session;
    }

    /// <summary>True when the camera that the game reports is at the requested position and angles. A null request always matches.</summary>
    private static bool CameraMatches(Vector3? position, Vector3? angles, Vector3 readPosition, Vector3 readAngles, float tolerance)
    {
        if (position is { } requestedPosition && Vector3.Distance(requestedPosition, readPosition) > tolerance)
        {
            return false;
        }

        if (angles is not { } requestedAngles)
        {
            return true;
        }

        return AngleDifference(requestedAngles.X, readAngles.X) <= tolerance
            && AngleDifference(requestedAngles.Y, readAngles.Y) <= tolerance
            && AngleDifference(requestedAngles.Z, readAngles.Z) <= tolerance;
    }

    /// <summary>The smallest difference between two angles in degrees, from 0 to 180.</summary>
    private static float AngleDifference(float a, float b)
    {
        var difference = Math.Abs(a - b) % 360f;

        return difference > 180f ? 360f - difference : difference;
    }

    private static string Format(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static LogSeverity? ToReplySeverity(LogSeverity severity) => severity == LogSeverity.Message ? null : severity;

    private static void ThrowIfInvalidMapName(string map)
    {
        if (map.Length == 0 || map.AsSpan().ContainsAnyExcept(MapNameCharacters))
        {
            throw new ArgumentException($"'map' must be a map name with letters, digits, '_', '-' and '/' only, got '{map}'.");
        }
    }

    private static ConVarReply ToReply(ConVar conVar)
    {
        var flags = "0x" + conVar.Flags.ToString("X", CultureInfo.InvariantCulture);
        bool? hidden = (conVar.Flags & HiddenFlags) != 0 ? true : null;

        if (conVar.IsCommand)
        {
            return new ConVarReply(conVar.Name, null, null, null, null, flags, true, hidden, conVar.HelpText, conVar.EnumValues);
        }

        return new ConVarReply(conVar.Name, conVar.Value, conVar.DefaultValue, conVar.MinValue, conVar.MaxValue, flags, null, hidden, conVar.HelpText, conVar.EnumValues);
    }
}
