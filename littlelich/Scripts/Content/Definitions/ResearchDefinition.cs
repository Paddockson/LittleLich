using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LittleLich.Core;

namespace LittleLich.Content.Definitions;

/// <summary>
/// Family base for research tomes (research revamp). A tome is owned from the start (<see cref="StartsOwned"/>) or
/// found later (<see cref="FoundBy"/>); only owned tomes are shown. Disabled tomes are not loaded into the game.
/// </summary>
public abstract class TomeDefinitionBase : Definition
{
    /// <summary>Short note under the name in the tome list ("Starting tome").</summary>
    public string Note { get; init; } = "";

    /// <summary>Owned in a new game.</summary>
    public bool StartsOwned { get; init; }

    /// <summary>How the tome is found when not owned at the start (placeholder rules).</summary>
    public TomeFoundBy? FoundBy { get; init; }

    /// <summary>Skill that earns XP from this tome's research (optional).</summary>
    public string Skill { get; init; } = "";

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Skill.Length > 0 && !SkillIds.All.Contains(Skill)) errors.Add("skill", $"'{Skill}' is not a skill");
        if (!StartsOwned && FoundBy is null) errors.Add("found_by", "a tome that is not owned at the start needs found_by");
        if (FoundBy is { AreaCleared: null or "" }) errors.Add("found_by.area_cleared", "is missing");
    }

    /// <summary>The tome's nodes must exist, have exactly one root, and form no cycles.</summary>
    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        var nodes = db.All<ResearchNodeDefinitionBase>().Where(n => n.Tome == Id).ToDictionary(n => n.Id);
        if (nodes.Count == 0) { errors.Add(null, "tome has no research nodes"); return; }
        var roots = nodes.Values.Count(n => n.Requires.Count == 0);
        if (roots != 1) errors.Add(null, $"tome must have exactly one root node (has {roots})");
        if (FoundBy?.AreaCleared is { Length: > 0 } a && !db.Contains<AreaDefinitionBase>(a)) errors.Add("found_by.area_cleared", $"no area '{a}'");

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

    private static List<string>? FindCycle(string id, Dictionary<string, ResearchNodeDefinitionBase> nodes, Dictionary<string, int> state, List<string> path)
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

/// <summary>{ "area_cleared": "graveyard" }: the tome is granted the first time that area is cleared.</summary>
public sealed class TomeFoundBy
{
    public string? AreaCleared { get; init; }
}

/// <summary>JSON type "tome".</summary>
public sealed class TomeDefinition : TomeDefinitionBase { }

/// <summary>
/// One structured research effect (research revamp). Four types:
///   unlock  { kind: unit | building | spell | feature | recipes, id }
///   modify  { stat, op: add | mul | set, value, target?, per_level?, unit? }
///   trigger { on, source?, give }          (stored; applied by the system that raises the event)
///   chance  { on, items?, chance, give, per_level? }
/// </summary>
public sealed class ResearchEffect
{
    public static readonly IReadOnlySet<string> Types = new HashSet<string> { "unlock", "modify", "trigger", "chance" };
    public static readonly IReadOnlySet<string> UnlockKinds = new HashSet<string> { "unit", "building", "spell", "feature", "recipes" };
    public static readonly IReadOnlySet<string> Ops = new HashSet<string> { "add", "mul", "set" };

    public string Type { get; init; } = "";
    // unlock
    public string? Kind { get; init; }
    public string? Id { get; init; }
    // modify
    public string? Stat { get; init; }
    public string Op { get; init; } = "add";
    public double Value { get; init; }
    /// <summary>Narrows a modifier to a subject group (<see cref="StatTargets"/>); null = applies everywhere the stat is read.</summary>
    public string? Target { get; init; }
    /// <summary>Upgrade nodes: the value applies once per level (add: value x level; mul: value ^ level).</summary>
    public bool PerLevel { get; init; }
    /// <summary>Display hint ("tick", "pct_round_down").</summary>
    public string? Unit { get; init; }
    // trigger / chance
    public string? On { get; init; }
    public string? Source { get; init; }
    public List<string> Items { get; init; } = new();
    public double Chance { get; init; }
    /// <summary>What a trigger or chance gives: { "souls": 1 } or a keyword ("extra_item").</summary>
    public JsonElement? Give { get; init; }

    /// <summary>The stat key the modifier is stored under: "stat" or "stat@target".</summary>
    public string StatKey => StatTargets.Key(Stat!, Target);

    public override string ToString() => Type switch
    {
        "unlock" => $"Unlocks {Kind} {Id}",
        "modify" => $"{Stat}{(Target is null ? "" : $" ({Target})")} {Op} {Value:0.####}{(PerLevel ? " per level" : "")}",
        "trigger" => $"On {On} ({Source}): {Give}",
        "chance" => $"{Chance:P1} on {On}: {Give}",
        _ => Type,
    };

    internal void Validate(ContentErrors errors, string f, bool upgrade)
    {
        if (!Types.Contains(Type)) { errors.Add($"{f}.type", $"unknown effect '{Type}' ({string.Join(", ", Types)})"); return; }
        switch (Type)
        {
            case "unlock":
                if (Kind is null || !UnlockKinds.Contains(Kind)) errors.Add($"{f}.kind", $"must be one of {string.Join(", ", UnlockKinds)}");
                if (string.IsNullOrWhiteSpace(Id)) errors.Add($"{f}.id", "is missing");
                break;
            case "modify":
                if (string.IsNullOrWhiteSpace(Stat)) errors.Add($"{f}.stat", "is missing");
                if (!Ops.Contains(Op)) errors.Add($"{f}.op", "must be add, mul or set");
                if (!double.IsFinite(Value)) errors.Add($"{f}.value", "must be a number");
                if (Op == "mul" && !(Value > 0)) errors.Add($"{f}.value", "a mul value must be above 0");
                if (Target is not null && !StatTargets.Known(Target)) errors.Add($"{f}.target", $"unknown target '{Target}' ({string.Join(", ", StatTargets.All)})");
                break;
            case "trigger":
            case "chance":
                if (string.IsNullOrWhiteSpace(On)) errors.Add($"{f}.on", "is missing");
                if (Give is null) errors.Add($"{f}.give", "is missing");
                if (Type == "chance" && !(Chance > 0 && Chance <= 1)) errors.Add($"{f}.chance", "must be above 0 and at most 1");
                break;
        }
        if (PerLevel && !upgrade) errors.Add($"{f}.per_level", "only upgrade nodes have levels");
    }
}

/// <summary>A node's place in the tree: x across, y = layer (roots at 0, drawn at the bottom).</summary>
public sealed class NodeLayout
{
    public double X { get; init; }
    public double Y { get; init; }
}

/// <summary>
/// Family base for research nodes (research revamp). Nodes cost research work only (no resources).
/// "unlock" nodes are researched once; "upgrade" nodes repeat up to <see cref="MaxLevel"/>, each level costing
/// work x time_multiplier x work_growth_per_level ^ level.
/// </summary>
public abstract class ResearchNodeDefinitionBase : Definition
{
    public string Tome { get; init; } = "";
    public string Description { get; init; } = "";
    public List<string> Requires { get; init; } = new();
    /// <summary>"unlock" (one-off) or "upgrade" (repeatable).</summary>
    public string Kind { get; init; } = "unlock";
    /// <summary>Research work needed for the first (or only) level.</summary>
    public double Work { get; init; } = 1;
    /// <summary>Upgrade nodes: highest level (before research raises caps). Unlock nodes: 1.</summary>
    public int MaxLevel { get; init; } = 1;
    /// <summary>Upgrade nodes: work multiplier per level already researched (placeholder 1.15).</summary>
    public double WorkGrowthPerLevel { get; init; } = 1;
    /// <summary>Extra work multiplier from the old data (Mage Hand 1: 2 = double work).</summary>
    public double TimeMultiplier { get; init; } = 1;
    public NodeLayout Layout { get; init; } = new();
    public string? IconLarge { get; init; }
    public List<ResearchEffect> Effects { get; init; } = new();

    public bool IsUpgrade => Kind == "upgrade";

    /// <summary>Work for the next level when <paramref name="levelsDone"/> are done.</summary>
    public double WorkFor(int levelsDone) => Work * TimeMultiplier * Math.Pow(WorkGrowthPerLevel, Math.Max(0, levelsDone));

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (string.IsNullOrWhiteSpace(Tome)) errors.Add("tome", "is missing");
        if (Kind is not ("unlock" or "upgrade")) errors.Add("kind", "must be unlock or upgrade");
        if (!(Work > 0)) errors.Add("work", "must be above 0");
        if (MaxLevel < 1) errors.Add("max_level", "must be 1 or more");
        if (Kind == "unlock" && MaxLevel != 1) errors.Add("max_level", "unlock nodes have max_level 1");
        if (!(WorkGrowthPerLevel >= 1)) errors.Add("work_growth_per_level", "must be 1 or more");
        if (!(TimeMultiplier > 0)) errors.Add("time_multiplier", "must be above 0");
        if (Requires.Contains(Id)) errors.Add("requires", "a node cannot require itself");
        if (Requires.Distinct().Count() != Requires.Count) errors.Add("requires", "lists a node twice");
        for (var i = 0; i < Effects.Count; i++) Effects[i].Validate(errors, $"effects[{i}]", IsUpgrade);
    }

    /// <summary>Requirements exist in the same tome; unit and building unlocks name real content.</summary>
    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        if (!db.Contains<TomeDefinitionBase>(Tome)) errors.Add("tome", $"no tome '{Tome}'");
        foreach (var r in Requires)
        {
            if (!db.TryGet<ResearchNodeDefinitionBase>(r, out var req)) errors.Add("requires", $"no research node '{r}'");
            else if (req.Tome != Tome) errors.Add("requires", $"'{r}' is in another tome ({req.Tome})");
        }
        for (var i = 0; i < Effects.Count; i++)
        {
            var e = Effects[i];
            if (e.Type != "unlock") continue;
            var ok = e.Kind switch
            {
                "unit" => (db.TryGet<UnitDefinitionBase>(e.Id!, out var u) && u.IsPlayer) || db.Contains<SummonDefinitionBase>(e.Id!),
                "building" => db.Contains<BuildingDefinitionBase>(e.Id!),
                "recipes" => db.All<TransmutationDefinitionBase>().Any(t => t.Group == e.Id),
                _ => true,   // spells and features have no content yet; their unlocks are recorded for later systems
            };
            if (!ok) errors.Add($"effects[{i}].id", e.Kind == "unit" ? $"no player unit or summon '{e.Id}'" : $"no {e.Kind} '{e.Id}'");
        }
    }
}

/// <summary>JSON type "research_node".</summary>
public sealed class ResearchNodeDefinition : ResearchNodeDefinitionBase { }

/// <summary>One bench level tier (from the old build.json): cost of level L = base x multiplier ^ (L - min_level).</summary>
public sealed class BenchLevelTier
{
    public int MinLevel { get; init; }
    public int MaxLevel { get; init; }
    public Dictionary<string, double> Base { get; init; } = new();
    public Dictionary<string, double> Multiplier { get; init; } = new();
}

/// <summary>
/// JSON type "research_bench" (exactly one, id "research_bench"). Placeholder formula:
/// work per tick = (base_work + work_per_necromancer x staffed) x level_work_growth ^ (bench level - 1) x research efficiency.
/// Bench level costs come from tiers (rounded to <see cref="RoundSigFigs"/> significant figures, halves up).
/// </summary>
public sealed class ResearchBenchDefinition : Definition
{
    public int BaseBenches { get; init; } = 1;
    /// <summary>Necromancers one bench holds before capacity effects.</summary>
    public long BaseSlotsPerBench { get; init; } = 2;
    public double BaseWork { get; init; } = 1;
    public double WorkPerNecromancer { get; init; } = 0.5;
    public double LevelWorkGrowth { get; init; } = 1.1;
    public int RoundSigFigs { get; init; } = 2;
    public List<BenchLevelTier> LevelTiers { get; init; } = new();

    /// <summary>Highest bench level in the tiers (before research raises caps).</summary>
    public int MaxLevel => LevelTiers.Count == 0 ? 1 : LevelTiers.Max(t => t.MaxLevel);

    /// <summary>
    /// Cost of reaching <paramref name="level"/> (2 and up). Past the last tier, the last tier keeps growing.
    /// </summary>
    public IReadOnlyList<ItemAmount> CostOfLevel(int level)
    {
        if (LevelTiers.Count == 0 || level < 2) return Array.Empty<ItemAmount>();
        var t = LevelTiers.FirstOrDefault(x => level >= x.MinLevel && level <= x.MaxLevel) ?? LevelTiers.OrderBy(x => x.MaxLevel).Last();
        return t.Base.Select(kv =>
        {
            var v = kv.Value * Math.Pow(t.Multiplier.GetValueOrDefault(kv.Key, 1), level - t.MinLevel);
            return new ItemAmount { Id = kv.Key, Count = RoundSig(v, RoundSigFigs) };
        }).ToList();
    }

    /// <summary>Round to <paramref name="figs"/> significant figures, halves up (decimal-correct: 114.999... from 50 x 2.3 counts as 115).</summary>
    public static long RoundSig(double v, int figs)
    {
        if (!(v > 0)) return 0;
        if (v >= long.MaxValue / 10.0) return long.MaxValue;
        var clean = double.Parse(v.ToString("G12", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        var digits = (int)Math.Floor(Math.Log10(clean)) + 1;
        var scale = Math.Pow(10, digits - figs);
        return (long)(Math.Floor(clean / scale + 0.5 + 1e-9) * scale);
    }

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Id != "research_bench") errors.Add("id", "must be research_bench");
        if (BaseBenches < 1) errors.Add("base_benches", "must be 1 or more");
        if (BaseSlotsPerBench < 0) errors.Add("base_slots_per_bench", "must be 0 or more");
        if (!(BaseWork >= 0) || !(WorkPerNecromancer >= 0)) errors.Add("base_work", "work must be 0 or more");
        if (!(LevelWorkGrowth >= 1)) errors.Add("level_work_growth", "must be 1 or more");
        if (RoundSigFigs < 1) errors.Add("round_sig_figs", "must be 1 or more");
        for (var i = 0; i < LevelTiers.Count; i++)
        {
            var t = LevelTiers[i];
            if (t.MinLevel < 1 || t.MaxLevel < t.MinLevel) errors.Add($"level_tiers[{i}]", "needs 1 <= min_level <= max_level");
            if (i > 0 && t.MinLevel != LevelTiers[i - 1].MaxLevel + 1) errors.Add($"level_tiers[{i}].min_level", "tiers must follow on from each other");
            if (t.Base.Count == 0) errors.Add($"level_tiers[{i}].base", "needs at least one cost");
            if (t.Base.Values.Any(v => !(v > 0)) || t.Multiplier.Values.Any(v => !(v >= 1))) errors.Add($"level_tiers[{i}]", "bases must be above 0 and multipliers 1 or more");
        }
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        foreach (var id in LevelTiers.SelectMany(t => t.Base.Keys).Distinct().Where(id => !StockIds.Exists(db, id)))
            errors.Add("level_tiers", $"no resource or item '{id}'");
    }
}
