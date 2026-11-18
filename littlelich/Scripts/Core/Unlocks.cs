using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Core;

/// <summary>
/// What research has unlocked (the brief's "UnlockManager"). Content that any research effect unlocks is
/// "lockable" and stays locked until that effect is applied; everything else is open. Every system asks
/// <see cref="IsLocked"/> and simply does not draw locked things. Rebuilt from completed research on load, so it is
/// not saved itself. Raises, limits and efficiencies from research are stat modifiers, not kept here.
/// </summary>
public sealed class Unlocks
{
    public const string Unit = "unit";
    public const string Building = "building";
    public const string Spell = "spell";
    public const string Feature = "feature";
    public const string Recipe = "recipe";
    public const string Activity = "activity";
    public const string Transmutation = "transmutation";

    /// <summary>Feature id that opens the Summoning Circle (research node Summoning).</summary>
    public const string SummoningFeature = "summoning";

    private readonly HashSet<(string Kind, string Id)> _lockable = new();
    private readonly HashSet<(string Kind, string Id)> _unlocked = new();

    /// <summary>Nothing lockable: everything is open (tests and systems without research).</summary>
    public static Unlocks None => new();

    /// <summary>(kind, id) became available.</summary>
    public event Action<string, string>? Unlocked;

    public void SetLockable(string kind, string id) => _lockable.Add((kind, id));

    public bool IsLockable(string kind, string id) => _lockable.Contains((kind, id));

    public bool IsLocked(string kind, string id) => _lockable.Contains((kind, id)) && !_unlocked.Contains((kind, id));

    public bool IsUnlocked(string kind, string id) => !IsLocked(kind, id);

    /// <summary>Returns true if it was newly unlocked.</summary>
    public bool Unlock(string kind, string id)
    {
        if (!_unlocked.Add((kind, id))) return false;
        Unlocked?.Invoke(kind, id);
        return true;
    }

    public IEnumerable<string> UnlockedOf(string kind) => _unlocked.Where(u => u.Kind == kind).Select(u => u.Id);

    /// <summary>Debug and tests: open everything research could unlock (no research effects are applied).</summary>
    public void UnlockAll()
    {
        foreach (var x in _lockable.ToList()) Unlock(x.Kind, x.Id);
    }

    /// <summary>Forget every unlock (lockables stay). Used before re-applying completed research on load.</summary>
    public void Reset() => _unlocked.Clear();
}
