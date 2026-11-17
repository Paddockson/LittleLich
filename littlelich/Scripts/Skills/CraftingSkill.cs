using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content.Definitions;
using LittleLich.Core;

namespace LittleLich.Skills;

public enum RecipeStatus
{
    /// <summary>No necromancers assigned.</summary>
    Idle,
    Running,
    /// <summary>An input ran out; see <see cref="CraftingSkill.MissingInput"/>.</summary>
    MissingInput,
    /// <summary>The output is at its cap.</summary>
    OutputFull,
}

/// <summary>
/// A skill whose necromancers craft. Recipes come from JSON (<see cref="RecipeDefinition"/> with this skill id).
/// No queue: each recipe with necromancers crafts again and again while its inputs last.
///
/// Per tick batch, per recipe (in recipe order): crafts = assigned x ticks x speed / recipe ticks (+ carried progress),
/// limited by the inputs in stock and the room for the output. Inputs are consumed, the output stored, XP given per craft.
/// When limited, progress resets and the recipe shows why (missing input or output full); necromancers stay assigned.
/// Fixed-point maths, so Tick(n) equals n x Tick(1) for a recipe on its own. Recipes in one skill that compete for the
/// same input are served in recipe order within a batch.
/// </summary>
public abstract class CraftingSkill : SkillBase
{
    private sealed class State
    {
        public long Carry;   // progress in micro-ticks toward the next craft
        public RecipeStatus Status;
        public string? Missing;
    }

    private readonly Dictionary<string, State> _state = new(StringComparer.Ordinal);

    protected CraftingSkill(string id, string name, SkillContext context) : base(id, name, context)
    {
        Recipes = context.Content.All<RecipeDefinition>()
            .Where(r => r.Skill == id)
            .OrderBy(r => r.RequiredLevel)
            .ToList();
        foreach (var r in Recipes) _state[r.Id] = new State();
    }

    public IReadOnlyList<RecipeDefinition> Recipes { get; }

    /// <summary>(recipe id, change in necromancers).</summary>
    public event Action<string, long>? RecipeAssigned;
    /// <summary>(recipe id, new status).</summary>
    public event Action<string, RecipeStatus>? StatusChanged;

    public RecipeDefinition Recipe(string recipeId) =>
        Recipes.FirstOrDefault(r => r.Id == recipeId)
        ?? throw new ArgumentException($"{Name} has no recipe '{recipeId}'.", nameof(recipeId));

    /// <summary>Level reached and not locked behind research.</summary>
    public bool IsUnlocked(RecipeDefinition recipe) => Level >= recipe.RequiredLevel && !IsResearchLocked(recipe);

    public bool IsResearchLocked(RecipeDefinition recipe) => Context.Unlocks.IsLocked(Unlocks.Recipe, recipe.Id);
    public IEnumerable<RecipeDefinition> UnlockedRecipes => Recipes.Where(IsUnlocked);

    public long GetAssigned(string recipeId) => Context.Lich.Pool.GetAssigned(Id, recipeId);
    public RecipeStatus Status(string recipeId) => _state.TryGetValue(recipeId, out var s) ? s.Status : RecipeStatus.Idle;
    public string? MissingInput(string recipeId) => _state.TryGetValue(recipeId, out var s) ? s.Missing : null;

    public double SpeedMultiplier => Math.Max(0, Context.Lich.Stats.Get(SkillIds.SpeedStat(Id)).ToDouble());

    /// <summary>Crafts per tick at full speed with the current necromancers (ignores inputs).</summary>
    public double CraftsPerTick(string recipeId) => GetAssigned(recipeId) * SpeedMultiplier / Recipe(recipeId).Ticks;

    public long AddNecromancer(string recipeId, long count)
    {
        var r = Recipe(recipeId);
        if (!IsUnlocked(r) || count <= 0) return 0;
        var n = Context.Lich.TryAssign(Id, recipeId, count);
        if (n > 0) RecipeAssigned?.Invoke(recipeId, n);
        return n;
    }

    public long RemoveNecromancer(string recipeId, long count)
    {
        Recipe(recipeId);
        var n = Context.Lich.Unassign(Id, recipeId, count);
        if (n > 0) RecipeAssigned?.Invoke(recipeId, -n);
        if (GetAssigned(recipeId) == 0) SetStatus(recipeId, _state[recipeId], RecipeStatus.Idle, null);
        return n;
    }

    public override void Tick(long ticks)
    {
        if (ticks <= 0) return;
        var speed = SkillContext.ToMicro(SpeedMultiplier);
        var stock = Context.Stock;
        var xp = Num.Zero;
        foreach (var r in Recipes)
        {
            var s = _state[r.Id];
            var count = GetAssigned(r.Id);
            if (count <= 0)
            {
                SetStatus(r.Id, s, RecipeStatus.Idle, null);
                continue;
            }

            var unit = (Int128)r.Ticks * SkillContext.Micro;
            var progress = (Int128)s.Carry + (Int128)count * ticks * speed;
            var potential = SkillContext.Clamp(progress / unit);

            long affordable = long.MaxValue;
            string? limiting = null;
            foreach (var input in r.Inputs)
            {
                var can = stock.Get(input.Id) / input.Count;
                if (can < affordable)
                {
                    affordable = can;
                    limiting = input.Id;
                }
            }
            var outRoom = stock.Room(r.Output.Id) / r.Output.Count;
            var crafts = Math.Min(potential, Math.Min(affordable, outRoom));

            if (crafts > 0)
            {
                foreach (var input in r.Inputs) stock.TryRemove(input.Id, crafts * input.Count);
                var made = stock.Add(r.Output.Id, crafts * r.Output.Count);
                var craftXp = (Num)crafts * r.Xp;
                xp += craftXp;
                Context.Recap()?.AddGained(stock.KindOf(r.Output.Id), r.Output.Id, made);
            }

            if (potential > crafts || affordable == 0 || outRoom == 0)
            {
                s.Carry = 0;
                if (affordable <= outRoom)
                {
                    SetStatus(r.Id, s, RecipeStatus.MissingInput, limiting);
                }
                else
                {
                    SetStatus(r.Id, s, RecipeStatus.OutputFull, null);
                    Context.Recap()?.MarkCapped(r.Output.Id);
                }
            }
            else
            {
                s.Carry = (long)(progress % unit);
                SetStatus(r.Id, s, RecipeStatus.Running, null);
            }
        }
        if (xp > Num.Zero)
        {
            Context.Recap()?.AddGained("xp", Id, xp);
            AddXp(xp);
        }
    }

    private void SetStatus(string recipeId, State s, RecipeStatus status, string? missing)
    {
        if (s.Status == status && s.Missing == missing) return;
        s.Status = status;
        s.Missing = missing;
        StatusChanged?.Invoke(recipeId, status);
    }

    protected override void SaveExtra(SaveData data) =>
        data.Set("carry", _state.Where(kv => kv.Value.Carry != 0).ToDictionary(kv => kv.Key, kv => kv.Value.Carry));

    protected override void LoadExtra(SaveData data)
    {
        var carry = data.Get("carry", new Dictionary<string, long>());
        foreach (var r in Recipes)
        {
            var s = _state[r.Id];
            s.Carry = Math.Clamp(carry.GetValueOrDefault(r.Id), 0, Math.Max(0, r.Ticks * SkillContext.Micro - 1));
            SetStatus(r.Id, s, RecipeStatus.Idle, null);
        }
    }
}
