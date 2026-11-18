using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Content.Definitions;

/// <summary>Workshop building ids. Each has exactly one class and one definition in Data/buildings.json.</summary>
public static class BuildingIds
{
    public const string ManaWell = "mana_well";
    public const string Infuser = "infuser";
    public const string SoulWell = "soul_well";
    public const string Transmuter = "transmuter";
    public const string SummoningCircle = "summoning_circle";
    public const string UndeadMarket = "undead_market";
    public const string MagicalStorage = "magical_storage";

    /// <summary>Flow order (also the Workshop list order). Magical Storage does not tick.</summary>
    public static readonly IReadOnlyList<string> All = new[] { ManaWell, Infuser, SoulWell, Transmuter, SummoningCircle, UndeadMarket, MagicalStorage };
}

/// <summary>
/// Family base for Workshop buildings. Shared tuning (placeholder numbers in JSON):
///   work per tick = (base_work + staffed necromancers x work_per_necromancer) x (1 + level_bonus x (level - 1))
///   necromancer slots = base_slots + slots_per_level x (level - 1)
///   level, max level and upgrade cost: the building's building_upgrade in Data/upgrades.json
/// What one unit of "work" does is up to the building type.
/// </summary>
public abstract class BuildingDefinitionBase : Definition
{
    public long BaseSlots { get; init; }
    public long SlotsPerLevel { get; init; }
    public double BaseWork { get; init; }
    public double WorkPerNecromancer { get; init; }
    public double LevelBonus { get; init; }

    /// <summary>
    /// Optional level table (adjustment 02, Mana Well): one row per level from 1, in order. When present it replaces
    /// the formulas: necromancer places = max_necromancers, output multiplier = 1 + output_bonus, and the cost of
    /// reaching a level is that row's cost. The highest row is the last level, whatever research adds.
    /// </summary>
    public List<BuildingLevelRow> Levels { get; init; } = new();

    public bool HasLevelTable => Levels.Count > 0;

    /// <summary>The row for a level (clamped into the table), or null without a table.</summary>
    public BuildingLevelRow? Row(int level) => HasLevelTable ? Levels[System.Math.Clamp(level, 1, Levels.Count) - 1] : null;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (!BuildingIds.All.Contains(Id)) errors.Add("id", $"'{Id}' is not a Workshop building ({string.Join(", ", BuildingIds.All)})");
        if (BaseSlots < 0 || SlotsPerLevel < 0) errors.Add("base_slots", "slots must be 0 or more");
        if (!(BaseWork >= 0) || !(WorkPerNecromancer >= 0) || !(LevelBonus >= 0)) errors.Add("base_work", "work values must be 0 or more");
        for (var i = 0; i < Levels.Count; i++)
        {
            var r = Levels[i];
            var f = $"levels[{i}]";
            if (r.Level != i + 1) errors.Add($"{f}.level", $"must be {i + 1} (rows start at level 1, one per level, in order)");
            if (r.MaxNecromancers < 0) errors.Add($"{f}.max_necromancers", "must be 0 or more");
            if (i > 0 && r.MaxNecromancers < Levels[i - 1].MaxNecromancers) errors.Add($"{f}.max_necromancers", "must not go down from the level before");
            if (!(r.OutputBonus >= 0)) errors.Add($"{f}.output_bonus", "must be 0 or more");
            if (i == 0 && r.Cost.Count > 0) errors.Add($"{f}.cost", "level 1 is free (leave cost out)");
            if (i > 0 && r.Cost.Count == 0) errors.Add($"{f}.cost", "needs at least one cost");
            foreach (var (id, n) in r.Cost) if (n < 1) errors.Add($"{f}.cost.{id}", "must be 1 or more");
        }
    }

    /// <summary>Every building needs exactly one building_upgrade (its levels and costs).</summary>
    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        if (!db.All<BuildingUpgradeDefinition>().Any(u => u.Building == Id))
            errors.Add(null, "has no building_upgrade in upgrades.json");
        for (var i = 0; i < Levels.Count; i++)
            foreach (var id in Levels[i].Cost.Keys.Where(id => !StockIds.Exists(db, id)))
                errors.Add($"levels[{i}].cost.{id}", $"no resource or item with id '{id}'");
    }
}

/// <summary>One row of a building's level table: { "level": 5, "max_necromancers": 9, "cost": { ... }, "output_bonus": 0.0004 }.</summary>
public sealed class BuildingLevelRow
{
    public int Level { get; init; }
    public long MaxNecromancers { get; init; }
    /// <summary>Paid to reach this level from the one before (empty for level 1).</summary>
    public Dictionary<string, long> Cost { get; init; } = new();
    /// <summary>Total output bonus at this level: 0.0004 = +0.04%.</summary>
    public double OutputBonus { get; init; }
}

/// <summary>
/// JSON type "mana_well" (adjustment 02): one unit of work makes one unit of <see cref="Output"/>; a click makes
/// <see cref="ClickPower"/>. Output per necromancer per tick is the shared work_per_necromancer.
/// </summary>
public sealed class ManaWellDefinition : BuildingDefinitionBase
{
    public string Output { get; init; } = "mana";

    /// <summary>Mana one click on the well channels.</summary>
    public double ClickPower { get; init; } = 1;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (!(ClickPower >= 0)) errors.Add("click_power", "must be 0 or more");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        base.ValidateReferences(db, errors);
        if (!db.Contains<ResourceDefinitionBase>(Output)) errors.Add("output", $"no resource '{Output}'");
    }
}

/// <summary>JSON type "converter" (Infuser, Soul Well): <see cref="WorkPerOp"/> work turns the inputs into the output.</summary>
public sealed class ConverterDefinition : BuildingDefinitionBase
{
    [JsonConverter(typeof(ItemAmountMapConverter))]
    public List<ItemAmount> Inputs { get; init; } = new();
    [JsonConverter(typeof(SingleItemAmountConverter))]
    public ItemAmount Output { get; init; } = new();
    public double WorkPerOp { get; init; } = 1;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Inputs.Count == 0) errors.Add("inputs", "needs at least one input");
        if (!(WorkPerOp > 0)) errors.Add("work_per_op", "must be above 0");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        base.ValidateReferences(db, errors);
        foreach (var i in Inputs.Append(Output))
            if (!db.Contains<ResourceDefinitionBase>(i.Id)) errors.Add("inputs", $"no resource '{i.Id}'");
    }
}

/// <summary>
/// JSON type "infuser": <see cref="WorkPerOp"/> work turns <see cref="Input"/> (mana) into one unit of the element the
/// player picks (<see cref="Elements"/>: fire, water, air, earth).
/// </summary>
public sealed class InfuserDefinition : BuildingDefinitionBase
{
    [JsonConverter(typeof(SingleItemAmountConverter))]
    public ItemAmount Input { get; init; } = new();
    public List<string> Elements { get; init; } = new();
    public double WorkPerOp { get; init; } = 1;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Elements.Count == 0) errors.Add("elements", "needs at least one element");
        if (!(WorkPerOp > 0)) errors.Add("work_per_op", "must be above 0");
        if (Input.Count < 1) errors.Add("input.count", "must be 1 or more");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        base.ValidateReferences(db, errors);
        foreach (var id in Elements.Append(Input.Id))
            if (!db.Contains<ResourceDefinitionBase>(id)) errors.Add("elements", $"no resource '{id}'");
    }
}

/// <summary>JSON type "transmuter": runs one chosen <see cref="TransmutationDefinition"/> at a time.</summary>
public sealed class TransmuterDefinition : BuildingDefinitionBase { }

/// <summary>
/// JSON type "summoning_circle" (adjustment 01): summon slots by level and click power. Power per necromancer per
/// tick is the shared work_per_necromancer. Each level also raises max necromancers.
/// </summary>
public sealed class SummoningCircleDefinition : BuildingDefinitionBase
{
    public double MaxNecromancersPerLevel { get; init; }

    /// <summary>Power one click on the circle adds to the active slot.</summary>
    public double ClickPower { get; init; } = 1;

    /// <summary>Circle level (as text) -> summon slots from that level on: { "1": 2, "3": 3, "5": 4 }.</summary>
    public Dictionary<string, int> SlotsByLevel { get; init; } = new() { ["1"] = 1 };

    /// <summary>Summon slots at a circle level (the entry with the highest level at or below it).</summary>
    public int SlotsAt(int level) =>
        SlotsByLevel.Select(kv => (Level: int.TryParse(kv.Key, out var l) ? l : int.MaxValue, Slots: kv.Value))
            .Where(x => x.Level <= level).OrderBy(x => x.Level).Select(x => x.Slots).LastOrDefault();

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (!(ClickPower >= 0)) errors.Add("click_power", "must be 0 or more");
        if (!SlotsByLevel.ContainsKey("1")) errors.Add("slots_by_level", "needs an entry for level 1");
        foreach (var (k, v) in SlotsByLevel)
        {
            if (!int.TryParse(k, out var l) || l < 1) errors.Add($"slots_by_level.{k}", "keys must be levels (1 or more)");
            if (v < 0) errors.Add($"slots_by_level.{k}", "must be 0 or more");
        }
    }
}

/// <summary>JSON type "undead_market": one unit of work sells one unit of stock at its sell_price.</summary>
public sealed class UndeadMarketDefinition : BuildingDefinitionBase { }

/// <summary>
/// JSON type "magical_storage": no work. Each level adds <see cref="CapBonusPerLevel"/> x base cap to every resource
/// and <see cref="InventorySlotsPerLevel"/> inventory slots.
/// </summary>
public sealed class MagicalStorageDefinition : BuildingDefinitionBase
{
    public double CapBonusPerLevel { get; init; }

    /// <summary>Inventory slots at level 1 (phase 6).</summary>
    public long BaseInventorySlots { get; init; } = 40;

    /// <summary>Extra inventory slots per level above 1.</summary>
    public long InventorySlotsPerLevel { get; init; }
}

/// <summary>Family base for Transmuter recipes (resources and materials only in v0.1).</summary>
public abstract class TransmutationDefinitionBase : Definition
{
    [JsonConverter(typeof(ItemAmountMapConverter))]
    public List<ItemAmount> Inputs { get; init; } = new();
    [JsonConverter(typeof(SingleItemAmountConverter))]
    public ItemAmount Output { get; init; } = new();
    /// <summary>Optional group a research unlock opens all at once ("summoning_gear").</summary>
    public string? Group { get; init; }
    /// <summary>Work needed per conversion.</summary>
    public double Work { get; init; } = 1;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Inputs.Count == 0) errors.Add("inputs", "needs at least one input");
        if (Inputs.Any(i => i.Count < 1) || Output.Count < 1) errors.Add("inputs", "counts must be 1 or more");
        if (!(Work > 0)) errors.Add("work", "must be above 0");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        // v0.1 decision: the Transmuter converts resources and materials only (gear, potions, bombs and runes come
        // from Smithing, Alchemy and Enchanting).
        foreach (var i in Inputs.Append(Output))
            if (!db.Contains<ResourceDefinitionBase>(i.Id) && !db.Contains<MaterialDefinition>(i.Id))
                errors.Add("inputs", $"'{i.Id}' is not a resource or material (the Transmuter converts resources and materials only in v0.1)");
    }
}

/// <summary>JSON type "transmutation".</summary>
public sealed class TransmutationDefinition : TransmutationDefinitionBase { }

/// <summary>
/// Family base for Summoning Circle summons that are not unit bodies: necromancers (they join the free pool).
/// Unit bodies (Data/units/bases.json) carry the same summon_cost / summon_required / summon_level fields.
/// </summary>
public abstract class SummonDefinitionBase : Definition
{
    public static readonly IReadOnlySet<string> Produces = new HashSet<string> { "necromancer" };

    public string Description { get; init; } = "";
    /// <summary>Paid per summon (resources and items).</summary>
    [JsonConverter(typeof(ItemAmountMapConverter))]
    public List<ItemAmount> SummonCost { get; init; } = new();
    /// <summary>What it summons: "necromancer".</summary>
    public string Summons { get; init; } = "necromancer";
    /// <summary>How many per summon.</summary>
    public long Count { get; init; } = 1;
    /// <summary>Circle power needed per summon.</summary>
    public double SummonRequired { get; init; } = 1;
    /// <summary>Circle level needed before it can be chosen (1 = from the start).</summary>
    public int SummonLevel { get; init; } = 1;

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (!Produces.Contains(Summons)) errors.Add("summons", $"'{Summons}' cannot be summoned this way (unit bodies go in Data/units/bases.json)");
        if (Count < 1) errors.Add("count", "must be 1 or more");
        if (!(SummonRequired > 0)) errors.Add("summon_required", "must be above 0");
        if (SummonLevel < 1) errors.Add("summon_level", "must be 1 or more");
        if (SummonCost.Any(c => c.Count < 1)) errors.Add("summon_cost", "counts must be 1 or more");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        foreach (var c in SummonCost)
            if (!StockIds.Exists(db, c.Id)) errors.Add("summon_cost", $"no resource or item '{c.Id}'");
    }
}

/// <summary>JSON type "summon".</summary>
public sealed class SummonDefinition : SummonDefinitionBase { }
