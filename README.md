# source2mcp

An MCP server that drives Source 2 games (CS2, Deadlock and Dota 2) through VConsole2, the console protocol of the engine. It injects no code into the game.

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
| `launch` | Starts the game, loads a map and runs the setup. |
| `map` | Loads a map and waits until the player is in the game. |
| `console` | Runs console commands and returns their output. |
| `camera` | Moves the camera and reads it back with `getpos`. In Dota 2, it only reads the camera. |
| `screenshot` | Saves what the game renders to a PNG file. |
| `cvars` | Finds cvars and commands, and marks hidden ones. |
| `get_log` | Returns recent log lines. |
| `get_status` | Reports the process and the connection. |
| `quit` | Stops the game. |

## Safety

- The game always starts with `-insecure` and `sv_cheats 1`.
- The server loads local maps only. `console` refuses `connect`, `map`, `changelevel` and `quit`.
- The server does not change game files or the user's config.

## Repeatable screenshots

1. Run `noclip` one time.
2. Run `cl_drawhud 0`, `r_drawviewmodel 0` and `r_drawpanorama 0`.
3. Set `mat_tonemap_force_min` and `mat_tonemap_force_max` to the same value.
4. Set `host_framerate`. Do not set `host_timescale 0`.

## Use with vrf

The server uses the same position, angle and FOV conventions as the `vrf` server of [Source 2 Viewer](https://github.com/ValveResourceFormat/ValveResourceFormat). To compare a frame:

1. Open `de_dust2` in Source 2 Viewer, and `launch` CS2 with `de_dust2`.
2. Give the same `position`, `angles` and `fov` to `camera` in the two servers.
3. Take a screenshot in each, then use `vrf.compare_screenshots`.
