using System.Globalization;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;

namespace Source2Mcp;

/// <summary>The games that the server can start.</summary>
internal enum Game
{
    Cs2,
    Deadlock,
    Dota2,
    Hlvr,
    Steamvr,
    DeskJob,
    RobotRepair,
}

/// <summary>How a game loads a map from an addon. The map command cannot load a map from an addon that is not mounted.</summary>
/// <param name="CommandFormat">The console commands that mount the addon and load the map. {0} is the addon, {1} is the map.</param>
/// <param name="Local">The addon can be a folder in <c>{ModDirectory}_addons</c>.</param>
/// <param name="Workshop">The addon can be the id of a subscribed workshop item.</param>
/// <param name="EnabledListConVar">The cvar with the list of enabled addons that the commands change, or null.</param>
internal sealed record AddonSupport(string CommandFormat, bool Local, bool Workshop, string? EnabledListConVar);

/// <summary>What the server must know about one game.</summary>
/// <param name="GameFolder">The <c>game</c> folder, relative to the installation folder.</param>
/// <param name="ModDirectory">The folder of the game in the <c>game</c> folder, which has the <c>maps</c> folder.</param>
/// <param name="Addons">How the game loads a map from an addon, or null when the server cannot load addon maps.</param>
/// <param name="CheatsNeedTools">The game allows sv_cheats only in tools mode, so it always starts with -tools.</param>
/// <param name="EyeHeight">The height of the eye above the position that <c>setpos</c> sets, for a player who stands in noclip.</param>
/// <param name="FovCommand">The console command that sets the field of view, or null when no such command is known.</param>
/// <param name="HasNoclip">The player can fly with noclip, so that gravity does not move the camera.</param>
/// <param name="HasTonemapForce">The game uses mat_tonemap_force_min and mat_tonemap_force_max for the exposure.</param>
/// <param name="ExtraArguments">Command-line arguments that this game needs in addition to the common ones.</param>
internal sealed record GameProfile(
    Game Game,
    int AppId,
    string GameFolder,
    string ExecutableName,
    string ModDirectory,
    AddonSupport? Addons,
    bool CheatsNeedTools,
    float EyeHeight,
    string? FovCommand,
    bool HasNoclip,
    bool HasTonemapForce,
    IReadOnlyList<string> ExtraArguments)
{
    public static readonly GameProfile CounterStrike2 = new(
        Game.Cs2,
        AppId: 730,
        GameFolder: "game",
        ExecutableName: "cs2",
        ModDirectory: "csgo",
        // A local addon loads only in tools mode with -addon on the command line
        Addons: new AddonSupport("map_workshop {0} {1}", Local: false, Workshop: true, EnabledListConVar: null),
        CheatsNeedTools: false,
        EyeHeight: 64,
        FovCommand: "fov_cs_debug",
        HasNoclip: true,
        HasTonemapForce: true,
        ExtraArguments: []);

    public static readonly GameProfile Deadlock = new(
        Game.Deadlock,
        AppId: 1422450,
        GameFolder: "game",
        ExecutableName: "deadlock",
        ModDirectory: "citadel",
        Addons: null,
        CheatsNeedTools: false,
        // getpos reports the third-person camera, so the camera tool cannot read back the eye position
        EyeHeight: 64,
        FovCommand: null,
        HasNoclip: true,
        // The cvars exist, but the exposure does not change
        HasTonemapForce: false,
        ExtraArguments: []);

    public static readonly GameProfile Dota2 = new(
        Game.Dota2,
        AppId: 570,
        GameFolder: "game",
        ExecutableName: "dota2",
        ModDirectory: "dota",
        // The command does not find a map in a workshop item
        Addons: new AddonSupport("dota_launch_custom_game {0} {1}", Local: true, Workshop: false, EnabledListConVar: null),
        CheatsNeedTools: false,
        // setpos moves no camera. getpos reports the top-down game camera, which has a pitch of 60 and a yaw of 90.
        EyeHeight: 0,
        // default_fov has no effect on the game camera, and the camera FOV cvars are hidden
        FovCommand: null,
        // The game runs with no hero
        HasNoclip: false,
        // The cvars exist, but the exposure does not change
        HasTonemapForce: false,
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
        GameFolder: "game",
        ExecutableName: "hlvr",
        ModDirectory: "hlvr",
        // addon_play mounts all enabled addons, and addon_enable adds the addon to the saved list
        Addons: new AddonSupport("addon_enable {0}; addon_play {1}", Local: true, Workshop: true, EnabledListConVar: "default_enabled_addons_list"),
        CheatsNeedTools: false,
        EyeHeight: 64,
        // default_fov has no effect, and fov_desired is saved to the config
        FovCommand: null,
        HasNoclip: true,
        HasTonemapForce: true,
        // The game runs in a window with keyboard and mouse movement, and does not start SteamVR
        ExtraArguments: ["-novr"]);

    public static readonly GameProfile SteamVrHome = new(
        Game.Steamvr,
        AppId: 250820,
        GameFolder: Path.Combine("tools", "steamvr_environments", "game"),
        ExecutableName: "steamtours",
        ModDirectory: "steamtours",
        // All maps are in addons
        Addons: new AddonSupport("destinations_load_addon {0} {1}", Local: true, Workshop: true, EnabledListConVar: null),
        CheatsNeedTools: true,
        EyeHeight: 64,
        FovCommand: null,
        // noclip works, but prints no state
        HasNoclip: true,
        // The game has no tonemapping
        HasTonemapForce: false,
        // The game runs in a window with keyboard and mouse movement, and does not start SteamVR
        ExtraArguments: ["-novr"]);

    public static readonly GameProfile ApertureDeskJob = new(
        Game.DeskJob,
        AppId: 1902490,
        GameFolder: "game",
        ExecutableName: "deskjob",
        ModDirectory: "steampal",
        Addons: null,
        CheatsNeedTools: false,
        // The player is a camera at the desk, and getpos reports the camera position
        EyeHeight: 0,
        FovCommand: null,
        HasNoclip: false,
        HasTonemapForce: false,
        ExtraArguments: []);

    public static readonly GameProfile ApertureRobotRepair = new(
        Game.RobotRepair,
        // The game is part of The Lab
        AppId: 450390,
        GameFolder: "RobotRepair",
        ExecutableName: "vr",
        ModDirectory: "vr",
        Addons: null,
        CheatsNeedTools: false,
        EyeHeight: 64,
        FovCommand: null,
        HasNoclip: true,
        HasTonemapForce: true,
        // The game runs in a window with keyboard and mouse movement, and does not start SteamVR
        ExtraArguments: ["-novr"]);

    public static GameProfile Get(Game game) => game switch
    {
        Game.Cs2 => CounterStrike2,
        Game.Deadlock => Deadlock,
        Game.Dota2 => Dota2,
        Game.Hlvr => HalfLifeAlyx,
        Game.Steamvr => SteamVrHome,
        Game.DeskJob => ApertureDeskJob,
        Game.RobotRepair => ApertureRobotRepair,
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };

    /// <exception cref="ToolException">Steam does not have the game installed.</exception>
    private GameFolderLocator.SteamLibraryGameInfo FindInstallation() =>
        GameFolderLocator.FindSteamGameByAppId(AppId) ?? throw new ToolException($"Steam does not have {Game} (app {AppId}) installed.");

    /// <summary>Finds the <c>game</c> folder of the installation in the Steam libraries.</summary>
    /// <exception cref="ToolException">Steam does not have the game installed.</exception>
    public string GetGamePath() => Path.Combine(FindInstallation().GamePath, GameFolder);

    public string GetExecutablePath(string gamePath) => Path.Combine(gamePath, "bin", "win64", ExecutableName + ".exe");

    /// <exception cref="ToolException">The game has no such map.</exception>
    public void ThrowIfNoMap(string gamePath, string map)
    {
        var path = Path.Combine(gamePath, ModDirectory, "maps", map + ".vpk");

        if (!File.Exists(path) && !PackageHasMap(Path.Combine(gamePath, ModDirectory, "pak01_dir.vpk"), map))
        {
            throw new ToolException($"{Game} has no map '{map}'. Neither the file {path} nor an entry in the pak01 package of the game exists.");
        }
    }

    /// <summary>Returns the console commands that load the map from the addon.</summary>
    /// <exception cref="ToolException">The server cannot load addon maps in this game, or the addon has no such map.</exception>
    public string GetAddonMapCommand(string gamePath, string addon, string map)
    {
        if (Addons == null)
        {
            throw new ToolException($"The server cannot load addon maps in {Game}.");
        }

        var command = string.Format(CultureInfo.InvariantCulture, Addons.CommandFormat, addon, map);
        var checkedPaths = new List<string>();

        if (Addons.Local)
        {
            var localPath = Path.Combine(gamePath, ModDirectory + "_addons", addon, "maps", map + ".vpk");

            if (File.Exists(localPath))
            {
                return command;
            }

            checkedPaths.Add(localPath);
        }

        if (Addons.Workshop)
        {
            var itemPath = Path.Combine(FindInstallation().SteamPath, "workshop", "content", AppId.ToString(CultureInfo.InvariantCulture), addon);

            // A workshop item has one package, which is split into numbered archives when it is large
            var packagePath = Path.Combine(itemPath, addon + ".vpk");
            var splitPackagePath = Path.Combine(itemPath, addon + "_dir.vpk");

            if (PackageHasMap(packagePath, map) || PackageHasMap(splitPackagePath, map))
            {
                return command;
            }

            checkedPaths.Add(packagePath);
            checkedPaths.Add(splitPackagePath);
        }

        throw new ToolException($"{Game} has no map '{map}' in the addon '{addon}'. These files do not exist or do not have the map: {string.Join(", ", checkedPaths)}.");
    }

    /// <summary>True when the package has the map, as in a game or a workshop item that ships its maps in a package.</summary>
    private static bool PackageHasMap(string packagePath, string map)
    {
        if (!File.Exists(packagePath))
        {
            return false;
        }

        using var package = new Package();
        package.Read(packagePath);

        return package.FindEntry($"maps/{map}.vpk") != null;
    }
}
