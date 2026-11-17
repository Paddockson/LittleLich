using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Crypt.Buildings;
using LittleLich.Managers;

namespace LittleLich.UI.Dev;

/// <summary>
/// Phase 5: the Workshop. A list of buildings with a status marker on the left (green running, yellow needs
/// something, grey idle/passive); the selected building's page on the right. Every page shows level and upgrade,
/// status, work per tick and staff; each building adds its own controls (Transmuter recipe, Summoning Circle
/// target, Market sell list...). The page is rebuilt on selection or level up and updated in place on events.
/// </summary>
public partial class WorkshopPanel : HSplitContainer, IDevPanel
{
    public string Title => "Workshop";
    public Control Root => this;

    private readonly ItemList _list;
    private readonly VBoxContainer _page;
    private readonly List<Action> _updaters = new();
    private BuildingBase? _selected;

    private static CryptManager Crypt => CryptManager.Instance;

    public WorkshopPanel()
    {
        _list = new ItemList { CustomMinimumSize = new Vector2(215, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        _list.ItemSelected += i => Select(Crypt.Buildings[(int)i]);
        AddChild(_list);
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        _page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _page.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_page);
    }

    public void Bind()
    {
        Crypt.BuildingStatusChanged += (_, _, _) => RefreshList();
        Crypt.BuildingLevelChanged += (id, _) => { RefreshList(); if (_selected?.Id == id) Build(); };
        TickManager.Instance.TickEnded += (_, _) => Update();
        Crypt.StockChanged += (_, _) => { if (!TickManager.Instance.Scheduler.IsRunning) Update(); };
        Crypt.InventoryChanged += (_, _) => { if (!TickManager.Instance.Scheduler.IsRunning) Update(); };
        LichManager.Instance.NecromancerPoolChanged += (_, _, _) => { if (!TickManager.Instance.Scheduler.IsRunning) Update(); };
        GameManager.Instance.GameStarted += (_, _) => { RefreshList(); Build(); };
    }

    public void Refresh()
    {
        RefreshList();
        if (_selected is null && Crypt.Buildings.Count > 0) Select(Crypt.Buildings[0]);
        else Update();
    }

    /// <summary>Selects a building by id (used by --dev-tab=&lt;building id&gt;).</summary>
    public bool SelectId(string id)
    {
        if (Crypt.Building(id) is not { } b) return false;
        Select(b);
        return true;
    }

    private void Select(BuildingBase b)
    {
        _selected = b;
        Build();
    }

    // ---- List ----

    private void RefreshList()
    {
        var buildings = Crypt.Buildings;
        if (_list.ItemCount != buildings.Count)
        {
            _list.Clear();
            foreach (var _ in buildings) _list.AddItem("");
        }
        for (var i = 0; i < buildings.Count; i++)
        {
            var b = buildings[i];
            var (mark, color) = b.Status switch
            {
                BuildingStatus.Running => ("●", new Color(0.45f, 0.9f, 0.45f)),
                BuildingStatus.MissingInput or BuildingStatus.OutputFull or BuildingStatus.PoolFull or BuildingStatus.NothingToDo
                    => ("●", new Color(0.95f, 0.8f, 0.3f)),
                _ => ("○", new Color(0.7f, 0.7f, 0.7f)),
            };
            _list.SetItemText(i, $"{mark} {b.Name}  Lv {b.Level}");
            _list.SetItemCustomFgColor(i, color);
            _list.SetItemTooltip(i, StatusText(b));
            if (b == _selected) _list.Select(i);
        }
    }

    // ---- Page ----

    private void Build()
    {
        foreach (var c in _page.GetChildren()) c.QueueFree();
        _updaters.Clear();
        if (_selected is not { } b) return;

        var title = Label("", "HeaderMedium");
        var status = Label("");
        var work = Label("");
        _updaters.Add(() =>
        {
            title.Text = $"{b.Name}   level {b.Level}{(b.IsMaxLevel ? " (max)" : "")}";
            status.Text = StatusText(b);
            status.Modulate = b.Status is BuildingStatus.MissingInput or BuildingStatus.OutputFull or BuildingStatus.PoolFull
                ? new Color(1f, 0.65f, 0.4f) : Colors.White;
            work.Text = b.Ticks
                ? $"Work {Num(b.WorkPerTick)}/tick = ({Num(b.Definition.BaseWork)} base + {DevFormat.Exact(Math.Min(b.Staffed, b.Slots))} staff x " +
                  $"{Num(b.Definition.WorkPerNecromancer)}) x {Num(b.LevelMultiplier)} level bonus"
                : "No work: this building does not tick.";
        });

        var up = new HBoxContainer();
        var upgrade = new Button();
        upgrade.Pressed += () =>
        {
            if (!Crypt.TryUpgrade(b.Id)) DevLog.Error($"Cannot upgrade {b.Name}: need {DevFormat.Exact(b.UpgradeCost)} coins.");
        };
        up.AddChild(upgrade);
        var coins = Label("", wrap: false);
        up.AddChild(coins);
        _updaters.Add(() =>
        {
            upgrade.Text = b.IsMaxLevel ? "Max level" : $"Upgrade to {b.Level + 1} ({DevFormat.Exact(b.UpgradeCost)} coins)";
            upgrade.Disabled = b.IsMaxLevel || Crypt.Storage.Get("coins") < b.UpgradeCost;
            coins.Text = $"Coins {DevFormat.Exact(Crypt.Storage.Get("coins"))}";
        });

        _page.AddChild(title);
        _page.AddChild(up);
        _page.AddChild(status);
        _page.AddChild(work);

        if (b.Slots > 0 || b.Staffed > 0)
        {
            var slots = Label("");
            var row = new AssignmentRow(b.Id, BuildingBase.StaffActivity, "Staff");
            _updaters.Add(() =>
            {
                slots.Text = $"Necromancer slots: {DevFormat.Exact(b.Staffed)} of {DevFormat.Exact(b.Slots)} " +
                             $"(+{b.Definition.SlotsPerLevel} per level)";
                row.Refresh();
            });
            _page.AddChild(slots);
            _page.AddChild(row);
        }
        _page.AddChild(new HSeparator());

        switch (b)
        {
            case ManaWell w: BuildManaWell(w); break;
            case Infuser i: BuildConverter(i.Conversion, () => i.Progress); break;
            case SoulWell s: BuildConverter(s.Conversion, () => s.Progress); break;
            case Transmuter t: BuildTransmuter(t); break;
            case SummoningCircle c: BuildCircle(c); break;
            case UndeadMarket m: BuildMarket(m); break;
            case MagicalStorage st: BuildStorage(st); break;
        }
        Update();
    }

    private void Update()
    {
        if (!IsVisibleInTree()) return;
        foreach (var u in _updaters) u();
    }

    private void BuildManaWell(ManaWell w)
    {
        var l = Label("");
        _updaters.Add(() => l.Text = $"Makes {Num(w.WorkPerTick)} {Name(w.Output)}/tick.   {Stock(w.Output)}");
        _page.AddChild(l);
    }

    private void BuildConverter(ConverterDefinition d, Func<double> progress)
    {
        _page.AddChild(Label($"Converts {Amounts(d.Inputs)} into {Amount(d.Output)}; {Num(d.WorkPerOp)} work each."));
        var bar = Bar();
        var l = Label("");
        _updaters.Add(() =>
        {
            bar.Value = progress();
            l.Text = string.Join("   ", d.Inputs.Select(i => Stock(i.Id)).Append(Stock(d.Output.Id)));
        });
        _page.AddChild(bar);
        _page.AddChild(l);
    }

    private void BuildTransmuter(Transmuter t)
    {
        var pick = new OptionButton();
        pick.AddItem("(none)");
        foreach (var r in t.Recipes)
        {
            pick.AddItem($"{r.Name}: {Amounts(r.Inputs)} -> {Amount(r.Output)}, {Num(r.Work)} work{(t.IsLocked(r.Id) ? "  (locked: research)" : "")}");
            pick.SetItemDisabled(pick.ItemCount - 1, t.IsLocked(r.Id));
        }
        pick.Selected = t.SelectedRecipe is { } id ? t.Recipes.ToList().FindIndex(r => r.Id == id) + 1 : 0;
        pick.ItemSelected += i =>
        {
            t.Select(i == 0 ? null : t.Recipes[(int)i - 1].Id);
            DevLog.Add(LogCategory.Workshop, $"Transmuter recipe: {t.Recipe?.Name ?? "none"}.");
            Update();
        };
        _page.AddChild(Label("Resource conversion only (v0.1). Recipe:"));
        _page.AddChild(pick);
        var bar = Bar();
        var l = Label("");
        _updaters.Add(() =>
        {
            bar.Value = t.Progress;
            l.Text = t.Recipe is { } r ? string.Join("   ", r.Inputs.Select(i => Stock(i.Id)).Append(Stock(r.Output.Id))) : "";
        });
        _page.AddChild(bar);
        _page.AddChild(l);
    }

    private void BuildCircle(SummoningCircle c)
    {
        // Phase 9: unit mode (keep a unit type's stack at a target; set it from the Units tab).
        var unitMode = Label("");
        var unitBar = Bar();
        var back = new Button { Text = "Back to necromancers", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        back.Pressed += () => { c.SetUnitTarget(null, 0); Build(); };
        _page.AddChild(unitMode);
        _page.AddChild(unitBar);
        _page.AddChild(back);
        _updaters.Add(() =>
        {
            var units = CryptManager.Instance.Units;
            var on = c.UnitType is not null;
            unitMode.Text = on
                ? $"Unit mode: keeping {units?.Type(c.UnitType!)?.Name ?? c.UnitType} at {DevFormat.Exact(c.UnitTarget)} " +
                  $"(roster {DevFormat.Exact(units?.Count(c.UnitType!) ?? 0)}). Costs come from the unit type."
                : "Necromancer mode. (Units tab: 'Circle: keep stack at this count' switches to a unit type.)";
            unitBar.Visible = on;
            unitBar.Value = c.UnitProgress;
            back.Visible = on;
        });
        if (c.UnitType is not null) return;

        var r = c.Recipe;
        _page.AddChild(Label(r is null ? "No summon recipes." :
            $"Summons {r.Count} {r.Name} for {Amounts(r.Costs)}; {Num(r.Work)} work each. " +
            $"Each level adds {Num(((SummoningCircleDefinition)c.Definition).MaxNecromancersPerLevel)} max necromancers."));
        var bar = Bar();
        var l = Label("");
        _page.AddChild(bar);
        _page.AddChild(l);
        var row = new HBoxContainer();
        row.AddChild(Label("Target total (0 = up to max):", wrap: false));
        var target = new LineEdit { CustomMinimumSize = new Vector2(120, 0), Text = c.Target.ToString(CultureInfo.InvariantCulture) };
        row.AddChild(target);
        var set = new Button { Text = "Set" };
        set.Pressed += () =>
        {
            if (DevFormat.TryParseCount(target.Text, out var n) && n >= 0)
            {
                c.Target = n;
                DevLog.Add(LogCategory.Workshop, $"Summoning target: {(n == 0 ? "none (up to max)" : DevFormat.Exact(n))}.");
                Update();
            }
            else DevLog.Error($"'{target.Text}' is not a count.");
        };
        row.AddChild(set);
        _page.AddChild(row);
        _updaters.Add(() =>
        {
            bar.Value = c.Progress;
            var p = LichManager.Instance.State.Pool;
            l.Text = $"{Num(c.SummonsPerTick)} summons/tick at full supply.   Pool {DevFormat.Exact(p.Total)} / {DevFormat.Exact(p.Max)} " +
                     $"(free {DevFormat.Exact(p.Free)}).   " + (r is null ? "" : string.Join("   ", r.Costs.Select(x => $"{Name(x.Id)} {DevFormat.Exact(Crypt.Stockpile.Get(x.Id))}/{DevFormat.Exact(x.Count)}")));
        });
    }

    private void BuildMarket(UndeadMarket m)
    {
        _page.AddChild(Label("Sell list: each unit of work sells one unit, top to bottom, keeping the reserve. Stops when coins are full."));
        var grid = new GridContainer { Columns = 5 };
        _page.AddChild(grid);
        foreach (var h in new[] { "Sell", "Item", "Price", "Stock", "Reserve" }) grid.AddChild(Label(h, "HeaderSmall", false));
        foreach (var id in m.Sellable)
        {
            var e = m.Entry(id);
            var on = new CheckBox { ButtonPressed = e?.Enabled ?? false };
            var reserve = new LineEdit { Text = (e?.Reserve ?? 0).ToString(CultureInfo.InvariantCulture), CustomMinimumSize = new Vector2(90, 0) };
            void Apply()
            {
                var r = DevFormat.TryParseCount(reserve.Text, out var n) && n >= 0 ? n : 0;
                m.SetSell(id, on.ButtonPressed, r);
                DevLog.Add(LogCategory.Workshop, $"Market: {(on.ButtonPressed ? "sell" : "keep")} {Name(id)}, reserve {DevFormat.Exact(r)}.");
            }
            on.Toggled += _ => Apply();
            reserve.TextSubmitted += _ => Apply();
            grid.AddChild(on);
            grid.AddChild(Label(Name(id), wrap: false));
            grid.AddChild(Label(DevFormat.Exact(m.PriceOf(id)), wrap: false));
            var stock = Label("", wrap: false);
            grid.AddChild(stock);
            grid.AddChild(reserve);
            _updaters.Add(() => stock.Text = DevFormat.Exact(Crypt.Stockpile.Get(id)));
        }
        var coins = Label("");
        _updaters.Add(() => coins.Text = $"Coins {DevFormat.Exact(Crypt.Storage.Get("coins"))} / {DevFormat.Exact(Crypt.Storage.Cap("coins"))}, " +
                                         $"{Crypt.Storage.RatePerTick("coins").ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture)}/tick");
        _page.AddChild(coins);
    }

    private void BuildStorage(MagicalStorage st)
    {
        var l = Label("");
        _updaters.Add(() => l.Text =
            $"Every resource cap is base cap x {Num(st.CapMultiplier)} (+{Num(((MagicalStorageDefinition)st.Definition).CapBonusPerLevel)} per level). " +
            $"For example {Stock("mana")}, {Stock("coins")}.");
        _page.AddChild(l);
    }

    // ---- Helpers ----

    private static string StatusText(BuildingBase b) => b.Status switch
    {
        BuildingStatus.Running => "Running",
        BuildingStatus.Idle => b.WorkPerTick > 0 ? "Idle (waiting for the next tick)" : "Idle (no work: add necromancers)",
        BuildingStatus.MissingInput => $"Paused: missing {Name(b.StatusDetail)}",
        BuildingStatus.OutputFull => $"Paused: {Name(b.StatusDetail)} is full",
        BuildingStatus.NothingToDo => b is UndeadMarket ? "Nothing to sell" : "No recipe chosen",
        BuildingStatus.PoolFull => b.StatusDetail == "target" ? "Paused: summoning target reached" : "Paused: necromancer pool is at max",
        BuildingStatus.Passive => "Passive",
        _ => b.Status.ToString(),
    };

    private static string Name(string? id) => id is null ? "?" : StockIds.NameOf(GameManager.Instance.Content, id);
    private static string Amount(ItemAmount a) => $"{DevFormat.Exact(a.Count)} {Name(a.Id)}";
    private static string Amounts(IEnumerable<ItemAmount> xs) => string.Join(" + ", xs.Select(Amount));
    private static string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Stock(string id) =>
        Crypt.Storage.IsResource(id)
            ? $"{Name(id)} {DevFormat.Exact(Crypt.Storage.Get(id))}/{DevFormat.Exact(Crypt.Storage.Cap(id))}"
            : $"{Name(id)} {DevFormat.Exact(Crypt.Stockpile.Get(id))}";

    /// <summary>Wrapping labels for the page body; <paramref name="wrap"/> false for labels inside rows and grids.</summary>
    private static Label Label(string text, string? variation = null, bool wrap = true) =>
        new() { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off, ThemeTypeVariation = variation ?? "" };

    private static ProgressBar Bar() => new() { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(260, 10), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree()) Refresh();
    }
}
