using ValveResourceFormat.IO;

namespace Source2Mcp;

/// <summary>The games that the server can start.</summary>
internal enum Game
{
    Cs2,
    Deadlock,
    Dota2,
    Hlvr,
}

/// <summary>What the server must know about one game.</summary>
/// <param name="ModDirectory">The folder of the game in the <c>game</c> folder, which has the <c>maps</c> folder.</param>
/// <param name="EyeHeight">The height of the eye above the position that <c>setpos</c> sets, for a player who stands in noclip.</param>
/// <param name="FovCommand">The console command that sets the field of view, or null when no such command is known.</param>
/// <param name="ExtraArguments">Command-line arguments that this game needs in addition to the common ones.</param>
internal sealed record GameProfile(
    Game Game,
    int AppId,
    string ExecutableName,
    string ModDirectory,
    float EyeHeight,
    string? FovCommand,
    IReadOnlyList<string> ExtraArguments)
{
    public static readonly GameProfile CounterStrike2 = new(
        Game.Cs2,
        AppId: 730,
        ExecutableName: "cs2",
        ModDirectory: "csgo",
        EyeHeight: 64,
        FovCommand: "fov_cs_debug",
        ExtraArguments: []);

    public static readonly GameProfile Deadlock = new(
        Game.Deadlock,
        AppId: 1422450,
        ExecutableName: "deadlock",
        ModDirectory: "citadel",
        // getpos reports the third-person camera, so the camera tool cannot read back the eye position
        EyeHeight: 64,
        FovCommand: null,
        ExtraArguments: []);

    public static readonly GameProfile Dota2 = new(
        Game.Dota2,
        AppId: 570,
        ExecutableName: "dota2",
        ModDirectory: "dota",
        // setpos moves no camera. getpos reports the top-down game camera, which has a pitch of 60 and a yaw of 90.
        EyeHeight: 0,
        // default_fov has no effect on the game camera, and the camera FOV cvars are hidden
        FovCommand: null,
        // The game must not take the focus from other windows
        ExtraArguments:
        [
            "+dota_activate_window_on_hero_picking_start", "0",
            "+dota_activate_window_on_match_found", "0",
            "+dota_activate_window_on_ready_check", "0",
            "+dota_activate_window_on_unpause", "0",
        ]);

    public static readonly GameProfile HalfLifeAlyx = new(
        Game.Hlvr,
        AppId: 546560,
        ExecutableName: "hlvr",
        ModDirectory: "hlvr",
        EyeHeight: 64,
        // default_fov has no effect, and fov_desired is saved to the config
        FovCommand: null,
        // The game runs in a window with keyboard and mouse movement, and does not start SteamVR
        ExtraArguments: ["-novr"]);

    public static GameProfile Get(Game game) => game switch
    {
        Game.Cs2 => CounterStrike2,
        Game.Deadlock => Deadlock,
        Game.Dota2 => Dota2,
        Game.Hlvr => HalfLifeAlyx,
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };

    /// <summary>Finds the <c>game</c> folder of the installation in the Steam libraries.</summary>
    /// <exception cref="ToolException">Steam does not have the game installed.</exception>
    public string GetGamePath()
    {
        var game = GameFolderLocator.FindSteamGameByAppId(AppId) ?? throw new ToolException($"Steam does not have {Game} (app {AppId}) installed.");

        return Path.Combine(game.GamePath, "game");
    }

    public string GetExecutablePath(string gamePath) => Path.Combine(gamePath, "bin", "win64", ExecutableName + ".exe");

    /// <exception cref="ToolException">The game has no such map.</exception>
    public void ThrowIfNoMap(string gamePath, string map)
    {
        var path = Path.Combine(gamePath, ModDirectory, "maps", map + ".vpk");

        if (!File.Exists(path))
        {
            throw new ToolException($"{Game} has no map '{map}'. The file {path} does not exist.");
        }
    }
}
