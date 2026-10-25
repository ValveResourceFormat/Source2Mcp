using System.Collections;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Source2Mcp;

/// <summary>Writes a vector as [x, y, z], rounded to two decimals, and reads it from an array of three numbers.</summary>
internal sealed class Vector3Converter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var values = JsonSerializer.Deserialize<float[]>(ref reader, options);

        if (values is not { Length: 3 })
        {
            throw new JsonException();
        }

        return new Vector3(values[0], values[1], values[2]);
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(Math.Round(value.X, 2));
        writer.WriteNumberValue(Math.Round(value.Y, 2));
        writer.WriteNumberValue(Math.Round(value.Z, 2));
        writer.WriteEndArray();
    }
}

/// <summary>Writes a float rounded to three decimals, so that the reply does not show float noise.</summary>
internal sealed class FloatConverter : JsonConverter<float>
{
    public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException();
        }

        return reader.GetSingle();
    }

    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options) => writer.WriteNumberValue(Math.Round(value, 3));
}

internal static class JsonModifiers
{
    /// <summary>Omits empty collections and empty strings from replies.</summary>
    public static void SkipEmpty(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
        {
            if (property.PropertyType == typeof(string))
            {
                property.ShouldSerialize = static (_, value) => !string.IsNullOrEmpty((string?)value);
            }
            else if (property.PropertyType.IsAssignableTo(typeof(ICollection)))
            {
                property.ShouldSerialize = static (_, value) => value is ICollection { Count: > 0 };
            }
        }
    }
}
