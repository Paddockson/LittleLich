namespace LittleLich.Core.Lich;

public enum ModifierOp
{
    /// <summary>Added to the base value.</summary>
    Add,
    /// <summary>Multiplies (base + adds). A value of 1.1 means +10%.</summary>
    Mul,
    /// <summary>Replaces the base (the highest Set wins); adds and muls still apply on top.</summary>
    Set,
}

/// <summary>
/// One change to one stat, from a buff, debuff, gear (phase 6), research (phase 7), potion or spell.
/// Stat value = (base, or the highest Set, + sum of Add) x product of Mul.
/// A modifier with <see cref="ExpiresAtTick"/> is active while the lich's tick is below it, then removed.
/// </summary>
/// <param name="Source">Who added it (e.g. "gear:hood", "potion:iron_skin", "debug"). Used to remove modifiers in bulk.</param>
public sealed record Modifier(string StatId, ModifierOp Op, Num Value, string Source, long? ExpiresAtTick = null)
{
    public bool IsTimed => ExpiresAtTick.HasValue;
}

/// <summary>Stat ids the lich knows about. Any other id is allowed and has a base of 0.</summary>
public static class LichStats
{
    public const string MaxHealth = "max_health";
    /// <summary>Spell limit. The design doc also calls it Max Focus; "Insight" was chosen in phase 2.</summary>
    public const string MaxInsight = "max_insight";
    public const string MaxNecromancers = "max_necromancers";

    // Placeholder base values (v0.1). The Health skill (phase 4), gear (phase 6),
    // research (phase 7) and the Summoning Circle (phase 5) raise these through bases and modifiers.
    public const double BaseMaxHealth = 100;
    public const double BaseMaxInsight = 10;
    /// <summary>The old game's necromancer cap (research revamp: data scale). Research raises it.</summary>
    public const double BaseMaxNecromancers = 5;

    /// <summary>Research data says "insight"; the lich stat is <see cref="MaxInsight"/>.</summary>
    public static string Alias(string stat) => stat == "insight" ? MaxInsight : stat;
}
