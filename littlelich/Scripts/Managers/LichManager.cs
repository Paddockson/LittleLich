using System;
using System.Collections.Generic;
using Godot;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Ticks;

namespace LittleLich.Managers;

/// <summary>
/// Autoload singleton (after GameManager). Thin Godot adapter over <see cref="LichState"/>:
/// forwards commands and turns state events into signals for the UI. All logic lives in LichState.
/// GameManager registers <see cref="State"/> as a saveable during boot.
/// </summary>
public partial class LichManager : Node
{
    public static LichManager Instance { get; private set; } = null!;

    [Signal] public delegate void NecromancerPoolChangedEventHandler(long free, long assigned, long max);
    [Signal] public delegate void AssignmentChangedEventHandler(string ownerId, string activityId, long delta);
    [Signal] public delegate void StatChangedEventHandler(string statId, double value);
    [Signal] public delegate void HealthChangedEventHandler(double current, double max);

    public LichState State { get; } = new();

    public LichManager()
    {
        // Set in the constructor so GameManager can find it during boot whatever order autoloads become ready in.
        Instance = this;
        State.PoolChanged += () =>
            EmitSignal(SignalName.NecromancerPoolChanged, State.Pool.Free, State.Pool.Assigned, State.Pool.Max);
        State.AssignmentChanged += (o, a, d) => EmitSignal(SignalName.AssignmentChanged, o, a, d);
        State.StatChanged += (id, v) => EmitSignal(SignalName.StatChanged, id, v.ToDouble());
        State.HealthChanged += () =>
            EmitSignal(SignalName.HealthChanged, State.Health.ToDouble(), State.MaxHealth.ToDouble());
    }

    public override void _Ready()
    {
        // Tick step 2: buffs expire (no regeneration in v0.1). Registered once; the same State object is reloaded in place.
        TickManager.Instance.Register(State, TickGroup.Lich);
    }

    public override void _ExitTree() => TickManager.Instance?.Unregister(State);

    // ---- Commands ----

    public long AddToPool(long count) => State.AddToPool(count);
    public long TryAssign(string ownerId, string activityId, long count) => State.TryAssign(ownerId, activityId, count);
    public long Unassign(string ownerId, string activityId, long count) => State.Unassign(ownerId, activityId, count);
    public long GetAssigned(string ownerId, string activityId) => State.GetAssigned(ownerId, activityId);
    public IReadOnlyList<Assignment> AllAssignments() => State.AllAssignments();

    public void AddModifier(Modifier modifier) => State.AddModifier(modifier);
    public Modifier AddTimedModifier(string statId, ModifierOp op, Num value, string source, long durationTicks) =>
        State.AddTimedModifier(statId, op, value, source, durationTicks);
    public int RemoveModifiersFrom(string source) => State.RemoveModifiersFrom(source);
    public void Damage(Num amount) => State.Damage(amount);
    public void Heal(Num amount) => State.Heal(amount);
    public void RestoreFullHealth() => State.RestoreFullHealth();

    /// <summary>Debug command: add necromancers to the pool (raises Max if needed).</summary>
    public long DebugAddNecromancers(long count)
    {
        var added = State.DebugAddNecromancers(count);
        GD.Print($"[LichManager] Debug: added {added} necromancers (total {State.Pool.Total}).");
        return added;
    }
}
