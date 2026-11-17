namespace LittleLich.Core.Lich;

/// <summary>
/// Necromancers working on one thing. Keyed by owner + activity.
/// Skill example: OwnerId "mining", ActivityId "iron_ore".
/// Workshop example: OwnerId "mana_well", ActivityId "main".
/// </summary>
public sealed record Assignment(string OwnerId, string ActivityId, long Count);
