using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LittleLich.Content.Definitions;
using LittleLich.Core;

namespace LittleLich.Content;

/// <summary>One JSON content file: a display path (for errors) and its text.</summary>
public sealed record ContentSource(string Path, string Json)
{
    /// <summary>Reads every *.json under <paramref name="directory"/> (recursive), sorted by path for stable order.</summary>
    public static IEnumerable<ContentSource> FromDirectory(string directory)
    {
        if (!Directory.Exists(directory)) yield break;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var rel = System.IO.Path.GetRelativePath(directory, file).Replace('\\', '/');
            yield return new ContentSource(rel, File.ReadAllText(file));
        }
    }
}

/// <summary>
/// Loads JSON content into a <see cref="ContentDatabase"/>.
///
/// A file is either
///   an array of entries, each with a "type" field, or
///   an object { "type": "resource", "entries": [ ... ] } where "type" is the default for its entries.
///
/// Errors are collected across all files and thrown together as a <see cref="ContentLoadException"/>,
/// each naming the file, the entry id and the field.
/// </summary>
public sealed class ContentLoader
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal);

    /// <summary>A loader with every definition type the game knows about.</summary>
    public static ContentLoader CreateDefault()
    {
        var loader = new ContentLoader();
        loader.Register<ResourceDefinition>("resource");
        loader.Register<WeaponDefinition>("weapon");
        loader.Register<ArmourDefinition>("armour");
        loader.Register<AccessoryDefinition>("accessory");
        loader.Register<RuneDefinition>("rune");
        loader.Register<PotionDefinition>("potion");
        loader.Register<BombDefinition>("bomb");
        loader.Register<MaterialDefinition>("material");
        loader.Register<GatheringActivityDefinition>("gathering_activity");
        loader.Register<RecipeDefinition>("recipe");
        loader.Register<ManaWellDefinition>("mana_well");
        loader.Register<ConverterDefinition>("converter");
        loader.Register<TransmuterDefinition>("transmuter");
        loader.Register<SummoningCircleDefinition>("summoning_circle");
        loader.Register<UndeadMarketDefinition>("undead_market");
        loader.Register<MagicalStorageDefinition>("magical_storage");
        loader.Register<TransmutationDefinition>("transmutation");
        loader.Register<SummonDefinition>("summon");
        loader.Register<TomeDefinition>("tome");
        loader.Register<ResearchNodeDefinition>("research_node");
        loader.Register<ResearchBenchDefinition>("research_bench");
        loader.Register<UnitDefinition>("unit");
        loader.Register<MapNodeDefinition>("map_node");
        loader.Register<AreaDefinition>("area");
        // Later phases register units, research nodes, upgrades ...
        return loader;
    }

    public ContentLoader Register<T>(string typeName) where T : Definition
    {
        if (typeof(T).IsAbstract)
            throw new ArgumentException($"{typeof(T).Name} is abstract; register concrete definition classes only.");
        _types[typeName] = typeof(T);
        return this;
    }

    public IReadOnlyCollection<string> KnownTypes => _types.Keys;

    public ContentDatabase Load(IEnumerable<ContentSource> sources)
    {
        var db = new ContentDatabase();
        var errors = new List<ContentError>();

        foreach (var source in sources)
            LoadFile(source, db, errors);

        if (errors.Count == 0)
        {
            foreach (var def in db.Everything())
                def.ValidateReferences(db, new ContentErrors(errors, def.SourceFile, def.Id));
        }

        if (errors.Count > 0) throw new ContentLoadException(errors);
        return db;
    }

    private void LoadFile(ContentSource source, ContentDatabase db, List<ContentError> errors)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(source.Json,
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            errors.Add(new ContentError(source.Path, null, null,
                $"invalid JSON at line {e.LineNumber + 1}, position {e.BytePositionInLine + 1}: {FirstLine(e.Message)}"));
            return;
        }

        string? defaultType = null;
        JsonArray? entries;
        switch (root)
        {
            case JsonArray arr:
                entries = arr;
                break;
            case JsonObject obj:
                defaultType = obj["type"] is JsonValue tv && tv.TryGetValue<string>(out var t) ? t : null;
                entries = obj["entries"] as JsonArray;
                var extra = obj.Select(kv => kv.Key).Where(k => k != "type" && k != "entries").ToList();
                foreach (var key in extra)
                    errors.Add(new ContentError(source.Path, null, key, "unknown top-level field (expected \"type\" and \"entries\")"));
                if (entries is null)
                {
                    errors.Add(new ContentError(source.Path, null, "entries", "is missing or not an array"));
                    return;
                }
                break;
            default:
                errors.Add(new ContentError(source.Path, null, null, "top level must be an array of entries or an object with \"entries\""));
                return;
        }

        for (int i = 0; i < entries.Count; i++)
            LoadEntry(source.Path, i, entries[i], defaultType, db, errors);
    }

    private void LoadEntry(string file, int index, JsonNode? node, string? defaultType, ContentDatabase db, List<ContentError> errors)
    {
        if (node is not JsonObject entry)
        {
            errors.Add(new ContentError(file, $"#{index}", null, "entry must be a JSON object"));
            return;
        }

        string? rawId = entry["id"] is JsonValue iv && iv.TryGetValue<string>(out var s) ? s : null;
        var errs = new ContentErrors(errors, file, rawId ?? $"#{index}");

        string? typeName = entry["type"] is JsonValue tv && tv.TryGetValue<string>(out var t) ? t : defaultType;
        if (string.IsNullOrEmpty(typeName))
        {
            errs.Add("type", "is missing (set it on the entry or as the file's default \"type\")");
            return;
        }
        if (!_types.TryGetValue(typeName, out var clrType))
        {
            errs.Add("type", $"unknown type '{typeName}' (known: {string.Join(", ", _types.Keys.OrderBy(k => k))})");
            return;
        }

        var copy = (JsonObject)entry.DeepClone();
        copy.Remove("type");

        Definition? def;
        try
        {
            def = (Definition?)copy.Deserialize(clrType, GameJson.Content);
        }
        catch (JsonException e)
        {
            errs.Add(FieldFromJsonException(e), FirstLine(e.Message));
            return;
        }
        if (def is null)
        {
            errs.Add(null, "entry deserialised to null");
            return;
        }

        def.Type = typeName;
        def.SourceFile = file;

        int before = errors.Count;
        def.Validate(errs);
        if (errors.Count > before) return;

        if (!db.TryAdd(def))
        {
            var family = ContentDatabase.FamilyOf(def.GetType());
            var existing = db.Find(family, def.Id);
            errs.Add("id", $"duplicate {family.Name} id '{def.Id}' (first defined in {existing?.SourceFile})");
        }
    }

    private static string? FieldFromJsonException(JsonException e)
    {
        // Unknown field: "The JSON property 'base_capp' could not be mapped to any .NET member ..."
        const string marker = "The JSON property '";
        int at = e.Message.IndexOf(marker, StringComparison.Ordinal);
        if (at >= 0)
        {
            int start = at + marker.Length;
            int end = e.Message.IndexOf('\'', start);
            if (end > start) return e.Message[start..end];
        }
        // Wrong value type: Path is "$.base_cap"
        if (!string.IsNullOrEmpty(e.Path) && e.Path != "$")
            return e.Path.StartsWith("$.") ? e.Path[2..] : e.Path;
        return null;
    }

    private static string FirstLine(string message)
    {
        int nl = message.IndexOf('\n');
        var line = nl >= 0 ? message[..nl] : message;
        // Strip STJ's trailing " Path: $ | LineNumber: ..." noise.
        int p = line.IndexOf(" Path: ", StringComparison.Ordinal);
        return (p >= 0 ? line[..p] : line).Trim();
    }
}
