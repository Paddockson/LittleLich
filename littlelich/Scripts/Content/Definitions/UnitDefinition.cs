using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace LittleLich.Content.Definitions;

/// <summary>
/// Family base for reusable battle actions (JSON type "battle_action", Data/units/actions.json). Units list them by id.
/// Fires every <see cref="CooldownSeconds"/> on the battle clock (battles run on their own clock, in seconds).
///   effect: damage (value per living unit), reduce_next_incoming (the next attack against the group does value x 100% less),
///           heal (value HP per living unit to the target)
///   target: front (the enemy's front-most living section), back (its back-most), lowest (the enemy group with the lowest
///           HP fraction), self, ally_lowest (own group with the lowest HP fraction)
///   range:  melee (only from the front-most living section of its side) or ranged (any section)
/// </summary>
public abstract class BattleActionDefinitionBase : Definition
{
    public static readonly IReadOnlySet<string> Effects = new HashSet<string> { "damage", "reduce_next_incoming", "heal" };
    public static readonly IReadOnlySet<string> Targets = new HashSet<string> { "front", "back", "lowest", "self", "ally_lowest" };

    public double CooldownSeconds { get; init; } = 1;
    public string Effect { get; init; } = "damage";
    public double Value { get; init; }
    public string Target { get; init; } = "front";
    public string Range { get; init; } = "melee";

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (!(CooldownSeconds >= 0.1)) errors.Add("cooldown_seconds", "must be at least 0.1 seconds");
        if (!Effects.Contains(Effect)) errors.Add("effect", $"'{Effect}' is not one of {string.Join(", ", Effects)}");
        if (!Targets.Contains(Target)) errors.Add("target", $"'{Target}' is not one of {string.Join(", ", Targets)}");
        if (Range is not ("melee" or "ranged")) errors.Add("range", "must be melee or ranged");
        if (!(Value >= 0)) errors.Add("value", "must be 0 or more");
        if (Effect == "reduce_next_incoming" && Value > 1) errors.Add("value", "reduce_next_incoming is a fraction (0.1 = 10% less damage)");
        if (Effect == "reduce_next_incoming" && Target != "self") errors.Add("target", "reduce_next_incoming must target self");
    }
}

/// <summary>JSON type "battle_action".</summary>
public sealed class BattleActionDefinition : BattleActionDefinitionBase { }

/// <summary>How a player unit base is unlocked: { "research": "animate_bones" } (the node that unlocks it).</summary>
public sealed class UnitUnlock
{
    public string? Research { get; init; }
}

/// <summary>
/// Family base for units. Subclasses are picked by the JSON type: undead, wild, boss, structure.
/// Player bases (side "player", Data/units/bases.json) appear in the Unit Builder; enemies (side "enemy",
/// Data/units/enemies.json) are listed as defenders by areas (Data/world.json).
/// Adding a unit is a JSON entry only: the Unit Builder lists every player base and battles use any unit.
/// </summary>
public abstract class UnitDefinitionBase : Definition
{
    public const int MaxActions = 3;

    /// <summary>"player" (raised by the lich) or "enemy".</summary>
    public string Side { get; init; } = "enemy";
    /// <summary>Hit points per unit.</summary>
    public double Hp { get; init; } = 1;
    /// <summary>Swarm, tank, ranged ... (shown in the Unit Builder).</summary>
    public string Role { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Families research and spells target ("skeleton", "wisp").</summary>
    public List<string> Tags { get; init; } = new();
    /// <summary>Battle action ids (Data/units/actions.json), at most three.</summary>
    public List<string> Actions { get; init; } = new();
    /// <summary>Player bases: the research node that unlocks it (none = available from the start).</summary>
    public UnitUnlock? Unlock { get; init; }
    /// <summary>Player bases: slots for a weapon / armour. Empty = this body cannot carry one. v0.1 uses one of each.</summary>
    public List<string> WeaponSlots { get; init; } = new();
    public List<string> ArmourSlots { get; init; } = new();
    /// <summary>Player bases: paid per summon before gear (resources and items; gear comes from the unit type).</summary>
    [JsonConverter(typeof(ItemAmountMapConverter))]
    public List<ItemAmount> SummonCost { get; init; } = new();
    /// <summary>Player bases: Summoning Circle power needed per summon.</summary>
    public double SummonRequired { get; init; } = 1;
    /// <summary>Player bases: circle level needed before the circle can summon it (1 = from the start).</summary>
    public int SummonLevel { get; init; } = 1;

    /// <summary>The actions resolved from <see cref="Actions"/> (filled in when references are validated).</summary>
    [JsonIgnore]
    public IReadOnlyList<BattleActionDefinitionBase> ResolvedActions { get; private set; } = new List<BattleActionDefinitionBase>();

    public bool IsPlayer => Side == "player";

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Side is not ("player" or "enemy")) errors.Add("side", "must be player or enemy");
        if (!(Hp > 0)) errors.Add("hp", "must be above 0");
        if (Actions.Count > MaxActions) errors.Add("actions", $"has {Actions.Count} actions; at most {MaxActions}");
        if (Actions.GroupBy(a => a).Any(g => g.Count() > 1)) errors.Add("actions", "lists the same action twice");
        if (SummonCost.Any(c => c.Count < 1)) errors.Add("summon_cost", "counts must be 1 or more");
        if (!(SummonRequired > 0)) errors.Add("summon_required", "must be above 0");
        if (SummonLevel < 1) errors.Add("summon_level", "must be 1 or more");
        if (WeaponSlots.Count > 1 || ArmourSlots.Count > 1) errors.Add("weapon_slots", "v0.1 supports at most one weapon slot and one armour slot");
        if (!IsPlayer && (Unlock is not null || SummonCost.Count > 0 || WeaponSlots.Count > 0 || ArmourSlots.Count > 0))
            errors.Add("side", "unlock, summon_cost and gear slots are for player units only");
        if (Unlock is { Research: null or "" }) errors.Add("unlock.research", "is missing");
    }

    public override void ValidateReferences(ContentDatabase db, ContentErrors errors)
    {
        var resolved = new List<BattleActionDefinitionBase>();
        foreach (var id in Actions)
        {
            if (db.TryGet<BattleActionDefinitionBase>(id, out var a)) resolved.Add(a);
            else errors.Add("actions", $"unknown action id '{id}'");
        }
        ResolvedActions = resolved;
        foreach (var c in SummonCost)
            if (!StockIds.Exists(db, c.Id)) errors.Add("summon_cost", $"no resource or item '{c.Id}'");
        if (Unlock?.Research is { Length: > 0 } r && !db.Contains<ResearchNodeDefinitionBase>(r))
            errors.Add("unlock.research", $"unknown research node '{r}'");
    }
}

/// <summary>JSON type "undead": the lich's bodies and rival undead.</summary>
public sealed class UndeadUnitDefinition : UnitDefinitionBase { }

/// <summary>JSON type "wild": the wild people of the Woods and other living foes.</summary>
public sealed class WildUnitDefinition : UnitDefinitionBase { }

/// <summary>JSON type "boss": a single strong enemy (basic boss; boss mechanics are out of scope).</summary>
public sealed class BossUnitDefinition : UnitDefinitionBase { }

/// <summary>JSON type "structure": walls and gates. Enemy only, no actions; it just has to be broken.</summary>
public sealed class StructureUnitDefinition : UnitDefinitionBase
{
    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (IsPlayer) errors.Add("side", "structures are enemy only");
        if (Actions.Count > 0) errors.Add("actions", "structures have no actions");
    }
}
