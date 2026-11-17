using System;

namespace LittleLich.UI.Dev;

/// <summary>What a message is about (kept so callers say where a message comes from).</summary>
public enum LogCategory
{
    Tick,
    Pool,
    Lich,
    Save,
    Content,
    Level,
    Skill,
    Workshop,
    Research,
    Battle,
    Command,
    Error,
}

/// <summary>
/// Messages from panels to the shell's status line (the console and event log were removed). UI-only:
/// panels report what an action did or why it was refused; nothing in the simulation reads this.
/// </summary>
public static class DevLog
{
    /// <summary>(text, is error). The shell shows the latest one under the top bar.</summary>
    public static event Action<string, bool>? Posted;

    public static void Add(LogCategory category, string text) => Posted?.Invoke(text, category == LogCategory.Error);

    public static void Error(string text) => Posted?.Invoke(text, true);
}
