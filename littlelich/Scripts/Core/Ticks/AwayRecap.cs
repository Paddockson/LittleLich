using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LittleLich.Core.Ticks;

/// <summary>
/// What happened while the player was away. The scheduler makes one per offline run and exposes it as
/// <see cref="TickScheduler.ActiveRecap"/> while that run is processed; feature classes add to it
/// (it is null during live ticks). The recap screen comes later; for now the dev UI logs it.
/// </summary>
public sealed class AwayRecap
{
    private readonly Dictionary<(string Kind, string Id), Num> _gained = new();
    private readonly SortedSet<string> _capped = new(StringComparer.Ordinal);

    public double ElapsedSeconds { get; init; }
    /// <summary>Ticks actually processed.</summary>
    public long Ticks { get; init; }
    /// <summary>Ticks not processed because of the offline cap.</summary>
    public long TicksLostToCap { get; init; }

    /// <summary>Gains keyed by kind ("resource", "xp", "item"...) and id, in first-added order.</summary>
    public IReadOnlyDictionary<(string Kind, string Id), Num> Gained => _gained;

    /// <summary>Ids that hit a cap (storage full etc.) during the run.</summary>
    public IReadOnlyCollection<string> Capped => _capped;

    public bool IsEmpty => _gained.Count == 0 && _capped.Count == 0;

    public void AddGained(string kind, string id, Num amount)
    {
        if (amount == Num.Zero) return;
        var key = (kind, id);
        _gained[key] = _gained.GetValueOrDefault(key, Num.Zero) + amount;
    }

    public void MarkCapped(string id) => _capped.Add(id);

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append($"Away {TimeSpan.FromSeconds(Math.Floor(ElapsedSeconds))}: {Ticks:N0} ticks");
        if (TicksLostToCap > 0) sb.Append($" ({TicksLostToCap:N0} over the offline cap were not processed)");
        sb.Append('.');
        foreach (var ((kind, id), amount) in _gained)
            sb.Append($"\n  +{amount.ToShortString()} {kind} {id}");
        if (_capped.Count > 0) sb.Append("\n  Capped: " + string.Join(", ", _capped));
        if (IsEmpty) sb.Append(" Nothing was gained.");
        return sb.ToString();
    }
}
