using System;
using System.Collections.Generic;

namespace LittleLich.UI.Dev;

/// <summary>Event log categories. Later phases add their own (Level, Research, Battle...).</summary>
public enum LogCategory
{
    Tick,
    Pool,
    Lich,
    Save,
    Content,
    Command,
    Error,
}

public sealed record DevLogEntry(DateTime Time, LogCategory Category, string Text);

/// <summary>
/// The dev UI's event log. UI-only: panels and the shell push entries from manager signals;
/// the console shows them with category filters. Nothing in the simulation reads this.
/// </summary>
public static class DevLog
{
    public const int MaxEntries = 2000;

    private static readonly List<DevLogEntry> _entries = new();

    public static IReadOnlyList<DevLogEntry> Entries => _entries;

    public static event Action<DevLogEntry>? Added;
    public static event Action? Cleared;

    public static void Add(LogCategory category, string text)
    {
        var e = new DevLogEntry(DateTime.Now, category, text);
        _entries.Add(e);
        if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
        Added?.Invoke(e);
    }

    public static void Error(string text) => Add(LogCategory.Error, text);

    public static void Clear()
    {
        _entries.Clear();
        Cleared?.Invoke();
    }
}
