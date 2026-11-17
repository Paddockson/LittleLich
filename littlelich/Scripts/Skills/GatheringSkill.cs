using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content.Definitions;
using LittleLich.Core;

namespace LittleLich.Skills;

/// <summary>
/// A skill whose necromancers gather. Activities come from JSON (<see cref="GatheringActivityDefinition"/> with this skill id).
///
/// Necromancer counts live in the LichManager's pool (owner = skill id, activity = activity id); this class
/// asks the pool instead of keeping a second copy, so the two can never disagree.
///
/// Per tick batch, per activity: units = assigned x rate x speed modifier x ticks (+ carried fraction), in exact
/// fixed point so Tick(n) equals n x Tick(1). Output goes to the stockpile within the cap; XP per unit actually stored.
/// If the output is full, production stops (necromancers stay assigned) and the activity shows as capped.
/// </summary>
public abstract class GatheringSkill : SkillBase
{
    private sealed class State
    {
        public long Carry;      // micro-units of the next unit
        public bool Capped;
    }

    private readonly Dictionary<string, State> _state = new(StringComparer.Ordinal);

    protected GatheringSkill(string id, string name, SkillContext context) : base(id, name, context)
    {
        Activities = context.Content.All<GatheringActivityDefinition>()
            .Where(a => a.Skill == id)
            .OrderBy(a => a.RequiredLevel) // stable: load order within a level
            .ToList();
        foreach (var a in Activities) _state[a.Id] = new State();
    }

    /// <summary>All activities for this skill, by required level.</summary>
    public IReadOnlyList<GatheringActivityDefinition> Activities { get; }

    /// <summary>(activity id, change in necromancers).</summary>
    public event Action<string, long>? ActivityAssigned;
    /// <summary>(activity id, capped now).</summary>
    public event Action<string, bool>? CappedChanged;

    public GatheringActivityDefinition Activity(string activityId) =>
        Activities.FirstOrDefault(a => a.Id == activityId)
        ?? throw new ArgumentException($"{Name} has no activity '{activityId}'.", nameof(activityId));

    /// <summary>Level reached and not locked behind research.</summary>
    public bool IsUnlocked(GatheringActivityDefinition activity) => Level >= activity.RequiredLevel && !IsResearchLocked(activity);

    public bool IsResearchLocked(GatheringActivityDefinition activity) => Context.Unlocks.IsLocked(Unlocks.Activity, activity.Id);
    public IEnumerable<GatheringActivityDefinition> UnlockedActivities => Activities.Where(IsUnlocked);

    public long GetAssigned(string activityId) => Context.Lich.Pool.GetAssigned(Id, activityId);

    public bool IsCapped(string activityId) => _state.TryGetValue(activityId, out var s) && s.Capped;

    /// <summary>Output multiplier from the "&lt;skill&gt;_speed" stat (base 1).</summary>
    public double SpeedMultiplier => Math.Max(0, Context.Lich.Stats.Get(SkillIds.SpeedStat(Id)).ToDouble());

    /// <summary>Units per tick the activity's necromancers make right now (0 when capped).</summary>
    public double RatePerTick(string activityId) =>
        IsCapped(activityId) ? 0 : GetAssigned(activityId) * Activity(activityId).Rate * SpeedMultiplier;

    /// <summary>
    /// Assigns up to <paramref name="count"/> free necromancers to an unlocked activity.
    /// Returns how many were actually assigned (0 if locked or none free).
    /// </summary>
    public long AddNecromancer(string activityId, long count)
    {
        var a = Activity(activityId);
        if (!IsUnlocked(a) || count <= 0) return 0;
        var n = Context.Lich.TryAssign(Id, activityId, count);
        if (n > 0) ActivityAssigned?.Invoke(activityId, n);
        return n;
    }

    /// <summary>Frees up to <paramref name="count"/> necromancers. Returns how many were freed.</summary>
    public long RemoveNecromancer(string activityId, long count)
    {
        Activity(activityId);
        var n = Context.Lich.Unassign(Id, activityId, count);
        if (n > 0) ActivityAssigned?.Invoke(activityId, -n);
        return n;
    }

    public override void Tick(long ticks)
    {
        if (ticks <= 0) return;
        var speed = SpeedMultiplier;
        var xp = Num.Zero;
        foreach (var a in Activities)
        {
            var count = GetAssigned(a.Id);
            var s = _state[a.Id];
            if (count <= 0)
            {
                SetCapped(a.Id, s, false);
                continue;
            }

            var perNecromancer = SkillContext.ToMicro(a.Rate * speed);
            var progress = (Int128)s.Carry + (Int128)count * perNecromancer * ticks;
            var units = SkillContext.Clamp(progress / SkillContext.Micro);
            var room = Context.Stock.Room(a.Output);

            long stored;
            bool capped;
            if (units > room || room == 0)
            {
                stored = room;
                capped = true;
                s.Carry = 0;
            }
            else
            {
                stored = units;
                capped = false;
                s.Carry = (long)(progress % SkillContext.Micro);
            }

            if (stored > 0)
            {
                Context.Stock.Add(a.Output, stored);
                xp += (Num)stored * a.Xp;
                Context.Recap()?.AddGained(Context.Stock.KindOf(a.Output), a.Output, stored);
            }
            if (capped) Context.Recap()?.MarkCapped(a.Output);
            SetCapped(a.Id, s, capped);
        }
        if (xp > Num.Zero)
        {
            Context.Recap()?.AddGained("xp", Id, xp);
            AddXp(xp);
        }
    }

    private void SetCapped(string activityId, State s, bool capped)
    {
        if (s.Capped == capped) return;
        s.Capped = capped;
        CappedChanged?.Invoke(activityId, capped);
    }

    protected override void SaveExtra(SaveData data) =>
        data.Set("carry", _state.Where(kv => kv.Value.Carry != 0).ToDictionary(kv => kv.Key, kv => kv.Value.Carry));

    protected override void LoadExtra(SaveData data)
    {
        var carry = data.Get("carry", new Dictionary<string, long>());
        foreach (var (id, s) in _state)
        {
            s.Carry = Math.Clamp(carry.GetValueOrDefault(id), 0, SkillContext.Micro - 1);
            SetCapped(id, s, false);
        }
    }
}
