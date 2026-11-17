using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Core;

/// <summary>
/// What research has unlocked. Content that any research effect unlocks is "lockable" and stays locked until
/// that effect is applied; everything else is open. Also holds research bonuses that other systems read
/// (extra building levels). Rebuilt from completed research on load, so it is not saved itself.
/// </summary>
public sealed class Unlocks
{
    public const string Unit = "unit";
    public const string Recipe = "recipe";
    public const string Activity = "activity";
    public const string Transmutation = "transmutation";
    public const string Summon = "summon";

    private readonly HashSet<(string Kind, string Id)> _lockable = new();
    private readonly HashSet<(string Kind, string Id)> _unlocked = new();
    private readonly Dictionary<string, int> _buildingLevels = new(StringComparer.Ordinal);

    /// <summary>Nothing lockable: everything is open (tests and systems without research).</summary>
    public static Unlocks None => new();

    /// <summary>(kind, id) became available.</summary>
    public event Action<string, string>? Unlocked;
    /// <summary>Building level bonuses changed.</summary>
    public event Action? BonusesChanged;

    public void SetLockable(string kind, string id) => _lockable.Add((kind, id));

    public bool IsLockable(string kind, string id) => _lockable.Contains((kind, id));

    public bool IsLocked(string kind, string id) => _lockable.Contains((kind, id)) && !_unlocked.Contains((kind, id));

    /// <summary>Returns true if it was newly unlocked.</summary>
    public bool Unlock(string kind, string id)
    {
        if (!_unlocked.Add((kind, id))) return false;
        Unlocked?.Invoke(kind, id);
        return true;
    }

    public IEnumerable<string> UnlockedOf(string kind) => _unlocked.Where(u => u.Kind == kind).Select(u => u.Id);

    /// <summary>Extra max levels research has given a building.</summary>
    public int BuildingLevelBonus(string buildingId) => _buildingLevels.GetValueOrDefault(buildingId);

    public void AddBuildingLevels(string buildingId, int levels)
    {
        if (levels == 0) return;
        _buildingLevels[buildingId] = BuildingLevelBonus(buildingId) + levels;
        BonusesChanged?.Invoke();
    }

    /// <summary>Forget every unlock and bonus (lockables stay). Used before re-applying completed research on load.</summary>
    public void Reset()
    {
        _unlocked.Clear();
        var hadBonus = _buildingLevels.Count > 0;
        _buildingLevels.Clear();
        if (hadBonus) BonusesChanged?.Invoke();
    }
}
