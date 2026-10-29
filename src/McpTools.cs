using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Source2Mcp;

/// <summary>
/// Marks a method of <see cref="McpTools"/> as a tool named after it in snake_case. Its parameters
/// are the arguments: each needs a <see cref="DescriptionAttribute"/>, a <see cref="RangeAttribute"/>
/// bounds it, and a default makes it optional. The method returns the reply object or a task of it.
/// The return value is serialized as the JSON reply.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ToolAttribute(string description) : Attribute
{
    public string Description => description;

    /// <summary>The tool answers while another call runs, instead of waiting for it.</summary>
    public bool Concurrent { get; init; }
}

/// <summary>A failure worded for the caller.</summary>
#pragma warning disable CA1032 // Implement standard exception constructors
internal sealed class ToolException(string message) : Exception(message);
#pragma warning restore CA1032

/// <summary>The tools exposed over <see cref="McpServer"/>, declared by <see cref="ToolAttribute"/>.</summary>
internal sealed partial class McpTools : IAsyncDisposable
{
    /// <param name="Name">The argument name, in snake case.</param>
    private sealed record ToolParameter(string Name, Type Type, bool HasDefaultValue, object? DefaultValue, RangeAttribute? Range);

    private sealed record Tool(string Name, ToolAttribute Info, MethodInfo Method, JsonObject Schema, ToolParameter[] Parameters);

    /// <summary>
    /// Snake case names, no nulls or empty collections, and no escape of text that only a model reads.
    /// Positions round to two decimals and other floats to three.
    /// </summary>
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower), new Vector3Converter(), new FloatConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { JsonModifiers.SkipEmpty } },
    };

    private static readonly JsonSchemaExporterOptions SchemaOptions = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = static (context, schema) =>
        {
            // The custom converters leave these unconstrained
            var type = Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type;

            if (type == typeof(Vector3))
            {
                return new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "number" }, ["minItems"] = 3, ["maxItems"] = 3 };
            }

            if (type == typeof(float))
            {
                return new JsonObject { ["type"] = "number" };
            }

            // Optional arguments are left out rather than passed as null
            if (schema is JsonObject obj)
            {
                if (obj["type"] is JsonArray types)
                {
                    obj["type"] = FirstNonNullType(types);
                }

                if (obj.TryGetPropertyValue("default", out var defaultValue) && defaultValue == null)
                {
                    obj.Remove("default");
                }
            }

            return schema;
        },
    };

    private readonly Dictionary<string, Tool> _tools = [];

    // The game state that the tools drive is global, so calls run one at a time unless marked concurrent
    private readonly SemaphoreSlim _callLock = new(1, 1);

    // The call that holds the lock, so that get_status can name a call that blocks the others
    private volatile RunningCall? _runningCall;

    private sealed record RunningCall(string Tool, long StartTimestamp);

    public McpTools()
    {
        foreach (var method in typeof(McpTools).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (method.GetCustomAttribute<ToolAttribute>() is { } info)
            {
                var name = JsonNamingPolicy.SnakeCaseLower.ConvertName(method.Name);
                _tools.Add(name, CreateTool(name, info, method));
            }
        }
    }

    public bool Has(string name) => _tools.ContainsKey(name);

    public IEnumerable<string> Names => _tools.Keys;

    public JsonObject List()
    {
        var tools = new JsonArray();

        foreach (var tool in _tools.Values)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Info.Description,
                ["inputSchema"] = tool.Schema.DeepClone(),
            });
        }

        return new JsonObject { ["tools"] = tools };
    }

    public async Task<JsonObject> Call(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        var tool = _tools[name];
        var locked = false;

        try
        {
            if (!tool.Info.Concurrent)
            {
                await _callLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                locked = true;
                _runningCall = new RunningCall(name, Stopwatch.GetTimestamp());
            }

            var result = await Invoke(tool, arguments, cancellationToken).ConfigureAwait(false);

            return Reply(JsonSerializer.Serialize(result ?? new { }, Json));
        }
        catch (OperationCanceledException)
        {
            return Reply("The call was cancelled.", isError: true);
        }
        catch (Exception e) when (e is ToolException or ArgumentException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            Log.Info($"{name} failed: {e.Message}");
            return Reply(e.Message, isError: true);
        }
#pragma warning disable CA1031 // Any other failure is a bug. The caller gets it as an error reply, and the transport continues.
        catch (Exception e)
#pragma warning restore CA1031
        {
            Log.Error($"{name} failed unexpectedly: {e}");
            return Reply($"Unexpected {e.GetType().Name}: {e.Message}", isError: true);
        }
        finally
        {
            if (locked)
            {
                _runningCall = null;
                _callLock.Release();
            }
        }
    }

    /// <summary>A tool result: one text item.</summary>
    private static JsonObject Reply(string text, bool isError = false)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        };

        if (isError)
        {
            result["isError"] = true;
        }

        return result;
    }

    private static Tool CreateTool(string name, ToolAttribute info, MethodInfo method)
    {
        var parameters = method.GetParameters();
        var described = new ToolParameter[parameters.Length];
        var properties = new JsonObject();
        var required = new JsonArray();

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var parameterName = JsonNamingPolicy.SnakeCaseLower.ConvertName(parameter.Name!);
            var range = parameter.GetCustomAttribute<RangeAttribute>();

            described[i] = new ToolParameter(parameterName, parameter.ParameterType, parameter.HasDefaultValue, parameter.DefaultValue, range);

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                continue;
            }

            var schema = JsonSchemaExporter.GetJsonSchemaAsNode(Json, parameter.ParameterType, SchemaOptions) as JsonObject ?? [];

            schema["description"] = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? throw new InvalidOperationException($"{method.Name}({parameter.Name}) has no description.");

            if (range != null)
            {
                var valueType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

                // Integer bounds would truncate the value before the compare
                if (range.OperandType == typeof(int) && valueType == typeof(float))
                {
                    throw new InvalidOperationException($"{method.Name}({parameter.Name}) needs double bounds.");
                }

                schema[range.MinimumIsExclusive ? "exclusiveMinimum" : "minimum"] = JsonValue.Create(Convert.ToDouble(range.Minimum, CultureInfo.InvariantCulture));

                if (HasMaximum(range))
                {
                    schema["maximum"] = JsonValue.Create(Convert.ToDouble(range.Maximum, CultureInfo.InvariantCulture));
                }
            }

            properties[parameterName] = schema;

            if (!parameter.HasDefaultValue)
            {
                required.Add(parameterName);
            }
        }

        var toolSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
        };

        return new Tool(name, info, method, toolSchema, described);
    }

    private static JsonNode? FirstNonNullType(JsonArray types)
    {
        foreach (var type in types)
        {
            if ((string?)type != "null")
            {
                return type!.DeepClone();
            }
        }

        return null;
    }

    private static bool HasMaximum(RangeAttribute range) => range.Maximum is not (int.MaxValue or double.MaxValue);

    private async Task<object?> Invoke(Tool tool, JsonObject givenArguments, CancellationToken cancellationToken)
    {
        var properties = (JsonObject)tool.Schema["properties"]!;
        var arguments = new JsonObject();

        foreach (var (key, value) in givenArguments)
        {
            // Callers often guess the camelCase spelling of an argument
            var name = JsonNamingPolicy.SnakeCaseLower.ConvertName(key);

            if (!properties.ContainsKey(name))
            {
                var takes = properties.Count == 0 ? "none" : string.Join(", ", properties.Select(static p => p.Key));
                throw new ArgumentException($"Unknown argument '{key}'. {tool.Name} takes {takes}.");
            }

            if (arguments.ContainsKey(name))
            {
                throw new ArgumentException($"'{key}' and '{name}' are the same argument. Pass only one.");
            }

            arguments[name] = value?.DeepClone();
        }

        var values = new object?[tool.Parameters.Length];

        for (var i = 0; i < tool.Parameters.Length; i++)
        {
            var parameter = tool.Parameters[i];

            if (parameter.Type == typeof(CancellationToken))
            {
                values[i] = cancellationToken;
                continue;
            }

            var node = arguments[parameter.Name];

            if (node != null)
            {
                values[i] = Deserialize(parameter.Name, node, parameter.Type, (JsonObject)properties[parameter.Name]!);
            }
            else if (parameter.HasDefaultValue)
            {
                values[i] = parameter.DefaultValue;
            }
            else
            {
                throw new ArgumentException($"Missing '{parameter.Name}'.");
            }

            var range = parameter.Range;

            if (range != null && !range.IsValid(values[i]))
            {
                var lower = range.MinimumIsExclusive ? "more than" : "at least";
                var upper = HasMaximum(range) ? string.Create(CultureInfo.InvariantCulture, $" and at most {range.Maximum}") : string.Empty;
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"'{parameter.Name}' must be {lower} {range.Minimum}{upper}, got {node?.ToJsonString()}."));
            }
        }

        var returned = tool.Method.Invoke(this, BindingFlags.DoNotWrapExceptions, null, values, null);

        if (returned is Task<object> task)
        {
            return await task.ConfigureAwait(false);
        }

        return returned;
    }

    private static object? Deserialize(string name, JsonNode node, Type type, JsonObject schema)
    {
        try
        {
            // The enum converter also takes numbers and comma-separated lists, which the schema does not offer
            if (schema["enum"] is JsonArray names)
            {
                var isName = false;

                if (node.GetValueKind() == JsonValueKind.String)
                {
                    foreach (var value in names)
                    {
                        if (string.Equals((string?)value, (string?)node, StringComparison.OrdinalIgnoreCase))
                        {
                            isName = true;
                            break;
                        }
                    }
                }

                if (!isName)
                {
                    throw new JsonException();
                }
            }

            return node.Deserialize(type, Json);
        }
        catch (JsonException)
        {
            throw new ArgumentException($"'{name}' must be {Expected(schema)}, got {node.ToJsonString()}.");
        }
    }

    /// <summary>What a value of this schema looks like, for an error message.</summary>
    private static string Expected(JsonObject schema)
    {
        if (schema["enum"] is JsonArray values)
        {
            return $"one of {string.Join(", ", values)}";
        }

        return (string?)schema["type"] switch
        {
            "array" when schema["minItems"] is not null => "[x, y, z]",
            "array" => "an array",
            "object" => "an object",
            "integer" => "an integer",
            "boolean" => "true or false",
            var other => $"a {other}",
        };
    }
}
