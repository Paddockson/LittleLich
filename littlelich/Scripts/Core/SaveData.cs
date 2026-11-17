using System.Text.Json;
using System.Text.Json.Nodes;

namespace LittleLich.Core;

/// <summary>
/// One section of a save file, owned by a single <see cref="ISaveable"/>.
/// A thin wrapper over a JSON object so each feature stores plain values
/// (ids, counts, levels) without the save system knowing its shape.
/// </summary>
public sealed class SaveData
{
    public JsonObject Node { get; }

    public SaveData() : this(new JsonObject()) { }
    public SaveData(JsonObject node) { Node = node; }

    public bool IsEmpty => Node.Count == 0;
    public bool Has(string key) => Node.ContainsKey(key);

    public SaveData Set<T>(string key, T value)
    {
        Node[key] = JsonSerializer.SerializeToNode(value, GameJson.Save);
        return this;
    }

    /// <summary>Reads a value, or returns <paramref name="fallback"/> if missing or unreadable.</summary>
    public T Get<T>(string key, T fallback = default!)
    {
        if (!Node.TryGetPropertyValue(key, out var n) || n is null) return fallback;
        try { return n.Deserialize<T>(GameJson.Save) ?? fallback; }
        catch (JsonException) { return fallback; }
        catch (System.InvalidOperationException) { return fallback; }
    }

    /// <summary>Stores a whole plain C# object as this section's content.</summary>
    public static SaveData From<T>(T value) =>
        new((JsonSerializer.SerializeToNode(value, GameJson.Save) as JsonObject) ?? new JsonObject());

    /// <summary>Reads this section back as a plain C# object.</summary>
    public T As<T>() where T : new() => Node.Deserialize<T>(GameJson.Save) ?? new T();
}
