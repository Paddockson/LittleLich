using System;

namespace LittleLich.Core;

/// <summary>Save-wide bookkeeping (section "meta"). Lets phase 1 prove save/load round trips before any manager exists.</summary>
public sealed class GameMeta : ISaveable
{
    public string SaveKey => "meta";

    public DateTime CreatedAtUtc { get; private set; }
    public long SaveCount { get; private set; }

    /// <summary>Total ticks processed in this save (online and offline). Advanced by the TickManager from phase 3.</summary>
    public long TotalTicks { get; set; }

    public void StartNew(DateTime nowUtc)
    {
        CreatedAtUtc = nowUtc;
        SaveCount = 0;
        TotalTicks = 0;
    }

    internal void MarkSaved() => SaveCount++;

    public SaveData Save() => new SaveData()
        .Set("created_at_utc", CreatedAtUtc)
        .Set("save_count", SaveCount)
        .Set("total_ticks", TotalTicks);

    public void Load(SaveData data)
    {
        CreatedAtUtc = DateTime.SpecifyKind(data.Get("created_at_utc", DateTime.UnixEpoch), DateTimeKind.Utc);
        SaveCount = data.Get("save_count", 0L);
        TotalTicks = data.Get("total_ticks", 0L);
    }
}
