using System.Collections.Generic;
using System.Linq;
using LittleLich.Core;
using LittleLich.Core.Lich;

namespace LittleLich.Skills;

// One class per skill (design rule). Skill-specific behaviour (upgrades, special rules) goes in these classes later.

public sealed class MiningManager : GatheringSkill
{
    public MiningManager(SkillContext c) : base(SkillIds.Mining, "Mining", c) { }
}

public sealed class FishingManager : GatheringSkill
{
    public FishingManager(SkillContext c) : base(SkillIds.Fishing, "Fishing", c) { }
}

public sealed class HuntingManager : GatheringSkill
{
    public HuntingManager(SkillContext c) : base(SkillIds.Hunting, "Hunting", c) { }
}

public sealed class HerbalismManager : GatheringSkill
{
    public HerbalismManager(SkillContext c) : base(SkillIds.Herbalism, "Herbalism", c) { }
}

/// <summary>Weapons and armour (Transmuter stays resource conversion only in v0.1).</summary>
public sealed class SmithingManager : CraftingSkill
{
    public SmithingManager(SkillContext c) : base(SkillIds.Smithing, "Smithing", c) { }
}

/// <summary>Potions and bombs.</summary>
public sealed class AlchemyManager : CraftingSkill
{
    public AlchemyManager(SkillContext c) : base(SkillIds.Alchemy, "Alchemy", c) { }
}

/// <summary>Runes that attach to weapons and armour (attaching comes later).</summary>
public sealed class EnchantingManager : CraftingSkill
{
    public EnchantingManager(SkillContext c) : base(SkillIds.Enchanting, "Enchanting", c) { }
}

/// <summary>
/// Toughens the lich: +<see cref="MaxHealthPerLevel"/> max health per level above 1, as a modifier with source
/// "skill:health" (so the Lich tab can show what comes from the skill and what from gear).
/// </summary>
public sealed class HealthSkill : LevelOnlySkill
{
    public const string Source = "skill:health";
    /// <summary>Placeholder.</summary>
    public const double MaxHealthPerLevel = 10;

    public HealthSkill(SkillContext c) : base(SkillIds.Health, "Health", c) { }

    protected override void OnLevelChanged(int oldLevel, int newLevel)
    {
        var stats = Context.Lich.Stats;
        var bonus = (Num)((newLevel - 1) * MaxHealthPerLevel);
        var old = stats.Modifiers.Where(m => m.Source == Source).ToList();
        if (old.Count == 1 && old[0].StatId == LichStats.MaxHealth && old[0].Op == ModifierOp.Add && old[0].Value == bonus) return;
        // Add the new value before removing the old so max health never dips (which would clamp current health).
        if (bonus > Num.Zero) stats.Add(new Modifier(LichStats.MaxHealth, ModifierOp.Add, bonus, Source));
        foreach (var m in old) stats.RemoveWhere(x => ReferenceEquals(x, m));
    }
}

/// <summary>Raises and strengthens the undead. Level only in v0.1 (unit types and army size come later).</summary>
public sealed class NecromancySkill : LevelOnlySkill
{
    public NecromancySkill(SkillContext c) : base(SkillIds.Necromancy, "Necromancy", c) { }
}

/// <summary>Spells. Level only in v0.1 (Max Insight growth comes with the spell book).</summary>
public sealed class MagicSkill : LevelOnlySkill
{
    public MagicSkill(SkillContext c) : base(SkillIds.Magic, "Magic", c) { }
}

public static class SkillFactory
{
    /// <summary>
    /// Creates all ten skills in sidebar order and sets the base (1) of each automatable skill's speed stat.
    /// The caller registers them as saveables and registers the automatable ones with the tick scheduler.
    /// </summary>
    public static IReadOnlyList<SkillBase> CreateAll(SkillContext c)
    {
        foreach (var id in SkillIds.Gathering.Concat(SkillIds.Crafting))
            c.Lich.Stats.SetBase(SkillIds.SpeedStat(id), 1);

        return new SkillBase[]
        {
            new HealthSkill(c), new NecromancySkill(c), new MagicSkill(c),
            new MiningManager(c), new FishingManager(c), new HuntingManager(c), new HerbalismManager(c),
            new SmithingManager(c), new AlchemyManager(c), new EnchantingManager(c),
        };
    }
}
