using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LittleLich.Core.Saves;

/// <summary>
/// A whole save: header (version, time) plus one section per <see cref="ISaveable"/>, keyed by its SaveKey.
///
/// On disk:
/// <code>
/// {
///   "version": 1,
///   "saved_at_utc": "2026-11-17T14:00:00.0000000Z",
///   "sections": { "meta": { ... }, "lich": { ... } }
/// }
/// </code>
/// </summary>
public sealed class SaveFile
{
    public const string VersionKey = "version";
    public const string SavedAtKey = "saved_at_utc";
    public const string SectionsKey = "sections";

    public int Version { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public Dictionary<string, SaveData> Sections { get; } = new(StringComparer.Ordinal);

    public SaveData Section(string key) => Sections.TryGetValue(key, out var d) ? d : new SaveData();

    public JsonObject ToJson()
    {
        var sections = new JsonObject();
        foreach (var (key, data) in Sections)
            sections[key] = data.Node.DeepClone();

        return new JsonObject
        {
            [VersionKey] = Version,
            [SavedAtKey] = SavedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            [SectionsKey] = sections,
        };
    }

    public string ToJsonString() => ToJson().ToJsonString(GameJson.Save);

    /// <summary>Reads only the header. Throws <see cref="SaveFormatException"/> if it is missing or malformed.</summary>
    public static (int Version, DateTime SavedAtUtc) ReadHeader(JsonObject root)
    {
        if (root[VersionKey] is not JsonValue v || !v.TryGetValue<int>(out var version) || version < 1)
            throw new SaveFormatException($"missing or invalid \"{VersionKey}\"");

        if (root[SavedAtKey] is not JsonValue t || !t.TryGetValue<string>(out var ts) ||
            !DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var savedAt))
            throw new SaveFormatException($"missing or invalid \"{SavedAtKey}\"");

        return (version, DateTime.SpecifyKind(savedAt, DateTimeKind.Utc));
    }

    /// <summary>Builds a SaveFile from an already-migrated root object.</summary>
    public static SaveFile FromJson(JsonObject root)
    {
        var (version, savedAt) = ReadHeader(root);
        var file = new SaveFile { Version = version, SavedAtUtc = savedAt };

        switch (root[SectionsKey])
        {
            case null:
                break;
            case JsonObject sections:
                foreach (var (key, node) in sections)
                {
                    if (node is not JsonObject obj)
                        throw new SaveFormatException($"section \"{key}\" is not an object");
                    file.Sections[key] = new SaveData((JsonObject)obj.DeepClone());
                }
                break;
            default:
                throw new SaveFormatException($"\"{SectionsKey}\" is not an object");
        }
        return file;
    }

    public static JsonObject ParseRoot(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException e) { throw new SaveFormatException("not valid JSON: " + e.Message, e); }
        return node as JsonObject ?? throw new SaveFormatException("top level is not a JSON object");
    }
}

/// <summary>The save file exists but cannot be read (corrupt, truncated, wrong shape, too new).</summary>
public sealed class SaveFormatException : Exception
{
    public SaveFormatException(string message, Exception? inner = null) : base(message, inner) { }
}
