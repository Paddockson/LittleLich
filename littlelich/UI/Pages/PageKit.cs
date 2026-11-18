using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Crypt.Buildings;
using LittleLich.Managers;
using LittleLich.UI.Components;
using LittleLich.UI.Shell;
using LittleLich.UI.Theme;

namespace LittleLich.UI.Pages;

/// <summary>
/// Base for pages: a root control, a list of updaters that redraw numbers in place, and a rebuild hook for when
/// the structure changes (selection, level up, load). Updates are skipped while the page is hidden.
/// </summary>
public abstract partial class PageBase : Control, IPage
{
    protected readonly List<Action> Updaters = new();
    private bool _dirty;

    protected PageBase()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public Control Root => this;

    public virtual void Bind()
    {
        GameManager.Instance.GameStarted += (_, _) => Rebuild();
        // Hand actions (button presses) change state outside a tick; redraw once next frame.
        CryptManager.Instance.StockChanged += (_, _) => MarkDirty();
        CryptManager.Instance.InventoryChanged += (_, _) => MarkDirty();
        LichManager.Instance.NecromancerPoolChanged += (_, _, _) => MarkDirty();
    }

    public virtual void Refresh()
    {
        if (!IsVisibleInTree() && GetParent() is not null) return;
        if (Updaters.Count == 0) Rebuild();
        else foreach (var u in Updaters) u();
    }

    protected void MarkDirty() => _dirty = true;

    private bool _rebuildSoon;

    /// <summary>Rebuild once on the next frame (many events in a row, e.g. research unlocks while a save loads).</summary>
    protected void RebuildSoon() => _rebuildSoon = true;

    public override void _Process(double delta)
    {
        if (_rebuildSoon)
        {
            _rebuildSoon = false;
            _dirty = false;
            Rebuild();
            return;
        }
        if (!_dirty) return;
        _dirty = false;
        if (IsVisibleInTree()) foreach (var u in Updaters) u();
    }

    /// <summary>Clear the page and build it again from state.</summary>
    protected void Rebuild()
    {
        foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        Updaters.Clear();
        Build();
        foreach (var u in Updaters) u();
    }

    protected abstract void Build();

    /// <summary>Register an updater and run nothing yet (Rebuild runs them all at the end).</summary>
    protected void Update(Action a) => Updaters.Add(a);

    /// <summary>Fills the page with a horizontal row of cards (14 px gap).</summary>
    protected HBoxContainer CardRow()
    {
        var row = Ui.Row(Tokens.CardGap);
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(row);
        return row;
    }

    /// <summary>A card that fills its column vertically, with a scroll area for long content.</summary>
    protected static (Card Card, VBoxContainer Body) ScrollCard(float width = 0, bool expand = false, int gap = 10)
    {
        var card = new Card(gap) { SizeFlagsVertical = SizeFlags.ExpandFill };
        if (width > 0) card.CustomMinimumSize = new Vector2(width, 0);
        if (expand) card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return (card, card.Body);
    }

    protected static void Navigate(string key) => GameShell.Instance?.Navigate(key);
}

/// <summary>Shared lookups and text for pages.</summary>
public static class Kit
{
    public static ContentDatabase Db => GameManager.Instance.Content;
    public static CryptManager CryptMgr => CryptManager.Instance;
    public static LichManager Lich => LichManager.Instance;

    public static string Name(string? id) => id is null ? "?" : StockIds.NameOf(Db, id);

    /// <summary>Owned count of a resource or item.</summary>
    public static long Owned(string id) => CryptMgr.Stockpile.Get(id);

    /// <summary>"40 mana · 12 bones".</summary>
    public static string Costs(IEnumerable<ItemAmount> xs) => string.Join(" · ", xs.Select(c => $"{Fmt.Short(c.Count)} {Name(c.Id).ToLowerInvariant()}"));

    /// <summary>"4.2k gold" for the gold part, plus other parts: "150 gold · 100 mana".</summary>
    public static string CostText(IEnumerable<ItemAmount> xs) => Costs(xs);

    public static bool CanAfford(IEnumerable<ItemAmount> xs) => xs.All(c => Owned(c.Id) >= c.Count);

    /// <summary>Seconds a job of <paramref name="work"/> takes at <paramref name="workPerTick"/>.</summary>
    public static string Seconds(double work, double workPerTick) =>
        workPerTick <= 0 ? "—" : FormatTime(work / workPerTick * OfflineTime.BaseTickSeconds);

    public static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds)) return "—";
        if (seconds < 60) return $"{Fmt.Num1(seconds)}s";
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}m {t.Seconds:00}s";
    }

    public static (string Text, PillKind Kind) StatusPill(BuildingBase b) => b.Status switch
    {
        BuildingStatus.Running => ("RUNNING", PillKind.Running),
        BuildingStatus.Passive => ("PASSIVE", PillKind.Neutral),
        BuildingStatus.MissingInput => ("NEEDS INPUT", PillKind.Warning),
        BuildingStatus.OutputFull => ("FULL", PillKind.Warning),
        BuildingStatus.PoolFull => (b.StatusDetail == "target" ? "TARGET MET" : "POOL FULL", PillKind.Warning),
        BuildingStatus.NothingToDo => ("IDLE", PillKind.Warning),
        _ => ("IDLE", PillKind.Neutral),
    };

    /// <summary>Green when running or passive, yellow when it needs something, grey when idle.</summary>
    public static Color StatusColor(BuildingBase b) => b.Status switch
    {
        BuildingStatus.Running or BuildingStatus.Passive => Tokens.Green,
        BuildingStatus.Idle => Tokens.DotOff,
        _ => Tokens.Yellow,
    };

    public static string StatusText(BuildingBase b) => b.Status switch
    {
        BuildingStatus.Running => "Running",
        BuildingStatus.Idle => b.WorkPerTick > 0 ? "Idle, waiting for the next tick" : "Idle: add necromancers",
        BuildingStatus.MissingInput => $"Paused: missing {Name(b.StatusDetail)}",
        BuildingStatus.OutputFull => $"Paused: {Name(b.StatusDetail)} is full",
        BuildingStatus.NothingToDo => b is UndeadMarket ? "Nothing to sell" : "No recipe chosen",
        BuildingStatus.PoolFull => b.StatusDetail == "target" ? "Paused: target reached" : "Paused: necromancer pool is at max",
        BuildingStatus.Passive => "Passive",
        _ => b.Status.ToString(),
    };

    /// <summary>A label + value row (stat lines in cards): "Mana stored ........ 142k / 144k".</summary>
    public static (HBoxContainer Row, Label Value) KeyValue(string key, Color? valueColor = null, int size = Tokens.FontSmall)
    {
        var v = Ui.Bold("", size, valueColor ?? Tokens.TextMain);
        var row = Ui.Row(8, Ui.Text(key, size, Tokens.TextMuted), Ui.Fill(), v);
        return (row, v);
    }

    /// <summary>A Stepper for a building's staff (Free pool limits +).</summary>
    public static Stepper StaffStepper(BuildingBase b) =>
        new(() => b.Staffed, n => b.AddNecromancer(Clamp(n, Lich.State.Pool.Free)), n => b.RemoveNecromancer(Clamp(n, b.Staffed)))
        { Free = () => Math.Min(Lich.State.Pool.Free, b.FreeSlots) };

    /// <summary>Icon square with a dot (blocky stand-in for art).</summary>
    public static PanelContainer Icon(Color color, float size = 34)
    {
        var p = new PanelContainer { CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgPanelDark, Tokens.Border, Tokens.RadiusButton));
        var c = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        c.AddChild(Ui.Dot(color, size * 0.42f));
        p.AddChild(c);
        return p;
    }

    public static long Clamp(long step, long max) => Math.Max(0, Math.Min(step, max));
}
