using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Core.Lich;

/// <summary>
/// Every necromancer the lich owns and where each one works. The single source of truth for
/// assignments: skill and building classes ask here and only keep a copy of what they were given.
///
/// Invariants (kept by every method): 0 &lt;= Assigned &lt;= Total, Free = Total - Assigned,
/// Assigned = sum of all assignment counts, no assignment has a count of 0 or less.
/// Total may sit above Max if Max drops (e.g. a buff expires); necromancers are never destroyed by that,
/// the pool just stops growing until Total is under Max again.
/// </summary>
public sealed class NecromancerPool
{
    /// <summary>Design cap: necromancers can reach 100 billion.</summary>
    public const long HardCap = 100_000_000_000;

    private readonly Dictionary<(string Owner, string Activity), long> _assignments = new();
    private IReadOnlyList<Assignment>? _sorted;

    public long Total { get; private set; }
    public long Max { get; private set; }
    public long Assigned { get; private set; }
    public long Free => Total - Assigned;

    /// <summary>Raised after any change to Total, Max or assignments (top bar: Free, Assigned, Max).</summary>
    public event Action? Changed;

    /// <summary>An assignment changed: owner, activity, signed change in count (+assigned, -freed).</summary>
    public event Action<string, string, long>? AssignmentChanged;

    /// <summary>Adds necromancers (Summoning Circle). Respects <see cref="Max"/>. Returns how many were added.</summary>
    public long AddToPool(long count)
    {
        if (count <= 0) return 0;
        var room = Max - Total;
        if (room <= 0) return 0;
        var added = Math.Min(count, room);
        Total += added;
        Changed?.Invoke();
        return added;
    }

    /// <summary>Sets the maximum, clamped to 0..<see cref="HardCap"/>.</summary>
    public void SetMax(long max)
    {
        max = Math.Clamp(max, 0, HardCap);
        if (max == Max) return;
        Max = max;
        Changed?.Invoke();
    }

    /// <summary>Assigns up to <paramref name="count"/> free necromancers. Returns how many were actually assigned.</summary>
    public long TryAssign(string ownerId, string activityId, long count)
    {
        var key = Key(ownerId, activityId);
        var n = Math.Min(count, Free);
        if (n <= 0) return 0;
        _assignments[key] = _assignments.GetValueOrDefault(key) + n;
        Assigned += n;
        Touch();
        AssignmentChanged?.Invoke(ownerId, activityId, n);
        return n;
    }

    /// <summary>Frees up to <paramref name="count"/> necromancers from an assignment. Returns how many were freed.</summary>
    public long Unassign(string ownerId, string activityId, long count)
    {
        var key = Key(ownerId, activityId);
        if (count <= 0 || !_assignments.TryGetValue(key, out var current)) return 0;
        var n = Math.Min(count, current);
        if (n == current) _assignments.Remove(key);
        else _assignments[key] = current - n;
        Assigned -= n;
        Touch();
        AssignmentChanged?.Invoke(ownerId, activityId, -n);
        return n;
    }

    /// <summary>Frees every necromancer an owner has, across all its activities. Returns how many were freed.</summary>
    public long UnassignOwner(string ownerId)
    {
        var removed = _assignments.Where(kv => kv.Key.Owner == ownerId)
            .OrderBy(kv => kv.Key.Activity, StringComparer.Ordinal).ToList();
        if (removed.Count == 0) return 0;
        long freed = 0;
        foreach (var (k, count) in removed)
        {
            freed += count;
            _assignments.Remove(k);
        }
        Assigned -= freed;
        Touch();
        foreach (var (k, count) in removed) AssignmentChanged?.Invoke(k.Owner, k.Activity, -count);
        return freed;
    }

    public long GetAssigned(string ownerId, string activityId) =>
        _assignments.GetValueOrDefault((ownerId, activityId));

    /// <summary>Total necromancers an owner has across its activities (e.g. Mana Well "3 of 11").</summary>
    public long GetAssignedToOwner(string ownerId) =>
        _assignments.Where(kv => kv.Key.Owner == ownerId).Sum(kv => kv.Value);

    /// <summary>
    /// Every assignment, sorted by owner then activity so tick processing is deterministic.
    /// The TickManager reads this every tick; the list is cached until assignments change.
    /// </summary>
    public IReadOnlyList<Assignment> AllAssignments() =>
        _sorted ??= _assignments
            .Select(kv => new Assignment(kv.Key.Owner, kv.Key.Activity, kv.Value))
            .OrderBy(a => a.OwnerId, StringComparer.Ordinal)
            .ThenBy(a => a.ActivityId, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Replaces the whole pool from save data. Bad entries (blank ids, counts &lt;= 0) are dropped and
    /// duplicates merged. If the saved total is lower than the assigned sum, the total is raised so every
    /// saved assignment is restored exactly. Does not raise <see cref="Changed"/>; the caller does.
    /// </summary>
    internal void Restore(long total, IEnumerable<Assignment> assignments)
    {
        _assignments.Clear();
        long assigned = 0;
        foreach (var a in assignments)
        {
            if (a.Count <= 0 || string.IsNullOrWhiteSpace(a.OwnerId) || string.IsNullOrWhiteSpace(a.ActivityId)) continue;
            var key = (a.OwnerId, a.ActivityId);
            _assignments[key] = _assignments.GetValueOrDefault(key) + a.Count;
            assigned += a.Count;
        }
        Assigned = assigned;
        Total = Math.Max(Math.Max(total, 0), assigned);
        _sorted = null;
    }

    private void Touch()
    {
        _sorted = null;
        Changed?.Invoke();
    }

    private static (string, string) Key(string ownerId, string activityId)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("Owner id is required.", nameof(ownerId));
        if (string.IsNullOrWhiteSpace(activityId)) throw new ArgumentException("Activity id is required.", nameof(activityId));
        return (ownerId, activityId);
    }
}
