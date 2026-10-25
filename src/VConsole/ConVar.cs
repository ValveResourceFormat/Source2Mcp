using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;

namespace VConsole;

/// <summary>One cvar or command from CVRB.</summary>
/// <param name="Flags">FCVAR flags. Values use more than 32 bits.</param>
/// <param name="EnumValues">The names of the enum values.</param>
internal sealed record ConVar(
    string Name,
    ulong Flags,
    string Value,
    string HelpText,
    string DefaultValue,
    string MinValue,
    string MaxValue,
    bool IsCommand,
    IReadOnlyList<string> EnumValues)
{
    /// <summary>Reads the KV3 payload of CVRB. The list has one entry for a value change, or all entries on connect.</summary>
    /// <exception cref="InvalidDataException">The payload is not KV3, or it has no <c>m_vecDetails</c> array.</exception>
    public static List<ConVar> ParseList(ReadOnlyMemory<byte> payload)
    {
        KVObject root;

        try
        {
            // The full list is hundreds of KB, so read the array behind the payload instead of a copy
            if (!MemoryMarshal.TryGetArray(payload, out var segment))
            {
                segment = payload.ToArray();
            }

            using var stream = new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false);
            using var reader = new BinaryReader(stream);

            var kv3 = new BinaryKV3 { Resource = null! };
            kv3.Read(reader);
            root = kv3.Data.Root;
        }
#pragma warning disable CA1031 // The KV3 reader throws many exception types for bad data. All of them mean the same thing here.
        catch (Exception e)
#pragma warning restore CA1031
        {
            throw new InvalidDataException($"CVRB does not contain valid KV3: {e.Message}", e);
        }

        if (!root.TryGetValue("m_vecDetails", out var details) || !details.IsArray)
        {
            throw new InvalidDataException("CVRB has no m_vecDetails array.");
        }

        var conVars = new List<ConVar>(details.Count);

        for (var i = 0; i < details.Count; i++)
        {
            conVars.Add(FromKeyValues(details[i]));
        }

        return conVars;
    }

    private static ConVar FromKeyValues(KVObject entry)
    {
        var enumValues = new List<string>();

        if (entry.TryGetValue("m_vecEnum", out var values) && values.IsArray)
        {
            for (var i = 0; i < values.Count; i++)
            {
                enumValues.Add(GetString(values[i], "m_szName"));
            }
        }

        return new ConVar(
            Name: GetString(entry, "m_szName"),
            Flags: GetUInt64(entry, "m_nFlags"),
            Value: GetString(entry, "m_szValue"),
            HelpText: GetString(entry, "m_szHelpString"),
            DefaultValue: GetString(entry, "m_szDefault"),
            MinValue: GetString(entry, "m_szMin"),
            MaxValue: GetString(entry, "m_szMax"),
            IsCommand: GetBoolean(entry, "m_bCommand"),
            EnumValues: enumValues);
    }

    // A missing key gets a default value, because a later game build can remove a key

    private static string GetString(KVObject entry, string key) => entry.TryGetValue(key, out var value) ? (string)value : string.Empty;

    private static ulong GetUInt64(KVObject entry, string key) => entry.TryGetValue(key, out var value) ? (ulong)value : 0;

    private static bool GetBoolean(KVObject entry, string key) => entry.TryGetValue(key, out var value) && (bool)value;
}
