using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Content.Definitions;

/// <summary>Army sections, front to back (match the formation).</summary>
public static class Sections
{
    public const string Front = "front";
    public const string Middle = "middle";
    public const string Back = "back";
    public static readonly IReadOnlyList<string> All = new[] { Front, Middle, Back };
}

/// <summary>A unit id and count in an army section.</summary>
public sealed class UnitCount
{
    public string Unit { get; init; } = "";
    public long Count { get; init; } = 1;
}

/// <summary>
/// Family base for world map nodes. The v0.1 map is a set of paths between nodes (no rings).
/// A node with <see cref="Area"/> holds that area; <see cref="Home"/> marks where armies start (the lich's crypt).
/// An area can be attacked when a path leads to it from home through nodes that are not uncleared areas.
/// </summary>
public abstract class MapNodeDefinitionBase : Definition
{
    public List<double> Position { get; init; } = new() { 0, 0 };
    /// <summary>Nodes this one has a path to (paths go both ways).</summary>
    public List<string> Links { get; init; } = new();
    public string? Area { get; init; }
    public bool Home { get; init; }

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Position.Count != 2) errors.Add("position", "must be [x, y]");
        if (Links.Contains(Id)) errors.Add("links", "a node cannot link to itself");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        foreach (var l in Links)
            if (!db.Contains<MapNodeDefinitionBase>(l)) errors.Add("links", $"no map node '{l}'");
        if (Area is { } a && !db.Contains<AreaDefinitionBase>(a)) errors.Add("area", $"no area '{a}'");
    }
}

/// <summary>JSON type "map_node".</summary>
public sealed class MapNodeDefinition : MapNodeDefinitionBase { }

/// <summary>
/// Family base for areas: defenders by section, and rewards when cleared (once).
/// v0.1: the Graveyard (another lich's small undead army; the gates are the walls) and the Woods (wild people).
/// </summary>
public abstract class AreaDefinitionBase : Definition
{
    public string Description { get; init; } = "";
    /// <summary>Defenders: section -> unit counts.</summary>
    public Dictionary<string, List<UnitCount>> Defenders { get; init; } = new();
    /// <summary>Granted once when cleared: resource or item id -> count (resources are capped by storage).</summary>
    public Dictionary<string, long> Rewards { get; init; } = new();

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        foreach (var s in Defenders.Keys.Where(s => !Sections.All.Contains(s)))
            errors.Add("defenders", $"'{s}' is not a section ({string.Join(", ", Sections.All)})");
        if (!Defenders.Values.SelectMany(x => x).Any(u => u.Count > 0)) errors.Add("defenders", "an area needs defenders");
        foreach (var u in Defenders.Values.SelectMany(x => x).Where(u => u.Count < 1)) errors.Add("defenders", $"count for '{u.Unit}' must be 1 or more");
        foreach (var (id, n) in Rewards) if (n < 1) errors.Add($"rewards.{id}", "must be 1 or more");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        foreach (var u in Defenders.Values.SelectMany(x => x))
            if (!db.Contains<UnitDefinitionBase>(u.Unit)) errors.Add("defenders", $"no unit '{u.Unit}'");
        foreach (var id in Rewards.Keys)
            if (!StockIds.Exists(db, id)) errors.Add($"rewards.{id}", $"no resource or item '{id}'");
        if (!db.All<MapNodeDefinitionBase>().Any(n => n.Area == Id)) errors.Add(null, "no map node holds this area");
    }
}

/// <summary>JSON type "area".</summary>
public sealed class AreaDefinition : AreaDefinitionBase { }
