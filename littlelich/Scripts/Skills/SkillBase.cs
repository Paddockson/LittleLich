using System;
using LittleLich.Content;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Ticks;
using LittleLich.Crypt;

namespace LittleLich.Skills;

/// <summary>What a skill needs from the rest of the game. Plain objects, so skills are testable without Godot.</summary>
public sealed record SkillContext(LichState Lich, Stockpile Stock, ContentDatabase Content, Func<AwayRecap?> Recap)
{
    /// <summary>Fixed-point scale for production maths (see <see cref="FixedPoint"/>).</summary>
    public const long Micro = FixedPoint.Micro;

    public static long ToMicro(double value) => FixedPoint.ToMicro(value);

    public static long Clamp(Int128 v) => FixedPoint.Clamp(v);
}

/// <summary>
/// A skill: level 1 to 999 from total XP (<see cref="XpCurve"/>), with events for the UI.
/// Saves its own section "skill_&lt;id&gt;". The level is derived from XP on load (the saved level is for reading only).
/// </summary>
public abstract class SkillBase : ITickable, ISaveable
{
    protected SkillBase(string id, string name, SkillContext context)
    {
        Id = id;
        Name = name;
        Context = context;
    }

    public string Id { get; }
    public string Name { get; }
    public int Level { get; private set; } = 1;
    public Num Xp { get; private set; } = Num.Zero;

    protected SkillContext Context { get; }

    public string SaveKey => "skill_" + Id;

    /// <summary>Total XP at the start of the current level and at the next one.</summary>
    public Num LevelStartXp => XpCurve.XpForLevel(Level);
    public Num NextLevelXp => XpCurve.XpForLevel(Math.Min(Level + 1, XpCurve.MaxLevel));
    public bool IsMaxLevel => Level >= XpCurve.MaxLevel;

    /// <summary>0..1 progress through the current level.</summary>
    public double LevelProgress =>
        IsMaxLevel ? 1 : Math.Clamp(((Xp - LevelStartXp) / (NextLevelXp - LevelStartXp)).ToDouble(), 0, 1);

    public event Action<SkillBase>? XpChanged;
    /// <summary>(skill, old level, new level).</summary>
    public event Action<SkillBase, int, int>? LeveledUp;

    public abstract void Tick(long ticks);

    /// <summary>Adds XP and handles level ups (possibly several at once).</summary>
    protected void AddXp(Num amount)
    {
        if (!(amount > Num.Zero)) return;
        Xp += amount;
        var old = Level;
        Level = XpCurve.LevelForXp(Xp);
        XpChanged?.Invoke(this);
        if (Level != old)
        {
            OnLevelChanged(old, Level);
            LeveledUp?.Invoke(this, old, Level);
        }
    }

    /// <summary>Debug: give XP.</summary>
    public void GrantXp(Num amount) => AddXp(amount);

    /// <summary>Debug: jump to a level (only upwards; XP is set to that level's threshold).</summary>
    public void DebugSetLevel(int level)
    {
        level = Math.Clamp(level, 1, XpCurve.MaxLevel);
        if (level <= Level) return;
        AddXp(XpCurve.XpForLevel(level) - Xp);
    }

    /// <summary>Called after the level changes (level up or load). Apply level-based effects here.</summary>
    protected virtual void OnLevelChanged(int oldLevel, int newLevel) { }

    protected virtual void SaveExtra(SaveData data) { }
    protected virtual void LoadExtra(SaveData data) { }

    public SaveData Save()
    {
        var d = new SaveData().Set("level", Level).Set("xp", Xp);
        SaveExtra(d);
        return d;
    }

    public void Load(SaveData data)
    {
        var xp = data.Get("xp", Num.Zero);
        Xp = xp.IsFinite && xp > Num.Zero ? xp : Num.Zero;
        var old = Level;
        Level = XpCurve.LevelForXp(Xp);
        LoadExtra(data);
        OnLevelChanged(old, Level);
        XpChanged?.Invoke(this);
    }
}

/// <summary>Health, Necromancy, Magic: levels only, no page and no automation in v0.1.</summary>
public abstract class LevelOnlySkill : SkillBase
{
    protected LevelOnlySkill(string id, string name, SkillContext context) : base(id, name, context) { }

    /// <summary>Nothing advances on the tick; these skills are not registered with the TickManager.</summary>
    public override void Tick(long ticks) { }
}
