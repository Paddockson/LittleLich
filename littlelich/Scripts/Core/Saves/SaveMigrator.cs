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
    public const int CurrentVersion = 2;

    /// <summary>fromVersion -> step that upgrades a root from that version to fromVersion + 1.</summary>
    private static readonly Dictionary<int, Action<SaveData>> Steps = new()
    {
        [1] = V1ToV2,
    };

    /// <summary>
    /// Ids that were resources in v1 and are inventory materials from v2 (only mana, gold, souls and the four elements
    /// stay resources).
    /// </summary>
    public static readonly IReadOnlyList<string> V2MaterialIds = new[]
    {
        "copper_ore", "coal", "iron_ore", "raw_eel", "wolf_hide", "nightshade", "bones", "copper_bar", "iron_bar",
    };

    /// <summary>
    /// v1 -> v2: the currency "coins" is renamed "gold"; gathered and crafted materials move from storage into the
    /// inventory (added to any stacks there); "infused_mana" no longer exists (replaced by the four elements) and is dropped.
    /// </summary>
    private static void V1ToV2(SaveData root)
    {
        if (root.Node["sections"] is not System.Text.Json.Nodes.JsonObject sections) return;
        if (sections["storage"] is not System.Text.Json.Nodes.JsonObject storage ||
            storage["stock"] is not System.Text.Json.Nodes.JsonObject stock) return;

        if (stock["coins"] is { } coins)
        {
            stock.Remove("coins");
            stock["gold"] = (stock["gold"]?.GetValue<long>() ?? 0) + coins.GetValue<long>();
        }
        stock.Remove("infused_mana");

        if (sections["inventory"] is not System.Text.Json.Nodes.JsonObject inventory)
            sections["inventory"] = inventory = new System.Text.Json.Nodes.JsonObject();
        if (inventory["stacks"] is not System.Text.Json.Nodes.JsonObject stacks)
            inventory["stacks"] = stacks = new System.Text.Json.Nodes.JsonObject();
        foreach (var id in V2MaterialIds)
        {
            if (stock[id] is not { } n) continue;
            stock.Remove(id);
            stacks[id] = (stacks[id]?.GetValue<long>() ?? 0) + n.GetValue<long>();
        }
    }

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
