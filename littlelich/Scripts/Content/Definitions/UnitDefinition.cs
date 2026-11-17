using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Content.Definitions;

/// <summary>
/// One battle action (data). Fires every <see cref="Cooldown"/> seconds on the battle clock.
///   effect: attack (value damage per living unit), brace (the next attack against the group does value x 100% less),
///           heal (value HP per living unit to the target group)
///   target: front (the enemy's front-most living section), back (the enemy's back-most), lowest (the enemy group with
///           the lowest HP fraction), self, ally_lowest (own group with the lowest HP fraction)
///   range:  melee (only usable from the front-most living section of its side) or ranged (any section)
/// </summary>
public sealed class UnitAction
{
    public static readonly IReadOnlySet<string> Effects = new HashSet<string> { "attack", "brace", "heal" };
    public static readonly IReadOnlySet<string> Targets = new HashSet<string> { "front", "back", "lowest", "self", "ally_lowest" };

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double Cooldown { get; init; } = 1;
    public string Effect { get; init; } = "attack";
    public double Value { get; init; }
    public string Target { get; init; } = "front";
    public string Range { get; init; } = "melee";

    public bool IsMelee => Range == "melee";
}

/// <summary>
/// Family base for units. Placeholder schema pulled forward from phase 10 so phase 8 battles have something to fight
/// with: hit points per unit and up to three actions. Phase 10 adds roles, damage types, costs and gear.
/// </summary>
public abstract class UnitDefinitionBase : Definition
{
    public const int MaxActions = 3;

    /// <summary>"player" (raised by the lich) or "enemy".</summary>
    public string Side { get; init; } = "player";
    public double Hp { get; init; } = 1;
    public List<UnitAction> Actions { get; init; } = new();

    public override void Validate(ContentErrors errors)
    {
        base.Validate(errors);
        if (Side is not ("player" or "enemy")) errors.Add("side", "must be player or enemy");
        if (!(Hp > 0)) errors.Add("hp", "must be above 0");
        if (Actions.Count > MaxActions) errors.Add("actions", $"at most {MaxActions} actions per unit");
        if (Actions.GroupBy(a => a.Id).Any(g => g.Count() > 1)) errors.Add("actions", "action ids must be unique");
        for (var i = 0; i < Actions.Count; i++)
        {
            var a = Actions[i];
            var f = $"actions[{i}]";
            if (string.IsNullOrWhiteSpace(a.Id)) errors.Add($"{f}.id", "is missing");
            if (!(a.Cooldown >= 0.1)) errors.Add($"{f}.cooldown", "must be at least 0.1 seconds");
            if (!UnitAction.Effects.Contains(a.Effect)) errors.Add($"{f}.effect", $"must be one of {string.Join(", ", UnitAction.Effects)}");
            if (!UnitAction.Targets.Contains(a.Target)) errors.Add($"{f}.target", $"must be one of {string.Join(", ", UnitAction.Targets)}");
            if (a.Range is not ("melee" or "ranged")) errors.Add($"{f}.range", "must be melee or ranged");
            if (!(a.Value >= 0)) errors.Add($"{f}.value", "must be 0 or more");
            if (a.Effect == "brace" && a.Value > 1) errors.Add($"{f}.value", "brace is a fraction (0.1 = 10% less damage)");
        }
    }
}

/// <summary>JSON type "unit".</summary>
public sealed class UnitDefinition : UnitDefinitionBase { }
