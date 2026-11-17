using System;
using System.Collections.Generic;

namespace LittleLich.Core.Saves;

/// <summary>
/// Upgrades old save files step by step to <see cref="CurrentVersion"/>.
///
/// To change the save format:
///   1. Bump <see cref="CurrentVersion"/>.
///   2. Add a step for the old version that edits the root in place, e.g.
///      Steps[1] = root => { /* v1 -> v2 */ };
/// Steps only touch ids/counts/fields in the JSON; definitions are never in saves.
/// </summary>
public static class SaveMigrator
{
    public const int CurrentVersion = 1;

    /// <summary>fromVersion -> step that upgrades a root from that version to fromVersion + 1.</summary>
    private static readonly Dictionary<int, Action<SaveData>> Steps = new()
    {
        // No migrations yet: version 1 is the first format.
    };

    /// <summary>
    /// Migrates <paramref name="data"/> (the whole save root) from <paramref name="fromVersion"/>
    /// to <see cref="CurrentVersion"/> and updates its version field.
    /// </summary>
    public static void Migrate(SaveData data, int fromVersion) =>
        Migrate(data, fromVersion, CurrentVersion, Steps);

    /// <summary>Testable core: run <paramref name="steps"/> from <paramref name="fromVersion"/> up to <paramref name="toVersion"/>.</summary>
    public static void Migrate(SaveData data, int fromVersion, int toVersion, IReadOnlyDictionary<int, Action<SaveData>> steps)
    {
        if (fromVersion > toVersion)
            throw new SaveFormatException($"save version {fromVersion} is newer than this game supports ({toVersion})");

        for (int v = fromVersion; v < toVersion; v++)
        {
            if (!steps.TryGetValue(v, out var step))
                throw new SaveFormatException($"no migration from save version {v} to {v + 1}");
            step(data);
            data.Node[SaveFile.VersionKey] = v + 1;
        }
    }
}
