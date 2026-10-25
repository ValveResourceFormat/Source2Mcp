using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Source2Mcp;

/// <summary>
/// One loopback HTTP endpoint that speaks the MCP revision in <see cref="ProtocolVersion"/> over JSON-RPC 2.0.
/// That revision has no handshake. Every request carries its protocol version, so the server keeps nothing between calls.
/// </summary>
internal sealed class McpServer : IDisposable
{
    private const string ProtocolVersion = "2026-07-28";
    private const string Meta = "io.modelcontextprotocol/";

    private const string Instructions = """
        Drives Source 2 games (CS2, Deadlock and Dota 2) through VConsole2, the console protocol of the engine. Start the game with launch. Use console for all other work: console runs commands as the game console does.
        The server starts the game with -insecure. The game cannot connect to online servers. Load local maps only.
        A successful call replies with compact JSON that omits null and empty fields. A failed call replies with plain text and isError.
        Positions are [x, y, z] in world units. Angles are [pitch, yaw, roll] in degrees, with pitch positive downwards. The vrf server uses the same conventions, so you can copy a camera from one server to the other.
        The game does not link output to a command. Thus console collects all log lines until an echo marker, and lines from other sources can also occur. A command that completes in a later frame writes more output after the marker. Use quiet_seconds to collect that output, or read it later with get_log.
        To change the map, use the map tool. It waits until the map is loaded and does not return the log of the map load.
        The game can ignore a development-only or defensive command without an error message. Use cvars to make sure that a command exists. Test a hidden command with console before you use it.
        To find entities by a part of the class or name, use ent_find for server entities and cl_ent_find for client entities. To read the origin and angles of one entity, use cl_dumpentity with its index.
        When launch or map loads a map, it also runs a setup. In CS2, after the setup, no bots are in the game, the round does not end, and the player is alive on the T team. On the Deadlock map dl_hideout, the setup removes the intro view. In Dota 2, after the setup, no creeps spawn, the fog of war is off, and the game runs with no hero.
        In Dota 2, the camera tool cannot move the camera. Use dota_camera_set_lookatpos and dota_camera_distance through console.
        For repeatable screenshots, do these steps first:
        1. Run noclip one time, so that gravity does not move the camera.
        2. Run cl_drawhud 0, r_drawviewmodel 0 and r_drawpanorama 0.
        3. Set mat_tonemap_force_min and mat_tonemap_force_max to the same value, so that the exposure does not change.
        4. Set host_framerate to lock the time step. To pause the game, set sv_pausable 1 and run pause. Do not set host_timescale 0.
        """;

    private static readonly string ProductVersion = typeof(McpServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";

    private readonly HttpListener _listener = new();
    private readonly McpTools _tools;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();

    public McpServer(int port, McpTools tools)
    {
        _tools = tools;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();

        _ = Task.Run(AcceptLoop);

        Log.Info($"Listening on http://127.0.0.1:{port}/mcp");
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _listener.Close();
        _shutdown.Dispose();
    }

    private async Task AcceptLoop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => Respond(context));
        }
    }

    private async Task Respond(HttpListenerContext context)
    {
        var response = context.Response;

        try
        {
            var (status, body) = await Handle(context.Request).ConfigureAwait(false);
            response.StatusCode = status;

            if (status == 405)
            {
                response.AddHeader("Allow", "POST");
            }

            if (body != null)
            {
                response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(response.OutputStream, body, McpTools.Json).ConfigureAwait(false);
            }

            response.Close();
        }
#pragma warning disable CA1031 // A failure to answer one request must not stop the server
        catch (Exception e)
#pragma warning restore CA1031
        {
            Log.Error($"Failed to answer a request: {e}");
            response.Abort();
        }
    }

    private async Task<(int Status, JsonNode? Body)> Handle(HttpListenerRequest request)
    {
        if (request.Url?.AbsolutePath != "/mcp")
        {
            return (404, Error(null, -32601, "The only endpoint is POST /mcp."));
        }

        if (request.HttpMethod != "POST")
        {
            return (405, null);
        }

        if (!IsLoopback(request))
        {
            return (403, Error(null, -32600, "The server accepts only loopback hosts and origins."));
        }

        JsonObject? rpc;

        try
        {
            rpc = await JsonNode.ParseAsync(request.InputStream, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }).ConfigureAwait(false) as JsonObject;
        }
        catch (JsonException)
        {
            return (400, Error(null, -32700, "Parse error"));
        }

        if (rpc == null)
        {
            return (400, Error(null, -32600, "Expected a single JSON-RPC request object."));
        }

        var id = rpc["id"]?.DeepClone();

        // Without an id the message is a notification
        if (rpc.ContainsKey("id") && id?.GetValueKind() is not (JsonValueKind.String or JsonValueKind.Number))
        {
            return (400, Error(null, -32600, "'id' must be a string or a number."));
        }

        try
        {
            return await Handle(request, rpc, id).ConfigureAwait(false);
        }
        catch (InvalidOperationException e)
        {
            // A member of the wrong JSON type
            return (400, Error(id, -32600, $"Invalid request: {e.Message}"));
        }
    }

    private async Task<(int Status, JsonNode? Body)> Handle(HttpListenerRequest request, JsonObject rpc, JsonNode? id)
    {
        var method = (string?)rpc["method"];
        var parameters = rpc["params"] as JsonObject;

        if (method == null)
        {
            return (400, Error(id, -32600, "Expected a single JSON-RPC request object."));
        }

        if (parameters == null && rpc["params"] != null)
        {
            return (400, Error(id, -32600, "'params' must be an object."));
        }

        if (id == null)
        {
            if (method == "notifications/cancelled")
            {
                await Cancel(parameters?["requestId"]).ConfigureAwait(false);
            }

            return (202, null);
        }

        var meta = parameters?["_meta"];
        var version = (string?)meta?[Meta + "protocolVersion"];
        var toolName = method == "tools/call" ? (string?)parameters?["name"] : null;

        if (version == null || meta?[Meta + "clientCapabilities"] == null)
        {
            return (400, Error(id, -32602, $"MCP {ProtocolVersion} needs '{Meta}protocolVersion' and '{Meta}clientCapabilities' in params._meta of every request."));
        }

        if (CheckHeaders(request, method, version, toolName) is { } mismatch)
        {
            return (400, Error(id, -32020, mismatch));
        }

        if (version != ProtocolVersion)
        {
            var versions = new JsonObject
            {
                ["supported"] = new JsonArray(ProtocolVersion),
                ["requested"] = version,
            };

            return (400, Error(id, -32022, $"Unsupported protocol version. This server speaks only MCP {ProtocolVersion}.", versions));
        }

        return method switch
        {
            "server/discover" => (200, Result(id, Cacheable(new JsonObject
            {
                ["supportedVersions"] = new JsonArray(ProtocolVersion),
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["instructions"] = Instructions,
            }))),
            "tools/list" => (200, Result(id, Cacheable(_tools.List()))),
            "tools/call" when toolName == null => (400, Error(id, -32602, "tools/call needs 'name'.")),
            "tools/call" when !_tools.Has(toolName) => (400, Error(id, -32602, $"Unknown tool '{toolName}'. The tools are {string.Join(", ", _tools.Names)}.")),
            "tools/call" when parameters?["arguments"] is not (null or JsonObject) => (400, Error(id, -32602, "'arguments' must be an object.")),
            "tools/call" => (200, Result(id, await Call(toolName, parameters?["arguments"] as JsonObject ?? [], id).ConfigureAwait(false))),
            _ => (404, Error(id, -32601, $"Method not found: {method}")),
        };
    }

    /// <summary>Rejects hosts and origins that are not loopback, so that a browser page cannot drive the game through DNS rebinding.</summary>
    private static bool IsLoopback(HttpListenerRequest request)
    {
        if (request.Url?.IsLoopback != true)
        {
            return false;
        }

        var origin = request.Headers["Origin"];

        if (origin == null)
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
    }

    /// <summary>
    /// The transport copies the version, method and tool name into headers. They must match the body,
    /// so that an intermediary that routes on a header and this server that acts on the body agree.
    /// </summary>
    private static string? CheckHeaders(HttpListenerRequest request, string method, string version, string? toolName)
    {
        if (request.Headers["MCP-Protocol-Version"] != version)
        {
            return "MCP-Protocol-Version header does not match params._meta.";
        }

        if (request.Headers["Mcp-Method"] != method)
        {
            return "Mcp-Method header does not match the method.";
        }

        if (toolName != null && request.Headers["Mcp-Name"] != toolName)
        {
            return "Mcp-Name header does not match the tool name.";
        }

        return null;
    }

    private async Task Cancel(JsonNode? requestId)
    {
        if (requestId == null || !_running.TryGetValue(requestId.ToJsonString(), out var call))
        {
            return;
        }

        try
        {
            await call.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The call completed before the cancel request
        }
    }

    private async Task<JsonObject> Call(string name, JsonObject arguments, JsonNode id)
    {
        using var call = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var key = id.ToJsonString();

        // Ids are unique only per client, so a cancel request cannot reach a second running call with the same id
        var cancellable = _running.TryAdd(key, call);

        try
        {
            return await _tools.Call(name, arguments, call.Token).ConfigureAwait(false);
        }
        finally
        {
            if (cancellable)
            {
                _running.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Marks a result as the same for every caller for as long as the server runs.</summary>
    private static JsonObject Cacheable(JsonObject result)
    {
        result["ttlMs"] = int.MaxValue;
        result["cacheScope"] = "public";
        return result;
    }

    private static JsonObject Result(JsonNode id, JsonObject result)
    {
        result["resultType"] = "complete";
        result["_meta"] = new JsonObject
        {
            [Meta + "serverInfo"] = new JsonObject
            {
                ["name"] = "source2mcp",
                ["version"] = ProductVersion,
            },
        };

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result,
        };
    }

    private static JsonObject Error(JsonNode? id, int code, string message, JsonNode? data = null)
    {
        var error = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        };

        if (data != null)
        {
            error["data"] = data;
        }

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error,
        };
    }
}
