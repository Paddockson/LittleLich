using System;
using Godot;
using LittleLich.Core;
using LittleLich.Managers;

namespace LittleLich.UI.Dev;

/// <summary>Phase 1 panel: current game, slot list and New / Save / Load / Delete.</summary>
public partial class SavePanel : VBoxContainer, IDevPanel
{
    public string Title => "Saves";
    public Control Root => this;

    private readonly Label _current;
    private readonly Tree _slots;
    private readonly ConfirmationDialog _confirm;
    private Action? _onConfirm;

    public SavePanel()
    {
        AddThemeConstantOverride("separation", 8);
        _current = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_current);

        var buttons = new HFlowContainer();
        AddChild(buttons);
        Btn(buttons, "Save now", () => GameManager.Instance.SaveNow());
        Btn(buttons, "New game (next free slot)", () =>
        {
            if (GameManager.Instance.Session is { } s) GameManager.Instance.NewGame(s.Saves.NextFreeSlot());
        });
        Btn(buttons, "Load selected", () =>
        {
            if (Selected() is int slot) Run($"Load slot {slot}", () => GameManager.Instance.LoadSlot(slot));
        });
        Btn(buttons, "New game over selected", () =>
        {
            if (Selected() is int slot)
                Confirm($"Start a new game in slot {slot}? The current save there becomes its backup.",
                    () => Run($"New game in slot {slot}", () => GameManager.Instance.NewGame(slot)));
        });
        Btn(buttons, "Delete selected", () =>
        {
            if (Selected() is int slot)
                Confirm($"Delete slot {slot} and its backup? This cannot be undone.",
                    () => Run($"Delete slot {slot}", () => GameManager.Instance.DeleteSlot(slot)));
        });
        Btn(buttons, "Refresh", Refresh);

        _slots = new Tree
        {
            Columns = 4,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 200),
            SelectMode = Tree.SelectModeEnum.Row,
        };
        string[] titles = { "Slot", "Saved", "Version", "State" };
        for (var i = 0; i < titles.Length; i++) _slots.SetColumnTitle(i, titles[i]);
        AddChild(_slots);

        _confirm = new ConfirmationDialog();
        _confirm.Confirmed += () => _onConfirm?.Invoke();
        AddChild(_confirm);
    }

    public void Bind()
    {
        GameManager.Instance.GameSaved += _ => Refresh();
        GameManager.Instance.GameStarted += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var gm = GameManager.Instance;
        if (gm.Session is not { } s)
        {
            _current.Text = "No game loaded" + (gm.BootError is null ? "." : ": boot failed (see log).");
            _slots.Clear();
            return;
        }

        _current.Text =
            $"Current: slot {s.CurrentSlot} ({s.StartKind})   created {s.Meta.CreatedAtUtc.ToLocalTime():g}   " +
            $"saved {s.Meta.SaveCount} times   total ticks {DevFormat.Exact(s.Meta.TotalTicks)}   " +
            $"autosave every {s.AutosaveIntervalTicks} ticks" +
            (s.LastLoadWarning is { } w ? $"\nRecovered from backup: {w}" : "");

        var selected = Selected();
        _slots.Clear();
        var root = _slots.CreateItem();
        foreach (var i in gm.ListSlots())
        {
            var it = _slots.CreateItem(root);
            it.SetText(0, i.Slot == s.CurrentSlot ? $"{i.Slot} (current)" : i.Slot.ToString());
            it.SetMetadata(0, i.Slot);
            it.SetText(1, i.SavedAtUtc is { } t ? t.ToLocalTime().ToString("g") : "-");
            it.SetText(2, i.Version?.ToString() ?? "-");
            it.SetText(3, !i.Readable ? "UNREADABLE" : i.MainCorrupt ? "main corrupt, backup ok" : i.HasBackup ? "ok + backup" : "ok");
            if (i.Slot == selected) it.Select(0);
        }
    }

    private int? Selected() => _slots.GetSelected() is { } it ? it.GetMetadata(0).AsInt32() : null;

    private void Confirm(string text, Action then)
    {
        _confirm.DialogText = text;
        _onConfirm = then;
        _confirm.PopupCentered();
    }

    private void Run(string what, Action action)
    {
        try
        {
            action();
            DevLog.Add(LogCategory.Save, what + ": done.");
        }
        catch (Exception e)
        {
            DevLog.Error($"{what} failed: {e.Message}");
        }
        Refresh();
    }

    private static void Btn(Container parent, string text, Action press)
    {
        var b = new Button { Text = text };
        b.Pressed += press;
        parent.AddChild(b);
    }
}
