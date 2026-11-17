using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Core.Ticks;

/// <summary>
/// Runs registered <see cref="ITickable"/> features in a fixed order. Plain C#; the TickManager autoload
/// owns one and drives it from real time and from offline time.
///
/// One call of <see cref="RunTicks"/>(n):
///   1. TickCount += n, raise <see cref="TickStarted"/>
///   2-6. call Tick(n) on every feature, sorted by group, then order, then registration order
///   7. raise <see cref="TickEnded"/> (the TickManager hangs UI updates and the autosave check on this)
///
/// Batching contract: each feature's Tick(n) must equal n calls of Tick(1) for that feature on its own.
/// Running a batch therefore processes every feature n ticks in group order, rather than interleaving
/// features tick by tick; features must not rely on another feature's result from the same tick
/// beyond what their group order already guarantees for a single tick.
///
/// Registration: features register when their manager creates them and unregister on dispose.
/// Registering or unregistering during a tick is allowed and takes effect from the next feature call
/// (a feature unregistered mid-tick is not called again).
/// </summary>
public sealed class TickScheduler
{
    private sealed class Entry
    {
        public required ITickable Feature { get; init; }
        public required TickGroup Group { get; init; }
        public required int Order { get; init; }
        public required long Seq { get; init; }
        public bool Removed { get; set; }
    }

    private readonly List<Entry> _entries = new();
    private long _seq;
    private bool _running;

    /// <summary>Ticks processed in this game (synced from the save's total ticks when a game starts).</summary>
    public long TickCount { get; set; }

    /// <summary>The recap being collected while an offline run is processed; null during live ticks.</summary>
    public AwayRecap? ActiveRecap { get; private set; }

    public bool IsRunning => _running;

    /// <summary>Step 1: (tick count after the increment, ticks in this batch).</summary>
    public event Action<long, long>? TickStarted;

    /// <summary>Step 7: (tick count, ticks in this batch).</summary>
    public event Action<long, long>? TickEnded;

    public void Register(ITickable feature, TickGroup group, int order = 0)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (IsRegistered(feature))
            throw new InvalidOperationException($"{feature.GetType().Name} is already registered with the tick scheduler.");
        var e = new Entry { Feature = feature, Group = group, Order = order, Seq = _seq++ };
        // Insert in sorted position so the list is always in processing order.
        var i = _entries.FindIndex(x => Compare(e, x) < 0);
        if (i < 0) _entries.Add(e); else _entries.Insert(i, e);
    }

    public bool Unregister(ITickable feature)
    {
        var i = _entries.FindIndex(x => ReferenceEquals(x.Feature, feature));
        if (i < 0) return false;
        _entries[i].Removed = true;
        _entries.RemoveAt(i);
        return true;
    }

    public bool IsRegistered(ITickable feature) => _entries.Any(x => ReferenceEquals(x.Feature, feature));

    /// <summary>Registered features in processing order (for tests and the dev UI).</summary>
    public IReadOnlyList<(ITickable Feature, TickGroup Group, int Order)> Registered =>
        _entries.Select(e => (e.Feature, e.Group, e.Order)).ToList();

    /// <summary>Processes <paramref name="ticks"/> ticks as one batch. Not re-entrant.</summary>
    public void RunTicks(long ticks)
    {
        if (ticks <= 0) return;
        if (_running) throw new InvalidOperationException("RunTicks was called while a tick was already running.");
        _running = true;
        try
        {
            TickCount += ticks;
            TickStarted?.Invoke(TickCount, ticks);
            foreach (var e in _entries.ToArray())
            {
                if (e.Removed) continue;
                e.Feature.Tick(ticks);
            }
        }
        finally
        {
            _running = false;
        }
        TickEnded?.Invoke(TickCount, ticks);
    }

    /// <summary>
    /// Offline progress: converts <paramref name="elapsedSeconds"/> with the base tick length (never a sped-up one),
    /// caps it at <paramref name="maxTicks"/>, and runs it as one batch while collecting an <see cref="AwayRecap"/>.
    /// </summary>
    public AwayRecap RunOffline(double elapsedSeconds, long maxTicks = OfflineTime.MaxOfflineTicks)
    {
        var uncapped = OfflineTime.ToTicks(elapsedSeconds);
        var ticks = Math.Min(uncapped, Math.Max(0, maxTicks));
        var recap = new AwayRecap
        {
            ElapsedSeconds = Math.Max(0, elapsedSeconds),
            Ticks = ticks,
            TicksLostToCap = uncapped - ticks,
        };
        ActiveRecap = recap;
        try
        {
            RunTicks(ticks);
        }
        finally
        {
            ActiveRecap = null;
        }
        return recap;
    }

    private static int Compare(Entry a, Entry b)
    {
        var c = a.Group.CompareTo(b.Group);
        if (c != 0) return c;
        c = a.Order.CompareTo(b.Order);
        return c != 0 ? c : a.Seq.CompareTo(b.Seq);
    }
}
