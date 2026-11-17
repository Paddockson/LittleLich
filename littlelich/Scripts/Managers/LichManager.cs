using System;
using System.Collections.Generic;
using Godot;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Ticks;
using LittleLich.Content;
using LittleLich.Crypt;
using LittleLich.Skills;

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


    // ---- Skills (phase 4) ----

    [Signal] public delegate void SkillXpChangedEventHandler(string skillId, double xp);
    [Signal] public delegate void SkillLeveledUpEventHandler(string skillId, int oldLevel, int newLevel);
    /// <summary>A skill's activity or recipe gained/lost necromancers through the skill (delta signed).</summary>
    [Signal] public delegate void SkillActivityAssignedEventHandler(string skillId, string activityId, long delta);
    [Signal] public delegate void ActivityCappedChangedEventHandler(string skillId, string activityId, bool capped);
    [Signal] public delegate void RecipeStatusChangedEventHandler(string skillId, string recipeId, int status);

    private readonly List<SkillBase> _skills = new();
    private readonly Dictionary<string, SkillBase> _skillById = new(StringComparer.Ordinal);

    /// <summary>All ten skills in sidebar order (empty until boot creates them).</summary>
    public IReadOnlyList<SkillBase> Skills => _skills;

    public SkillBase? Skill(string id) => _skillById.GetValueOrDefault(id);
    public GatheringSkill? Gathering(string id) => Skill(id) as GatheringSkill;
    public CraftingSkill? Crafting(string id) => Skill(id) as CraftingSkill;

    /// <summary>
    /// Boot step 3 (called by GameManager after content loads, before the save is read): creates the ten skill
    /// classes, registers the automatable ones with the TickManager (gathering before crafting) and returns all of
    /// them for save registration.
    /// </summary>
    public IReadOnlyList<SkillBase> InitializeSkills(ContentDatabase content, Stockpile stock)
    {
        if (_skills.Count > 0) throw new InvalidOperationException("Skills are already initialised.");
        var tm = TickManager.Instance;
        var ctx = new SkillContext(State, stock, content, () => tm.Scheduler.ActiveRecap);
        foreach (var s in SkillFactory.CreateAll(ctx))
        {
            _skills.Add(s);
            _skillById[s.Id] = s;
            var id = s.Id;
            s.XpChanged += _ => EmitSignal(SignalName.SkillXpChanged, id, s.Xp.ToDouble());
            s.LeveledUp += (_, from, to) => EmitSignal(SignalName.SkillLeveledUp, id, from, to);
            switch (s)
            {
                case GatheringSkill g:
                    g.ActivityAssigned += (a, d) => EmitSignal(SignalName.SkillActivityAssigned, id, a, d);
                    g.CappedChanged += (a, c) => EmitSignal(SignalName.ActivityCappedChanged, id, a, c);
                    tm.Register(g, TickGroup.Skills, TickOrder.Gathering);
                    break;
                case CraftingSkill c:
                    c.RecipeAssigned += (r, d) => EmitSignal(SignalName.SkillActivityAssigned, id, r, d);
                    c.StatusChanged += (r, st) => EmitSignal(SignalName.RecipeStatusChanged, id, r, (int)st);
                    tm.Register(c, TickGroup.Skills, TickOrder.Crafting);
                    break;
            }
        }
        return _skills;
    }

    /// <summary>
    /// Assigns through the owning skill when the owner is a gathering/crafting skill (enforces level locks),
    /// otherwise straight to the pool (Workshop buildings, tests).
    /// </summary>
    public long AssignVia(string ownerId, string activityId, long count) => Skill(ownerId) switch
    {
        GatheringSkill g => g.AddNecromancer(activityId, count),
        CraftingSkill c => c.AddNecromancer(activityId, count),
        _ => TryAssign(ownerId, activityId, count),
    };

    public long UnassignVia(string ownerId, string activityId, long count) => Skill(ownerId) switch
    {
        GatheringSkill g => g.RemoveNecromancer(activityId, count),
        CraftingSkill c => c.RemoveNecromancer(activityId, count),
        _ => Unassign(ownerId, activityId, count),
    };

    public override void _ExitTree()
    {
        var tm = TickManager.Instance;
        if (tm is null) return;
        tm.Unregister(State);
        foreach (var s in _skills) tm.Unregister(s);
    }

    // ---- Equipment and item use (phase 6) ----

    /// <summary>A slot changed; an empty slot id means every slot was reloaded from a save.</summary>
    [Signal] public delegate void EquipmentChangedEventHandler(string slot);
    [Signal] public delegate void ItemUsedEventHandler(string itemId);

    /// <summary>The lich's seven equipment slots (null until boot).</summary>
    public Gear.Equipment? Equipment { get; private set; }
    public Gear.ItemUser? Items { get; private set; }

    /// <summary>Boot step 3 (after Crypt and skills): creates equipment and item use. Returns the equipment saveable.</summary>
    public ISaveable InitializeGear(ContentDatabase content, Crypt.Inventory inventory)
    {
        if (Equipment is not null) throw new InvalidOperationException("Gear is already initialised.");
        Equipment = new Gear.Equipment(State, inventory, content);
        Items = new Gear.ItemUser(State, inventory, content);
        Equipment.Changed += slot => EmitSignal(SignalName.EquipmentChanged, slot);
        Equipment.Reloaded += () => EmitSignal(SignalName.EquipmentChanged, "");   // "" = all slots (load)
        Items.Used += id => EmitSignal(SignalName.ItemUsed, id);
        return Equipment;
    }

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
