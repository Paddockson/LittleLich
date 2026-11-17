using System.Collections.Generic;
using System.Linq;
using LittleLich.Content.Definitions;

namespace LittleLich.World;

/// <summary>An action as a battle uses it (from a <see cref="BattleActionDefinitionBase"/>, possibly changed by gear).</summary>
public sealed record BattleUnitAction(string Id, string Name, double Cooldown, string Effect, double Value, string Target, string Range)
{
    public bool IsMelee => Range == "melee";

    public static BattleUnitAction From(BattleActionDefinitionBase a, double extraDamage = 0) =>
        new(a.Id, a.Name, a.CooldownSeconds, a.Effect, a.Effect == "damage" ? a.Value + extraDamage : a.Value, a.Target, a.Range);
}

/// <summary>
/// A unit as a battle uses it: id, name, hp per unit and actions. Built from a unit definition (enemies) or from a
/// unit type with its gear (the player's army, <see cref="Crypt.Units.UnitBuilder.BattleUnit"/>).
/// </summary>
public sealed record BattleUnit(string Id, string Name, double Hp, IReadOnlyList<BattleUnitAction> Actions)
{
    public static BattleUnit From(UnitDefinitionBase u) =>
        new(u.Id, u.Name, u.Hp, u.ResolvedActions.Select(a => BattleUnitAction.From(a)).ToList());
}
