using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content;
using LittleLich.Core.Saves;

namespace LittleLich.Core;

/// <summary>How the current game was started.</summary>
public enum SessionStart { None, NewGame, Loaded, LoadedFromBackup }

/// <summary>
/// The plain-C# heart of the GameManager: owns the content database, the save slots and the list of
/// <see cref="ISaveable"/>s, and works out time away on load. No Godot types, so it is fully testable.
/// </summary>
public sealed class GameSession
{
    private readonly List<ISaveable> _saveables = new();
    private readonly Func<DateTime> _utcNow;
    private long _ticksSinceAutosave;

    public GameSession(ContentDatabase content, SaveService saves, Func<DateTime>? utcNow = null)
    {
        Content = content;
        Saves = saves;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        Register(Meta);
    }

    public ContentDatabase Content { get; }
    public SaveService Saves { get; }
    public GameMeta Meta { get; } = new();

    public int? CurrentSlot { get; private set; }
    public SessionStart StartKind { get; private set; }

    /// <summary>Why the main save failed to load, when the backup was used.</summary>
    public string? LastLoadWarning { get; private set; }

    /// <summary>Seconds the player was away, from the last load. Consumed by the TickManager (phase 3).</summary>
    public double PendingOfflineSeconds { get; private set; }

    /// <summary>Ticks to autosave after. Set by the TickManager in phase 3 (default: every 30 ticks = 60 s).</summary>
    public long AutosaveIntervalTicks { get; set; } = 30;

    public event Action<int>? Saved;
    public event Action<int, SessionStart>? Started;
    public event Action<double>? OfflineTimeComputed;

    public IReadOnlyList<ISaveable> Saveables => _saveables;

    /// <summary>Adds a saveable. Keys must be unique. Managers register in boot order.</summary>
    public void Register(ISaveable saveable)
    {
        if (_saveables.Any(s => s.SaveKey == saveable.SaveKey))
            throw new InvalidOperationException($"Save key '{saveable.SaveKey}' is already registered.");
        _saveables.Add(saveable);
    }

    /// <summary>
    /// Startup: load the most recently saved readable slot, or start a new game in slot 1 if there are none.
    /// </summary>
    public SessionStart StartFromSaves()
    {
        var latest = Saves.ListSlots()
            .Where(s => s.Readable)
            .OrderByDescending(s => s.SavedAtUtc)
            .FirstOrDefault();

        if (latest is not null)
        {
            try
            {
                return LoadSlot(latest.Slot);
            }
            catch (SaveLoadException)
            {
                // Became unreadable between listing and loading; fall through to a new game in a free slot.
            }
        }
        // Never overwrite an unreadable slot: the player may want to recover it by hand.
        return NewGame(Saves.NextFreeSlot());
    }

    /// <summary>Resets every saveable to defaults and writes the new save immediately.</summary>
    public SessionStart NewGame(int slot)
    {
        var empty = new SaveData();
        foreach (var s in _saveables) s.Load(empty);
        Meta.StartNew(_utcNow());

        CurrentSlot = slot;
        StartKind = SessionStart.NewGame;
        LastLoadWarning = null;
        PendingOfflineSeconds = 0;
        _ticksSinceAutosave = 0;

        Save();
        Started?.Invoke(slot, StartKind);
        return StartKind;
    }

    /// <summary>Loads a slot (falling back to its backup) and computes time away.</summary>
    public SessionStart LoadSlot(int slot)
    {
        var result = Saves.Load(slot);
        foreach (var s in _saveables) s.Load(result.File.Section(s.SaveKey));

        CurrentSlot = slot;
        StartKind = result.FromBackup ? SessionStart.LoadedFromBackup : SessionStart.Loaded;
        LastLoadWarning = result.MainError;
        _ticksSinceAutosave = 0;

        PendingOfflineSeconds = OfflineTime.ElapsedSeconds(result.File.SavedAtUtc, _utcNow());
        OfflineTimeComputed?.Invoke(PendingOfflineSeconds);

        // Re-write immediately when the main file was corrupt so the slot is healthy again.
        if (result.FromBackup) Save();

        Started?.Invoke(slot, StartKind);
        return StartKind;
    }

    /// <summary>Hands the time away to the caller (the TickManager) exactly once.</summary>
    public double TakePendingOfflineSeconds()
    {
        var s = PendingOfflineSeconds;
        PendingOfflineSeconds = 0;
        return s;
    }

    /// <summary>Gathers every saveable's section and writes the current slot.</summary>
    public void Save()
    {
        if (CurrentSlot is not int slot)
            throw new InvalidOperationException("No game is loaded.");

        Meta.MarkSaved();
        var file = new SaveFile();
        foreach (var s in _saveables) file.Sections[s.SaveKey] = s.Save();
        Saves.Write(slot, file);
        _ticksSinceAutosave = 0;
        Saved?.Invoke(slot);
    }

    /// <summary>
    /// Called by the TickManager after each tick (or batch of ticks). Saves once
    /// <see cref="AutosaveIntervalTicks"/> have passed. Returns true if it saved.
    /// </summary>
    public bool NotifyTicksProcessed(long ticks)
    {
        if (CurrentSlot is null || ticks <= 0) return false;
        Meta.TotalTicks += ticks;
        _ticksSinceAutosave += ticks;
        if (AutosaveIntervalTicks <= 0 || _ticksSinceAutosave < AutosaveIntervalTicks) return false;
        Save();
        return true;
    }
}
