using System;
using System.Collections.Generic;
using Godot;
using LittleLich.Content;
using LittleLich.Core;
using LittleLich.Core.Saves;

namespace LittleLich.Managers;

/// <summary>
/// Autoload singleton (first in the autoload list). Boots the game:
///   1. Load content JSON from res://Data into the ContentDatabase.
///   2. Find saves in user://saves; load the latest, or create a new game.
///   3. Create/load LichManager, CryptManager, TickManager state (registered as saveables in later phases).
///   4. Compute time away and hold it for the TickManager (phase 3).
///   5. The main scene (UI/Main.tscn) shows the shell.
///
/// All game state lives in <see cref="GameSession"/> (plain C#). This node only adapts it to Godot:
/// file paths, signals for the UI, and save-on-quit.
/// </summary>
public partial class GameManager : Node
{
    public const string ContentRoot = "res://Data";
    public const string SaveRoot = "user://saves";

    public static GameManager Instance { get; private set; } = null!;

    [Signal] public delegate void BootedEventHandler();
    [Signal] public delegate void GameSavedEventHandler(int slot);

    public GameSession? Session { get; private set; }
    public ContentDatabase Content => Session?.Content ?? ContentDatabase.Empty;

    /// <summary>Set if boot failed (e.g. bad content). The shell shows it instead of crashing.</summary>
    public string? BootError { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        Boot();
    }

    private void Boot()
    {
        // 1. Content
        ContentDatabase content;
        try
        {
            content = ContentLoader.CreateDefault().Load(ReadContentSources(ContentRoot));
            GD.Print($"[GameManager] Loaded {content.Count} content definitions.");
        }
        catch (ContentLoadException e)
        {
            Fail(e.Message);
            return;
        }

        // 2. Saves
        var saves = new SaveService(ProjectSettings.GlobalizePath(SaveRoot));
        Session = new GameSession(content, saves);
        Session.Saved += slot => EmitSignal(SignalName.GameSaved, slot);

        // 3. Later phases register manager state here, in boot order, before the save is read:
        //    Session.Register(LichManager.Instance); Session.Register(CryptManager.Instance); ...

        try
        {
            var start = Session.StartFromSaves();
            GD.Print($"[GameManager] {start} in slot {Session.CurrentSlot}.");
            if (Session.LastLoadWarning is { } w)
                GD.PushWarning($"[GameManager] Main save was unreadable ({w}); loaded the backup instead.");
        }
        catch (Exception e) when (e is SaveLoadException or System.IO.IOException or UnauthorizedAccessException)
        {
            Fail("Could not load or create a save: " + e.Message);
            return;
        }

        // 4. Time away. TickManager (phase 3) will call Session.TakePendingOfflineSeconds().
        if (Session.PendingOfflineSeconds > 0)
            GD.Print($"[GameManager] Away for {Session.PendingOfflineSeconds:0}s " +
                     $"({OfflineTime.ToTicks(Session.PendingOfflineSeconds)} ticks pending).");

        EmitSignal(SignalName.Booted);
    }

    private void Fail(string message)
    {
        BootError = message;
        GD.PushError("[GameManager] " + message);
        EmitSignal(SignalName.Booted);
    }

    // ---- Commands (called by UI and other managers) ----

    public void SaveNow()
    {
        if (Session?.CurrentSlot is null) return;
        try { Session.Save(); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PushError("[GameManager] Save failed: " + e.Message);
        }
    }

    public void NewGame(int slot) => Session?.NewGame(slot);
    public void LoadSlot(int slot) => Session?.LoadSlot(slot);
    public IReadOnlyList<SaveSlotInfo> ListSlots() => Session?.Saves.ListSlots() ?? Array.Empty<SaveSlotInfo>();

    /// <summary>Autosave trigger: the TickManager calls this after each tick or batch (phase 3).</summary>
    public void OnTicksProcessed(long ticks)
    {
        try { Session?.NotifyTicksProcessed(ticks); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PushError("[GameManager] Autosave failed: " + e.Message);
        }
    }

    private bool _savedOnQuit;

    /// <summary>Save on quit: window close, or any other shutdown (GetTree().Quit(), --quit-after).</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) SaveOnQuit();
    }

    public override void _ExitTree() => SaveOnQuit();

    private void SaveOnQuit()
    {
        if (_savedOnQuit) return;
        _savedOnQuit = true;
        SaveNow();
    }

    /// <summary>Reads every *.json under a res:// folder through Godot's file API (works in exported builds).</summary>
    private static IEnumerable<ContentSource> ReadContentSources(string root)
    {
        var files = new List<string>();
        CollectJson(root, files);
        files.Sort(StringComparer.Ordinal);
        foreach (var path in files)
        {
            var text = FileAccess.GetFileAsString(path);
            if (text.Length == 0 && FileAccess.GetOpenError() != Error.Ok)
                throw new ContentLoadException(new[] { new ContentError(path, null, null, $"cannot read file ({FileAccess.GetOpenError()})") });
            yield return new ContentSource(path.Substring(root.Length + 1), text);
        }
    }

    private static void CollectJson(string dir, List<string> into)
    {
        foreach (var f in DirAccess.GetFilesAt(dir))
            if (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) into.Add($"{dir}/{f}");
        foreach (var d in DirAccess.GetDirectoriesAt(dir))
            CollectJson($"{dir}/{d}", into);
    }
}
