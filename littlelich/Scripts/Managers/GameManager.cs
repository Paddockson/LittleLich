using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content;
using LittleLich.Core;
using LittleLich.Core.Saves;

namespace LittleLich.Managers;

/// <summary>
/// Autoload singleton (first in the autoload list). Boots the game:
///   1. Load content JSON from res://Data into the ContentDatabase.
///   2. Find saves in user://saves; load the latest, or create a new game.
///   3. Create/load LichManager, CryptManager state (registered as saveables before the save is read).
///   4. Compute time away and hold it for the TickManager, which processes it as offline ticks.
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
    /// <summary>A game was started or loaded (slot, SessionStart as int).</summary>
    [Signal] public delegate void GameStartedEventHandler(int slot, int startKind);

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
        Session.Started += (slot, kind) => EmitSignal(SignalName.GameStarted, slot, (int)kind);

        // 3. Manager state registers here, in boot order, before the save is read.
        if (LichManager.Instance is null)
        {
            Fail("LichManager autoload is missing (check project.godot [autoload]).");
            return;
        }
        Session.Register(LichManager.Instance.State);
        //    Later: Session.Register(CryptManager.Instance.State); ...

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

        // 4. Time away. The TickManager takes it (Session.TakePendingOfflineSeconds) in its _Ready / on GameStarted
        //    and runs it as batched offline ticks, after every manager has registered its tick features.
        if (Session.PendingOfflineSeconds > 0)
            GD.Print($"[GameManager] Away for {Session.PendingOfflineSeconds:0}s " +
                     $"({OfflineTime.ToTicks(Session.PendingOfflineSeconds)} ticks pending).");

        RunDebugArgs();
        EmitSignal(SignalName.Booted);
    }

    /// <summary>
    /// Debug commands after "--" on the command line:
    ///   --add-necromancers=N            add N necromancers to the pool (raises Max if needed)
    ///   --assign=owner:activity:N       assign N necromancers
    ///   --unassign=owner:activity:N     unassign N necromancers
    /// Prints the lich pool afterwards. Used for headless save/reload checks.
    /// </summary>
    private static void RunDebugArgs()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Length == 0) return;
        var lich = LichManager.Instance;
        var used = false;
        foreach (var arg in args)
        {
            var eq = arg.IndexOf('=');
            if (eq < 0) continue;
            var name = arg[..eq];
            var value = arg[(eq + 1)..];
            switch (name)
            {
                case "--add-necromancers" when long.TryParse(value, out var n):
                    used = true;
                    lich.DebugAddNecromancers(n);
                    break;
                case "--assign" or "--unassign":
                    var parts = value.Split(':');
                    if (parts.Length != 3 || !long.TryParse(parts[2], out var c)) break;
                    used = true;
                    var done = name == "--assign" ? lich.TryAssign(parts[0], parts[1], c) : lich.Unassign(parts[0], parts[1], c);
                    GD.Print($"[GameManager] {name[2..]} {parts[0]}/{parts[1]}: {done} of {c}");
                    break;
            }
        }
        if (!used) return;
        var p = lich.State.Pool;
        GD.Print($"[LichManager] Pool total={p.Total} free={p.Free} assigned={p.Assigned} max={p.Max}; " +
                 string.Join(", ", lich.AllAssignments().Select(a => $"{a.OwnerId}/{a.ActivityId}={a.Count}")));
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

    /// <summary>Deletes another slot (not the current one). Throws InvalidOperationException for the current slot.</summary>
    public bool DeleteSlot(int slot) => Session?.DeleteSlot(slot) ?? false;

    /// <summary>Total ticks + autosave check: the TickManager calls this after every tick batch (tick step 7).</summary>
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
