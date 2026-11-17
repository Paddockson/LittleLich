using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Core.Lich;
using LittleLich.Managers;

namespace LittleLich.UI.Dev;

/// <summary>Phase 2 panel: health, stats, modifiers, the necromancer pool and the assignments table.</summary>
public partial class LichPanel : ScrollContainer, IDevPanel
{
    public string Title => "Lich";
    public Control Root => this;

    private Label _health = null!;
    private ProgressBar _healthBar = null!;
    private Label _pool = null!;
    private Label _tick = null!;
    private VBoxContainer _rows = null!;
    private Label _noRows = null!;
    private LineEdit _owner = null!;
    private LineEdit _activity = null!;
    private Tree _stats = null!;
    private Tree _mods = null!;
    private readonly Dictionary<(string, string), AssignmentRow> _rowByKey = new();

    private static LichState State => LichManager.Instance.State;

    public LichPanel()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 8);
        AddChild(col);

        // Health
        col.AddChild(Header("Health"));
        var hrow = new HBoxContainer();
        col.AddChild(hrow);
        _healthBar = new ProgressBar { CustomMinimumSize = new Vector2(240, 0), ShowPercentage = false, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        hrow.AddChild(_healthBar);
        _health = new Label();
        hrow.AddChild(_health);
        _tick = new Label();
        col.AddChild(_tick);

        // Necromancer pool
        col.AddChild(Header("Necromancer pool"));
        _pool = new Label();
        col.AddChild(_pool);
        var debug = new HFlowContainer();
        col.AddChild(debug);
        debug.AddChild(new Label { Text = "Debug add:" });
        foreach (var (text, n) in new[] { ("+1", 1L), ("+10", 10L), ("+100", 100L), ("+1K", 1_000L), ("+1M", 1_000_000L), ("+1B", 1_000_000_000L) })
        {
            var b = new Button { Text = text, TooltipText = "Adds to the free pool, raising Max if needed (debug modifier)." };
            b.Pressed += () => LichManager.Instance.DebugAddNecromancers(n);
            debug.AddChild(b);
        }

        // Assignments
        col.AddChild(Header("Assignments"));
        _rows = new VBoxContainer();
        col.AddChild(_rows);
        _noRows = new Label { Text = "No assignments." };
        _rows.AddChild(_noRows);
        var form = new HFlowContainer();
        col.AddChild(form);
        form.AddChild(new Label { Text = "New:" });
        _owner = new LineEdit { PlaceholderText = "owner (mining)", CustomMinimumSize = new Vector2(140, 0) };
        _activity = new LineEdit { PlaceholderText = "activity (iron_ore)", CustomMinimumSize = new Vector2(140, 0) };
        form.AddChild(_owner);
        form.AddChild(_activity);
        var assign = new Button { Text = "Assign 1", TooltipText = "Until skills exist (phase 4), any owner/activity pair can be assigned." };
        assign.Pressed += AssignFromForm;
        form.AddChild(assign);

        // Stats
        col.AddChild(Header("Stats"));
        _stats = MakeTree(new[] { "Stat", "Base", "Value" }, 130);
        col.AddChild(_stats);

        // Modifiers
        col.AddChild(Header("Modifiers (add with console: mod <stat> <add|mul> <value> [ticks])"));
        _mods = MakeTree(new[] { "Stat", "Op", "Value", "Source", "Expires at tick" }, 160);
        col.AddChild(_mods);
        var remove = new Button { Text = "Remove all from selected source", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        remove.Pressed += () =>
        {
            if (_mods.GetSelected() is { } item)
            {
                var source = item.GetText(3);
                var n = LichManager.Instance.RemoveModifiersFrom(source);
                DevLog.Add(LogCategory.Lich, $"Removed {n} modifier(s) from '{source}'.");
                Refresh();
            }
        };
        col.AddChild(remove);
    }

    public void Bind()
    {
        var lm = LichManager.Instance;
        lm.NecromancerPoolChanged += (_, _, _) => RefreshPool();
        lm.AssignmentChanged += (_, _, _) => RefreshPool();
        lm.StatChanged += (_, _) => { RefreshStats(); RefreshHealth(); };
        lm.HealthChanged += (_, _) => RefreshHealth();
        // Lich tick and "ticks left" on timed modifiers move every tick.
        // Stats are only redrawn when a timed modifier is counting down (redrawing clears the table selection).
        TickManager.Instance.TickEnded += (_, _) =>
        {
            RefreshHealth();
            if (State.Stats.Modifiers.Any(m => m.IsTimed)) RefreshStats();
        };
        GameManager.Instance.GameStarted += (_, _) => Refresh();
    }

    public void Refresh()
    {
        RefreshHealth();
        RefreshPool();
        RefreshStats();
    }

    private void RefreshHealth()
    {
        var s = State;
        _healthBar.MaxValue = Math.Max(1, s.MaxHealth.ToDouble());
        _healthBar.Value = s.Health.ToDouble();
        _health.Text = $"{DevFormat.Exact(s.Health)} / {DevFormat.Exact(s.MaxHealth)}   (no regeneration in v0.1)" +
                       $"   Max Insight {DevFormat.Exact(s.MaxInsight)}";
        _tick.Text = $"Lich tick: {DevFormat.Exact(s.CurrentTick)}";
    }

    private void RefreshPool()
    {
        var p = State.Pool;
        _pool.Text = $"Total {DevFormat.Exact(p.Total)}   Free {DevFormat.Exact(p.Free)}   " +
                     $"Assigned {DevFormat.Exact(p.Assigned)}   Max {DevFormat.Exact(p.Max)}";

        // Keep one row per assignment; reuse rows so buttons don't flicker.
        var keys = State.AllAssignments().Select(a => (a.OwnerId, a.ActivityId)).ToList();
        foreach (var gone in _rowByKey.Keys.Except(keys).ToList())
        {
            _rowByKey[gone].QueueFree();
            _rowByKey.Remove(gone);
        }
        foreach (var k in keys)
        {
            if (_rowByKey.ContainsKey(k)) continue;
            var row = new AssignmentRow(k.OwnerId, k.ActivityId);
            _rowByKey[k] = row;
            _rows.AddChild(row);
        }
        // Keep rows in the pool's sorted order.
        for (var i = 0; i < keys.Count; i++) _rows.MoveChild(_rowByKey[keys[i]], i + 1);
        foreach (var r in _rowByKey.Values) r.Refresh();
        _noRows.Visible = keys.Count == 0;
    }

    private void RefreshStats()
    {
        var sheet = State.Stats;
        _stats.Clear();
        var root = _stats.CreateItem();
        foreach (var id in sheet.KnownStats.OrderBy(s => s, StringComparer.Ordinal))
        {
            var it = _stats.CreateItem(root);
            it.SetText(0, id);
            it.SetText(1, DevFormat.Exact(sheet.GetBase(id)));
            it.SetText(2, DevFormat.Exact(sheet.Get(id)));
        }

        _mods.Clear();
        root = _mods.CreateItem();
        foreach (var m in sheet.Modifiers)
        {
            var it = _mods.CreateItem(root);
            it.SetText(0, m.StatId);
            it.SetText(1, m.Op == ModifierOp.Add ? "add" : "mul");
            it.SetText(2, DevFormat.Exact(m.Value));
            it.SetText(3, m.Source);
            it.SetText(4, m.ExpiresAtTick is long t ? $"{DevFormat.Exact(t)} ({DevFormat.Exact(t - State.CurrentTick)} left)" : "never");
        }
    }

    private void AssignFromForm()
    {
        var owner = _owner.Text.Trim();
        var activity = _activity.Text.Trim();
        if (owner.Length == 0 || activity.Length == 0)
        {
            DevLog.Error("Assign: owner and activity are both required.");
            return;
        }
        try
        {
            if (DevAssign.Add(owner, activity, 1) == 0)
                DevLog.Error("Assign: nothing assigned (no free necromancers, or the activity is locked).");
        }
        catch (ArgumentException e)
        {
            DevLog.Error("Assign: " + e.Message);
        }
    }

    private static Label Header(string text) => new() { Text = text, ThemeTypeVariation = "HeaderSmall" };

    private static Tree MakeTree(string[] columns, float minHeight)
    {
        var t = new Tree
        {
            Columns = columns.Length,
            ColumnTitlesVisible = true,
            HideRoot = true,
            CustomMinimumSize = new Vector2(0, minHeight),
            SelectMode = Tree.SelectModeEnum.Row,
        };
        for (var i = 0; i < columns.Length; i++) t.SetColumnTitle(i, columns[i]);
        return t;
    }
}
