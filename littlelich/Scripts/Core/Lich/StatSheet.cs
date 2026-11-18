using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Core.Lich;

/// <summary>
/// Base values plus a list of <see cref="Modifier"/>s. Stat value = (base + sum of Add) x product of Mul.
/// Raises <see cref="StatChanged"/> only for stats whose final value actually changed.
/// </summary>
public sealed class StatSheet
{
    private readonly Dictionary<string, Num> _base = new();
    private readonly List<Modifier> _modifiers = new();

    public event Action<string, Num>? StatChanged;

    public IReadOnlyList<Modifier> Modifiers => _modifiers;

    /// <summary>Stats with a base value or at least one modifier.</summary>
    public IEnumerable<string> KnownStats => _base.Keys.Union(_modifiers.Select(m => m.StatId)).Distinct();

    public Num GetBase(string statId) => _base.GetValueOrDefault(statId, Num.Zero);

    public Num Get(string statId) => Combine(GetBase(statId), m => m.StatId == statId);

    /// <summary>
    /// A stat for one subject (research revamp target filter): <paramref name="baseValue"/> plus the untargeted
    /// modifiers of <paramref name="statId"/> (when <paramref name="includeUntargeted"/>) and every "stat@target"
    /// modifier whose target covers <paramref name="subject"/>. Per-necromancer targets are skipped here; read them
    /// with <see cref="PerNecromancer"/>.
    /// </summary>
    public Num GetFor(string statId, string subject, Num baseValue, bool includeUntargeted = true) =>
        Combine(baseValue, m =>
        {
            var (stat, target) = Core.StatTargets.Split(m.StatId);
            if (stat != statId) return false;
            if (target is null) return includeUntargeted;
            return !Core.StatTargets.IsPerNecromancer(target) && Core.StatTargets.Matches(target, subject);
        });

    /// <summary>Sum of the Add modifiers of "stat@per_necromancer_in_..." targets covering the subject (value per necromancer).</summary>
    public double PerNecromancer(string statId, string subject)
    {
        double sum = 0;
        foreach (var m in _modifiers)
        {
            var (stat, target) = Core.StatTargets.Split(m.StatId);
            if (stat == statId && target is not null && Core.StatTargets.IsPerNecromancer(target) && Core.StatTargets.Matches(target, subject) && m.Op == ModifierOp.Add)
                sum += m.Value.ToDouble();
        }
        return sum;
    }

    private Num Combine(Num baseValue, Func<Modifier, bool> match)
    {
        Num? set = null;
        var add = Num.Zero;
        var mul = Num.One;
        foreach (var m in _modifiers)
        {
            if (!match(m)) continue;
            switch (m.Op)
            {
                case ModifierOp.Add: add += m.Value; break;
                case ModifierOp.Mul: mul *= m.Value; break;
                case ModifierOp.Set: if (set is null || m.Value > set.Value) set = m.Value; break;
            }
        }
        return ((set ?? baseValue) + add) * mul;
    }

    public void SetBase(string statId, Num value) =>
        Mutate(new[] { statId }, () => _base[statId] = value);

    public void Add(Modifier modifier)
    {
        if (string.IsNullOrWhiteSpace(modifier.StatId)) throw new ArgumentException("Stat id is required.", nameof(modifier));
        if (string.IsNullOrWhiteSpace(modifier.Source)) throw new ArgumentException("Source is required.", nameof(modifier));
        Mutate(new[] { modifier.StatId }, () => _modifiers.Add(modifier));
    }

    /// <summary>Removes every modifier from a source (e.g. unequipping gear). Returns how many were removed.</summary>
    public int RemoveBySource(string source) => RemoveWhere(m => m.Source == source);

    /// <summary>Removes timed modifiers whose <see cref="Modifier.ExpiresAtTick"/> is at or before <paramref name="tick"/>.</summary>
    public int ExpireAt(long tick) => RemoveWhere(m => m.ExpiresAtTick is long t && t <= tick);

    public int RemoveWhere(Predicate<Modifier> match)
    {
        var hit = _modifiers.Where(m => match(m)).ToList();
        if (hit.Count == 0) return 0;
        Mutate(hit.Select(m => m.StatId).Distinct(), () => _modifiers.RemoveAll(match));
        return hit.Count;
    }

    /// <summary>Replaces all modifiers (save load). Raises changes for every affected stat.</summary>
    internal void ReplaceModifiers(IEnumerable<Modifier> modifiers)
    {
        var incoming = modifiers.ToList();
        var affected = _modifiers.Select(m => m.StatId).Union(incoming.Select(m => m.StatId)).Distinct().ToList();
        Mutate(affected, () =>
        {
            _modifiers.Clear();
            _modifiers.AddRange(incoming);
        });
    }

    private void Mutate(IEnumerable<string> statIds, Action change)
    {
        var ids = statIds.ToList();
        var before = ids.Select(Get).ToList();
        change();
        for (var i = 0; i < ids.Count; i++)
        {
            var now = Get(ids[i]);
            if (now != before[i]) StatChanged?.Invoke(ids[i], now);
        }
    }
}
