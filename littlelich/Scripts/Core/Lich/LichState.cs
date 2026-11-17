using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Core.Lich;

/// <summary>
/// The lich as plain game state (the LichManager autoload wraps this): stats with buffs/debuffs,
/// health, and the necromancer pool with every assignment. Save section "lich".
///
/// v0.1 decisions (phase 2): no health regeneration (health only changes through Damage/Heal/RestoreFullHealth);
/// the spell limit is called Insight (<see cref="LichStats.MaxInsight"/>).
/// Per-skill classes (phase 4) and the SpellBook register here later.
/// </summary>
public sealed class LichState : ISaveable, ITickable
{
    public const string DebugSource = "debug";

    private bool _quiet;

    public LichState()
    {
        Stats.StatChanged += OnStatChanged;
        Pool.Changed += () => { if (!_quiet) PoolChanged?.Invoke(); };
        Pool.AssignmentChanged += (o, a, d) => { if (!_quiet) AssignmentChanged?.Invoke(o, a, d); };
        ResetToDefaults();
        Health = MaxHealth;
    }

    public string SaveKey => "lich";

    public NecromancerPool Pool { get; } = new();
    public StatSheet Stats { get; } = new();

    /// <summary>Ticks this lich has lived through. Timed modifiers expire against this clock.</summary>
    public long CurrentTick { get; private set; }

    public Num Health { get; private set; }
    public Num MaxHealth => Stats.Get(LichStats.MaxHealth);
    public Num MaxInsight => Stats.Get(LichStats.MaxInsight);

    /// <summary>Free, Assigned or Max changed.</summary>
    public event Action? PoolChanged;
    /// <summary>One assignment changed: owner, activity, signed change (+assigned, -freed).</summary>
    public event Action<string, string, long>? AssignmentChanged;
    /// <summary>A stat's final value changed (stat id, new value).</summary>
    public event Action<string, Num>? StatChanged;
    /// <summary>Current or max health changed.</summary>
    public event Action? HealthChanged;

    // ---- Tick ----

    /// <summary>
    /// Lich tick step (tick order step 2): advance the clock and expire finished buffs/debuffs.
    /// No regeneration in v0.1. Tick(n) == n x Tick(1) because expiry only compares against the clock.
    /// </summary>
    public void Tick(long ticks)
    {
        if (ticks <= 0) return;
        CurrentTick += ticks;
        Stats.ExpireAt(CurrentTick);
    }

    // ---- Modifiers ----

    public void AddModifier(Modifier modifier) => Stats.Add(modifier);

    /// <summary>Adds a buff/debuff that lasts <paramref name="durationTicks"/> ticks from now.</summary>
    public Modifier AddTimedModifier(string statId, ModifierOp op, Num value, string source, long durationTicks)
    {
        if (durationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(durationTicks), "Duration must be positive.");
        var m = new Modifier(statId, op, value, source, CurrentTick + durationTicks);
        Stats.Add(m);
        return m;
    }

    public int RemoveModifiersFrom(string source) => Stats.RemoveBySource(source);

    // ---- Health ----

    public void Damage(Num amount)
    {
        if (amount <= Num.Zero) return;
        SetHealth(Num.Max(Num.Zero, Health - amount));
    }

    public void Heal(Num amount)
    {
        if (amount <= Num.Zero) return;
        SetHealth(Num.Min(MaxHealth, Health + amount));
    }

    public void RestoreFullHealth() => SetHealth(MaxHealth);

    public bool IsDead => Health <= Num.Zero;

    private void SetHealth(Num value)
    {
        if (value == Health) return;
        Health = value;
        if (!_quiet) HealthChanged?.Invoke();
    }

    // ---- Necromancers ----

    public long AddToPool(long count) => Pool.AddToPool(count);
    public long TryAssign(string ownerId, string activityId, long count) => Pool.TryAssign(ownerId, activityId, count);
    public long Unassign(string ownerId, string activityId, long count) => Pool.Unassign(ownerId, activityId, count);
    public long GetAssigned(string ownerId, string activityId) => Pool.GetAssigned(ownerId, activityId);
    public IReadOnlyList<Assignment> AllAssignments() => Pool.AllAssignments();

    /// <summary>
    /// Debug command: adds necromancers, raising Max with a permanent "debug" modifier if there is no room.
    /// Returns how many were added (only limited by <see cref="NecromancerPool.HardCap"/>).
    /// </summary>
    public long DebugAddNecromancers(long count)
    {
        if (count <= 0) return 0;
        var shortfall = Pool.Total + Math.Min(count, NecromancerPool.HardCap) - Pool.Max;
        if (shortfall > 0)
        {
            var existing = Stats.Modifiers
                .Where(m => m.Source == DebugSource && m.StatId == LichStats.MaxNecromancers && m.Op == ModifierOp.Add && !m.IsTimed)
                .Aggregate(Num.Zero, (s, m) => s + m.Value);
            Stats.RemoveWhere(m => m.Source == DebugSource && m.StatId == LichStats.MaxNecromancers && m.Op == ModifierOp.Add && !m.IsTimed);
            Stats.Add(new Modifier(LichStats.MaxNecromancers, ModifierOp.Add, existing + shortfall, DebugSource));
        }
        return Pool.AddToPool(count);
    }

    // ---- Stat side effects ----

    private void OnStatChanged(string statId, Num value)
    {
        if (statId == LichStats.MaxNecromancers) SyncPoolMax();
        if (statId == LichStats.MaxHealth)
        {
            // No regen: a higher max does not heal, a lower max clamps current health.
            if (Health > value) Health = Num.Max(Num.Zero, value);
            if (!_quiet) HealthChanged?.Invoke();
        }
        if (!_quiet) StatChanged?.Invoke(statId, value);
    }

    private void SyncPoolMax()
    {
        var max = Stats.Get(LichStats.MaxNecromancers);
        Pool.SetMax(max <= Num.Zero ? 0 : Num.Floor(max).ToLongClamped());
    }

    private void ResetToDefaults()
    {
        Stats.SetBase(LichStats.MaxHealth, LichStats.BaseMaxHealth);
        Stats.SetBase(LichStats.MaxInsight, LichStats.BaseMaxInsight);
        Stats.SetBase(LichStats.MaxNecromancers, LichStats.BaseMaxNecromancers);
        SyncPoolMax();
    }

    // ---- Save / load ----

    public SaveData Save() => new SaveData()
        .Set("current_tick", CurrentTick)
        .Set("health", Health)
        .Set("necromancers", new PoolDto
        {
            Total = Pool.Total,
            Assignments = Pool.AllAssignments()
                .Select(a => new AssignmentDto { OwnerId = a.OwnerId, ActivityId = a.ActivityId, Count = a.Count })
                .ToList(),
        })
        .Set("modifiers", Stats.Modifiers.Select(ModifierDto.From).ToList());

    /// <summary>Restores state; an empty section is a new game (full health, no necromancers). Raises every event once at the end.</summary>
    public void Load(SaveData data)
    {
        _quiet = true;
        try
        {
            CurrentTick = Math.Max(0, data.Get("current_tick", 0L));

            var mods = data.Get("modifiers", new List<ModifierDto>())
                .Select(d => d.ToModifier())
                .OfType<Modifier>()
                // Anything that should have expired already is dropped.
                .Where(m => m.ExpiresAtTick is not long t || t > CurrentTick);
            Stats.ReplaceModifiers(mods);
            ResetToDefaults();

            var pool = data.Get<PoolDto?>("necromancers", null) ?? new PoolDto();
            Pool.Restore(pool.Total, pool.Assignments.Select(a => new Assignment(a.OwnerId ?? "", a.ActivityId ?? "", a.Count)));

            var max = MaxHealth;
            var health = data.Has("health") ? data.Get("health", max) : max;
            Health = Num.Max(Num.Zero, Num.Min(health, max));
        }
        finally
        {
            _quiet = false;
        }

        PoolChanged?.Invoke();
        HealthChanged?.Invoke();
        foreach (var id in Stats.KnownStats.OrderBy(s => s, StringComparer.Ordinal))
            StatChanged?.Invoke(id, Stats.Get(id));
    }

    // Save shapes: ids and counts only. Public setters for System.Text.Json.
    public sealed class PoolDto
    {
        public long Total { get; set; }
        public List<AssignmentDto> Assignments { get; set; } = new();
    }

    public sealed class AssignmentDto
    {
        public string? OwnerId { get; set; }
        public string? ActivityId { get; set; }
        public long Count { get; set; }
    }

    public sealed class ModifierDto
    {
        public string? StatId { get; set; }
        public string? Op { get; set; }
        public Num Value { get; set; }
        public string? Source { get; set; }
        public long? ExpiresAtTick { get; set; }

        public static ModifierDto From(Modifier m) => new()
        {
            StatId = m.StatId,
            Op = m.Op == ModifierOp.Add ? "add" : "mul",
            Value = m.Value,
            Source = m.Source,
            ExpiresAtTick = m.ExpiresAtTick,
        };

        /// <summary>Null if the entry is unusable (blank ids or unknown op); it is dropped on load.</summary>
        public Modifier? ToModifier()
        {
            if (string.IsNullOrWhiteSpace(StatId) || string.IsNullOrWhiteSpace(Source)) return null;
            ModifierOp? op = Op switch { "add" => ModifierOp.Add, "mul" => ModifierOp.Mul, _ => null };
            return op is { } o ? new Modifier(StatId, o, Value, Source, ExpiresAtTick) : null;
        }
    }
}
