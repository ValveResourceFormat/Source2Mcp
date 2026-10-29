# source2mcp

An MCP server that drives Source 2 games (CS2, Deadlock, Dota 2 and Half-Life: Alyx) through VConsole2, the console protocol of the engine. It injects no code into the game.

## Run

You need Windows, the .NET SDK and the game installed through Steam.

```
dotnet run --project src -- --port 13339
```

The default port is 13339. Add the server to Claude Code:

```
claude mcp add --transport http source2mcp http://127.0.0.1:13339/mcp
```

## Tools

| Tool | What it does |
|---|---|
| `launch` | Starts the game, loads a map and runs the setup. With `tools`, it starts the game in tools mode. With `compare`, it prepares repeatable screenshots. |
| `map` | Loads a map and waits until the player is in the game. Takes `compare` as `launch` does. |
| `console` | Runs console commands and returns their output, the unknown commands, and the cvars that did not get the requested value. |
| `camera` | Moves the camera and reads it back with `getpos`. In Dota 2, it only reads the camera. |
| `screenshot` | Saves what the game renders to a PNG file, a few frames after the earlier commands. |
| `cvars` | Finds cvars and commands, and marks hidden ones. |
| `restore_cvars` | Sets the cvars that the server changed back to their original values. |
| `get_log` | Returns recent log lines. |
| `get_status` | Reports the process, the connection and the changed cvars. After the game exits, it gives the exit code and the last log lines. |
| `quit` | Restores the changed cvars and stops the game. |

## Safety

- The game always starts with `-insecure` and `sv_cheats 1`.
- The server loads local maps only. `console` refuses `connect`, `map`, `changelevel` and `quit`.
- The server does not change game files. The game runs with the user's config and saves archived cvars to it on quit. Thus the server records the value of each cvar before it first changes it, through `console`, `launch` or `map`, and `quit` sets it back. The reply of `quit` lists the cvars that could not be restored. Half-Life: Alyx sends no cvar list, so there `console` records no cvar.
- `launch` sets `engine_no_focus_sleep 0` through the console, so that the game renders at full rate without focus, and `quit` restores it. A `+cvar` launch argument is saved to the config like a console change, so the server does not set archived cvars on the command line.

## Repeatable screenshots

Give `compare` to `launch` or `map`. It runs these steps and returns the value of each cvar:

1. Run `noclip` until it reports on, except in Dota 2.
2. Run `cl_drawhud 0`, `r_drawviewmodel 0` and `r_drawpanorama 0`, and `r_always_render_all_windows 1` and `r_vconsole_foregroundforcerender 1` where the game shows them.
3. With `exposure`, set `mat_tonemap_force_min` and `mat_tonemap_force_max` to it. Deadlock and Dota 2 ignore these cvars.
4. With `host_framerate`, set `host_framerate`. Do not set `host_timescale 0`.

## Use with vrf

The server uses the same position, angle and FOV conventions as the `vrf` server of [Source 2 Viewer](https://github.com/ValveResourceFormat/ValveResourceFormat). To compare a frame:

1. Open `de_dust2` in Source 2 Viewer, and `launch` CS2 with `de_dust2`.
2. Give the same `position`, `angles` and `fov` to `camera` in the two servers.
3. Take a screenshot in each, then use `vrf.compare_screenshots`.
