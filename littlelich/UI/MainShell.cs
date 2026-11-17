using System;
using System.Linq;
using System.Text;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Managers;

namespace LittleLich.UI;

/// <summary>
/// Phase 1 placeholder shell. Reads GameManager state and sends commands; never changes state itself.
/// Replaced by the real top bar and screens in later phases.
/// </summary>
public partial class MainShell : Control
{
    private Label _status = null!;
    private Label _slots = null!;

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 32);
        AddChild(margin);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        margin.AddChild(col);

        col.AddChild(new Label { Text = "Little Lich — v0.1 shell", ThemeTypeVariation = "HeaderLarge" });
        _status = new Label();
        col.AddChild(_status);

        var buttons = new HBoxContainer();
        col.AddChild(buttons);
        var save = new Button { Text = "Save now" };
        save.Pressed += () => GameManager.Instance.SaveNow();
        buttons.AddChild(save);
        var newGame = new Button { Text = "New game (next free slot)" };
        newGame.Pressed += () =>
        {
            var gm = GameManager.Instance;
            if (gm.Session is { } s) gm.NewGame(s.Saves.NextFreeSlot());
            Refresh();
        };
        buttons.AddChild(newGame);

        col.AddChild(new Label { Text = "Save slots" });
        _slots = new Label();
        col.AddChild(_slots);

        var gm = GameManager.Instance;
        gm.GameSaved += _ => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var gm = GameManager.Instance;
        var sb = new StringBuilder();

        if (gm.BootError is { } err)
        {
            sb.AppendLine("BOOT ERROR");
            sb.AppendLine(err);
            _status.Text = sb.ToString();
            _slots.Text = "";
            return;
        }

        var s = gm.Session!;
        var resources = gm.Content.All<ResourceDefinitionBase>().Select(r => r.Name);
        sb.AppendLine($"Content definitions: {gm.Content.Count} (resources: {string.Join(", ", resources)})");
        sb.AppendLine($"Slot {s.CurrentSlot} — {s.StartKind}");
        if (s.LastLoadWarning is { } w) sb.AppendLine($"Recovered from backup: {w}");
        sb.AppendLine($"Created {s.Meta.CreatedAtUtc.ToLocalTime():g}, saved {s.Meta.SaveCount} times");
        sb.AppendLine($"Time away: {TimeSpan.FromSeconds(Math.Floor(s.PendingOfflineSeconds))}");
        _status.Text = sb.ToString();

        _slots.Text = string.Join("\n", gm.ListSlots().Select(i =>
            $"  slot {i.Slot}: " +
            (i.Readable ? $"saved {i.SavedAtUtc?.ToLocalTime():g}, v{i.Version}" : "UNREADABLE") +
            (i.MainCorrupt ? " (main corrupt, using backup)" : "") +
            (i.HasBackup ? " [backup]" : "")));
    }
}
