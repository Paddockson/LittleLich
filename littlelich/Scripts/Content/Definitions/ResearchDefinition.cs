using System.Collections.Generic;
using System.Linq;
using LittleLich.Core;

namespace LittleLich.Content.Definitions;

/// <summary>Family base for research tomes. v0.1 has one: necromancy_1.</summary>
public abstract class TomeDefinitionBase : Definition
{
    /// <summary>Skill that gates how deep the tome goes and earns XP from it (e.g. necromancy).</summary>
    public string Skill { get; init; } = "";

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Skill.Length > 0 && !SkillIds.All.Contains(Skill)) errors.Add("skill", $"'{Skill}' is not a skill");
    }

    /// <summary>The tome's nodes must exist, have a root, and form no cycles.</summary>
    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        var nodes = db.All<ResearchNodeDefinition>().Where(n => n.Tome == Id).ToDictionary(n => n.Id);
        if (nodes.Count == 0) { errors.Add(null, "tome has no research nodes"); return; }
        if (!nodes.Values.Any(n => n.Requires.Count == 0)) errors.Add(null, "tome has no root node (a node with no requires)");

        // Cycle check (depth-first, colour marking).
        var state = new Dictionary<string, int>(); // 1 = visiting, 2 = done
        foreach (var start in nodes.Keys)
        {
            if (FindCycle(start, nodes, state, new List<string>()) is { } cycle)
            {
                errors.Add("requires", $"cycle: {string.Join(" -> ", cycle)}");
                return;
            }
        }
    }

    private static List<string>? FindCycle(string id, Dictionary<string, ResearchNodeDefinition> nodes, Dictionary<string, int> state, List<string> path)
    {
        if (state.GetValueOrDefault(id) == 2 || !nodes.TryGetValue(id, out var node)) return null;
        path.Add(id);
        if (state.GetValueOrDefault(id) == 1) return path.SkipWhile(x => x != id).ToList();
        state[id] = 1;
        foreach (var r in node.Requires)
            if (FindCycle(r, nodes, state, path) is { } c) return c;
        state[id] = 2;
        path.RemoveAt(path.Count - 1);
        return null;
    }
}

/// <summary>JSON type "tome".</summary>
public sealed class TomeDefinition : TomeDefinitionBase { }

/// <summary>
/// One research effect (data): unlock_unit / unlock_recipe / unlock_activity { id }, unlock_bench,
/// building_level { building, levels }, stat_mod { stat, op, value }.
/// </summary>
public sealed class ResearchEffect
{
    public static readonly IReadOnlySet<string> Types = new HashSet<string>
        { "unlock_unit", "unlock_recipe", "unlock_activity", "unlock_bench", "building_level", "stat_mod" };

    public string Type { get; init; } = "";
    public string? Id { get; init; }
    public string? Building { get; init; }
    public int Levels { get; init; }
    public string? Stat { get; init; }
    public string Op { get; init; } = "add";
    public double Value { get; init; }

    public override string ToString() => Type switch
    {
        "unlock_unit" => $"Unlocks unit {Id}",
        "unlock_recipe" => $"Unlocks recipe {Id}",
        "unlock_activity" => $"Unlocks activity {Id}",
        "unlock_bench" => "Adds a research bench",
        "building_level" => $"+{Levels} max levels for {Building}",
        "stat_mod" => Op == "mul" ? $"{Stat} x{Value:0.###}" : $"{Stat} {(Value >= 0 ? "+" : "")}{Value:0.###}",
        _ => Type,
    };
}

/// <summary>Family base for research nodes.</summary>
public abstract class ResearchNodeDefinitionBase : Definition
{
    public static readonly IReadOnlySet<string> Sizes = new HashSet<string> { "small", "notable", "keystone" };

    public string Tome { get; init; } = "";
    public string Description { get; init; } = "";
    public List<string> Requires { get; init; } = new();
    /// <summary>Grid position for drawing the tree, [x, y].</summary>
    public List<double> Position { get; init; } = new() { 0, 0 };
    /// <summary>small, notable or keystone (drawing size).</summary>
    public string Size { get; init; } = "small";
    /// <summary>Paid up front when research starts: id -> count (resources or items).</summary>
    public Dictionary<string, long> Cost { get; init; } = new();
    /// <summary>Bench work needed (one staffed necromancer does 1 work per tick, before bonuses).</summary>
    public long ResearchTicks { get; init; } = 1;
    /// <summary>Tome skill level needed to start (0 = none).</summary>
    public int SkillLevel { get; init; }
    /// <summary>XP for the tome's skill on completion.</summary>
    public double Xp { get; init; }
    public List<ResearchEffect> Effects { get; init; } = new();

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (string.IsNullOrWhiteSpace(Tome)) errors.Add("tome", "is missing");
        if (Position.Count != 2) errors.Add("position", "must be [x, y]");
        if (!Sizes.Contains(Size)) errors.Add("size", $"must be one of {string.Join(", ", Sizes)}");
        if (ResearchTicks < 1) errors.Add("research_ticks", "must be 1 or more");
        if (SkillLevel < 0 || SkillLevel > 999) errors.Add("skill_level", "must be 0 to 999");
        if (Xp < 0) errors.Add("xp", "must be 0 or more");
        if (Requires.Contains(Id)) errors.Add("requires", "a node cannot require itself");
        foreach (var (id, n) in Cost) if (n < 1) errors.Add($"cost.{id}", "must be 1 or more");
        for (var i = 0; i < Effects.Count; i++)
        {
            var e = Effects[i];
            var f = $"effects[{i}]";
            if (!ResearchEffect.Types.Contains(e.Type)) { errors.Add($"{f}.type", $"unknown effect '{e.Type}' ({string.Join(", ", ResearchEffect.Types)})"); continue; }
            if (e.Type.StartsWith("unlock_") && e.Type != "unlock_bench" && string.IsNullOrWhiteSpace(e.Id)) errors.Add($"{f}.id", "is missing");
            if (e.Type == "building_level" && (string.IsNullOrWhiteSpace(e.Building) || e.Levels < 1)) errors.Add(f, "building_level needs building and levels >= 1");
            if (e.Type == "stat_mod" && (string.IsNullOrWhiteSpace(e.Stat) || e.Op is not ("add" or "mul") || !double.IsFinite(e.Value)))
                errors.Add(f, "stat_mod needs stat, op add|mul and a value");
        }
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        if (!db.Contains<TomeDefinitionBase>(Tome)) errors.Add("tome", $"no tome '{Tome}'");
        foreach (var r in Requires)
        {
            if (!db.TryGet<ResearchNodeDefinitionBase>(r, out var req)) errors.Add("requires", $"no research node '{r}'");
            else if (req.Tome != Tome) errors.Add("requires", $"'{r}' is in another tome ({req.Tome})");
        }
        foreach (var id in Cost.Keys)
            if (!StockIds.Exists(db, id)) errors.Add($"cost.{id}", $"no resource or item '{id}'");
        for (var i = 0; i < Effects.Count; i++)
        {
            var e = Effects[i];
            var f = $"effects[{i}]";
            switch (e.Type)
            {
                case "unlock_recipe" when RecipeKind(db, e.Id!) is null:
                    errors.Add($"{f}.id", $"no recipe, transmutation or summon '{e.Id}'");
                    break;
                case "unlock_activity" when !db.Contains<ActivityDefinitionBase>(e.Id!):
                    errors.Add($"{f}.id", $"no activity '{e.Id}'");
                    break;
                case "building_level" when !db.Contains<BuildingDefinitionBase>(e.Building!):
                    errors.Add($"{f}.building", $"no building '{e.Building}'");
                    break;
                case "unlock_unit" when !(db.TryGet<UnitDefinitionBase>(e.Id!, out var unit) && unit.IsPlayer):
                    errors.Add($"{f}.id", $"no player unit '{e.Id}'");
                    break;
            }
        }
    }

    /// <summary>Which unlock kind an unlock_recipe id refers to (recipe, transmutation or summon), or null.</summary>
    public static string? RecipeKind(ContentDatabase db, string id) =>
        db.Contains<RecipeDefinitionBase>(id) ? Unlocks.Recipe :
        db.Contains<TransmutationDefinitionBase>(id) ? Unlocks.Transmutation :
        db.Contains<SummonDefinitionBase>(id) ? Unlocks.Summon : null;
}

/// <summary>JSON type "research_node".</summary>
public sealed class ResearchNodeDefinition : ResearchNodeDefinitionBase { }

/// <summary>
/// JSON type "research_bench" (exactly one, id "research_bench"): benches at the start, necromancer slots per bench
/// and work rates. Work per tick on a bench = base_work + staffed x work_per_necromancer.
/// </summary>
public sealed class ResearchBenchDefinition : Definition
{
    public int BaseBenches { get; init; } = 1;
    public long SlotsPerBench { get; init; } = 5;
    public double BaseWork { get; init; }
    public double WorkPerNecromancer { get; init; } = 1;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Id != "research_bench") errors.Add("id", "must be research_bench");
        if (BaseBenches < 1) errors.Add("base_benches", "must be 1 or more");
        if (SlotsPerBench < 1) errors.Add("slots_per_bench", "must be 1 or more");
        if (!(BaseWork >= 0) || !(WorkPerNecromancer >= 0)) errors.Add("base_work", "work must be 0 or more");
    }
}
