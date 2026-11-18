using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Managers;
using LittleLich.Skills;
using LittleLich.UI.Components;
using LittleLich.UI.Theme;
using LittleLich.World;

namespace LittleLich.UI.Shell;

/// <summary>
/// Top bar (h48, BgBar, bottom border): tick bar, necromancer counter, siege strip (only during a battle),
/// Map and Settings buttons.
/// </summary>
public partial class TopBar : PanelContainer
{
    private readonly StatBar _tick;
    private readonly Label _tickText;
    private readonly Label _free, _assigned, _max;
    private readonly StatBar _poolBar;
    private readonly PanelContainer _counter;
    private readonly NavItem _siege;
    private readonly Label _siegeTitle, _siegeWalls;
    private readonly StatBar _siegeBar;

    public TopBar(Action<string> navigate)
    {
        var sb = Tokens.Box(Tokens.BgBar, Tokens.Border, 0, 20, 0, 0);
        sb.BorderWidthBottom = 1;
        AddThemeStyleboxOverride("panel", sb);
        CustomMinimumSize = new Vector2(0, Tokens.TopBarHeight);
        var row = Ui.Row(0);
        AddChild(row);

        // 1. Tick bar (w300)
        var tick = Ui.Col(5);
        tick.CustomMinimumSize = new Vector2(300, 0);
        tick.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _tickText = Ui.Text("0.0s / 2.0s", Tokens.FontLabel, Tokens.TextMain, Tokens.Bold);
        tick.AddChild(Ui.Row(0, Ui.Text("TICK", Tokens.FontLabel, Tokens.TextMuted, Tokens.LabelFont), Ui.Fill(), _tickText));
        _tick = new StatBar(Tokens.Green, 7);
        tick.AddChild(_tick);
        row.AddChild(tick);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(88, 0) });

        // 2. Necromancer counter (w254)
        _counter = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(254, 0), MouseFilter = MouseFilterEnum.Pass };
        _counter.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.NecroBg, Tokens.NecroBorder, Tokens.RadiusInset, 12, 5));
        _free = Ui.Bold("Free 0", 12, Tokens.Green);
        _assigned = Ui.Bold("Assigned 0", 12, Tokens.TextMain);
        _max = Ui.Text("Max 0", 12, Tokens.TextMuted);
        _poolBar = new StatBar(Tokens.PurpleDeep, 4);
        _counter.AddChild(Ui.Col(3, Ui.Row(12, _free, _assigned, Ui.Fill(), _max), _poolBar));
        row.AddChild(_counter);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(14, 0) });

        // 3. Siege strip (hidden when no battle is running)
        _siege = new NavItem(12, 6);
        _siege.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _siege.Selected = true;
        _siegeTitle = Ui.Text("SIEGE", Tokens.FontLabel, Tokens.Green, Tokens.LabelFont);
        _siegeWalls = Ui.Text("Walls 100%", Tokens.FontSmall, Tokens.TextMain);
        _siegeBar = Ui.Width(new StatBar(Tokens.Red, 6), 64);
        _siegeBar.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _siegeBar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _siege.Body.AddChild(Ui.Row(14, _siegeTitle, _siegeWalls, _siegeBar));
        _siege.Pressed += () => navigate("Battle");
        _siege.TooltipText = "Open the battle view";
        row.AddChild(_siege);

        row.AddChild(Ui.Fill());
        var map = new ActionButton("Map", padX: 10, padY: 6);
        map.Pressed += () => navigate("Map");
        row.AddChild(map);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });
        var settings = new ActionButton("Settings", padX: 10, padY: 6);
        settings.Pressed += () => navigate("Settings");
        row.AddChild(settings);
        foreach (var b in new[] { map, settings }) b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    public void SetTick(double elapsed, double length)
    {
        _tick.Value = elapsed / length;
        _tickText.Text = $"{Fmt.Num1(elapsed)}s / {Fmt.Num1(length)}s";
    }

    public void RefreshPool()
    {
        var p = LichManager.Instance.State.Pool;
        _free.Text = $"Free {Fmt.Short(p.Free)}";
        _assigned.Text = $"Assigned {Fmt.Short(p.Assigned)}";
        _max.Text = $"Max {Fmt.Short(p.Max)}";
        _poolBar.Value = p.Max > 0 ? (double)p.Assigned / p.Max : 0;
        _counter.TooltipText = $"Free {Fmt.Exact(p.Free)}\nAssigned {Fmt.Exact(p.Assigned)}\nMax {Fmt.Exact(p.Max)}\nTotal {Fmt.Exact(p.Total)}";
    }

    /// <summary>Called every frame: the siege strip follows the running battle (v0.1: one at a time).</summary>
    public void RefreshSiege()
    {
        var w = WorldManager.Instance.State;
        var b = w?.Battle;
        _siege.Visible = b is not null;
        if (b is null || w!.BattleArea is not { } areaId) return;
        var area = w.Area(areaId);
        _siegeTitle.Text = $"SIEGE · {(area?.Name ?? areaId).ToUpperInvariant()}";
        var hp = b.SideMaxHp(BattleSide.Defender) > 0 ? b.SideHp(BattleSide.Defender) / b.SideMaxHp(BattleSide.Defender) : 0;
        _siegeWalls.Text = $"Walls {hp:P0}";
        _siegeBar.Value = hp;
    }
}

/// <summary>Crypt tabs row (h46): "THE CRYPT" and Workshop, Research, Unit Builder, Spell Book, Lich.</summary>
public partial class CryptTabs : PanelContainer
{
    public static readonly (string Key, string Label)[] Tabs =
    {
        ("Crypt/Workshop", "Workshop"), ("Crypt/Research", "Research"), ("Crypt/UnitBuilder", "Unit Builder"),
        ("Crypt/SpellBook", "Spell Book"), ("Crypt/Lich", "Lich"),
    };

    private readonly Dictionary<string, ActionButton> _buttons = new();
    private readonly Label _label, _crumbs;
    private readonly ActionButton _back;

    public CryptTabs(Action<string> navigate)
    {
        var sb = Tokens.Box(Tokens.BgPanelDark, Tokens.Border, 0, 20, 0, 0);
        sb.BorderWidthBottom = 1;
        AddThemeStyleboxOverride("panel", sb);
        CustomMinimumSize = new Vector2(0, Tokens.TabsHeight);
        var row = Ui.Row(8);
        AddChild(row);
        _label = Ui.Width(Ui.Text("THE CRYPT", Tokens.FontLabel, Tokens.TextDim, Tokens.LabelFont), 190);
        row.AddChild(_label);
        _crumbs = Ui.Width(Ui.Text("", Tokens.FontLabel, Tokens.TextDim, Tokens.LabelFont), 360);
        _crumbs.Visible = false;
        row.AddChild(_crumbs);
        _back = new ActionButton("‹ Back to map", padX: 18, padY: 7, fontSize: Tokens.FontTab, font: Tokens.Heading) { SizeFlagsVertical = SizeFlags.ShrinkCenter, Visible = false };
        _back.Pressed += () => navigate("Map");
        row.AddChild(_back);
        foreach (var (key, label) in Tabs)
        {
            var b = new ActionButton(label, padX: 18, padY: 7, fontSize: Tokens.FontTab, font: Tokens.Heading)
            { SizeFlagsVertical = SizeFlags.ShrinkCenter };
            b.AddThemeColorOverride("font_color", Tokens.TextTab);
            b.Pressed += () => navigate(key);
            row.AddChild(b);
            _buttons[key] = b;
        }
    }

    /// <summary>Highlight the open Crypt tab (none when a skill, the map or settings is open).</summary>
    public void Select(string? key)
    {
        var battle = key is "Battle" or "BossPrep";
        _label.Visible = !battle;
        _crumbs.Visible = _back.Visible = battle;
        if (battle)
        {
            var area = LittleLich.UI.Pages.WorldUi.SelectedArea is { } a ? LittleLich.UI.Pages.WorldUi.W?.Area(LittleLich.UI.Pages.WorldUi.W.BattleArea ?? a)?.Name ?? a : "";
            _crumbs.Text = $"MAP  ›  {area.ToUpperInvariant()}  ›  {(key == "Battle" ? "FIGHT" : "PREPARE")}";
        }
        foreach (var b in _buttons.Values) b.Visible = !battle;
        foreach (var (k, b) in _buttons)
        {
            b.SetSelected(k == key);
            b.AddThemeColorOverride("font_color", k == key ? Tokens.TextMain : Tokens.TextTab);
        }
    }
}

/// <summary>
/// Skills sidebar (w210): LICH, GATHERING, CRAFTING groups; each skill shows a dot, name, "›" when it has a page,
/// level/999 and a 3 px XP bar. Gathering and crafting skills open their page; Lich skills show a tooltip only.
/// </summary>
public partial class SkillsSidebar : PanelContainer
{
    private sealed record Entry(NavItem Item, Dot Dot, Label Level, StatBar Xp);

    private readonly Dictionary<string, Entry> _entries = new();
    private readonly VBoxContainer _col;
    private readonly Action<string> _navigate;

    public SkillsSidebar(Action<string> navigate)
    {
        _navigate = navigate;
        var sb = Tokens.Box(Tokens.BgPanelDark, Tokens.Border, 0, 10, 10, 0);
        sb.BorderWidthRight = 1;
        AddThemeStyleboxOverride("panel", sb);
        CustomMinimumSize = new Vector2(Tokens.SidebarWidth, 0);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        _col = Ui.Col(6);
        _col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_col);
    }

    /// <summary>Builds one entry per skill from the LichManager (after boot).</summary>
    public void Build()
    {
        foreach (var c in _col.GetChildren()) c.QueueFree();
        _entries.Clear();
        string? group = null;
        foreach (var s in LichManager.Instance.Skills)
        {
            var g = s switch { GatheringSkill => "Gathering", CraftingSkill => "Crafting", _ => "Lich" };
            if (g != group)
            {
                group = g;
                var label = Ui.Section(g);
                label.CustomMinimumSize = new Vector2(0, group == "Lich" ? 18 : 26);
                label.VerticalAlignment = VerticalAlignment.Bottom;
                _col.AddChild(label);
            }
            var item = new NavItem(9, 5, 4);
            var dot = Ui.Dot(Tokens.DotOff);
            var name = Ui.Bold(s.Name, 12);
            var hasPage = s is GatheringSkill or CraftingSkill;
            var level = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
            var row = Ui.Row(7, dot, name);
            if (hasPage) row.AddChild(Ui.Bold("›", 13, Tokens.Green));
            row.AddChild(Ui.Fill());
            row.AddChild(level);
            row.MouseFilter = MouseFilterEnum.Ignore;
            item.Body.AddChild(row);
            var xp = new StatBar(Tokens.Green, 3);
            item.Body.AddChild(xp);
            var id = s.Id;
            if (hasPage) item.Pressed += () => _navigate("Skill/" + id);
            _col.AddChild(item);
            _entries[id] = new Entry(item, dot, level, xp);
        }
        Refresh();
    }

    public void Refresh()
    {
        foreach (var s in LichManager.Instance.Skills)
        {
            if (!_entries.TryGetValue(s.Id, out var e)) continue;
            e.Level.Text = $"{s.Level}/{XpCurve.MaxLevel}";
            e.Xp.Value = s.LevelProgress;
            e.Item.TooltipText = $"{s.Name}: level {s.Level}, {Fmt.Exact(s.Xp)} XP ({s.LevelProgress:P0} to next level)" +
                                 (s is HealthSkill ? $"\n+{(s.Level - 1) * HealthSkill.MaxHealthPerLevel:0} max health" : "");
        }
    }

    /// <summary>Highlight the open skill (null = none).</summary>
    public void Select(string? skillId)
    {
        foreach (var (id, e) in _entries)
        {
            e.Item.Selected = id == skillId;
            e.Dot.Color = id == skillId ? Tokens.Green : Tokens.DotOff;
        }
    }
}

/// <summary>
/// Resource bar (h60): one box per resource from JSON (w164): colour circle, stock, "/ cap", rate per tick and a
/// 3 px fill bar. Scrolls horizontally if there are more than fit.
/// </summary>
public partial class ResourceBar : PanelContainer
{
    private sealed record Box(PanelContainer Panel, Label Stock, Label Cap, Label Rate, StatBar Fill);

    private readonly Dictionary<string, Box> _boxes = new();
    private readonly HBoxContainer _row;

    public ResourceBar()
    {
        var sb = Tokens.Box(Tokens.BgBar, Tokens.Border, 0, 16, 0, 0);
        sb.BorderWidthTop = 1;
        AddThemeStyleboxOverride("panel", sb);
        CustomMinimumSize = new Vector2(0, Tokens.ResourceBarHeight);
        var scroll = new ScrollContainer { VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        _row = Ui.Row(16);
        _row.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_row);
    }

    public void Build()
    {
        foreach (var c in _row.GetChildren()) c.QueueFree();
        _boxes.Clear();
        foreach (var r in GameManager.Instance.Content.All<ResourceDefinitionBase>())
        {
            var panel = new PanelContainer { CustomMinimumSize = new Vector2(164, 0), MouseFilter = MouseFilterEnum.Pass, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            panel.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgCardAlt, Tokens.BorderCard, Tokens.RadiusButton, 10, 7));
            var color = r.Color is { } hex ? new Color(hex) : Tokens.TextMuted;
            var stock = Ui.Bold("0", Tokens.FontNumber);
            var cap = Ui.Text("/ 0", 10, Tokens.TextDim);
            var rate = Ui.Bold("+0/tick", 10, Tokens.Green);
            var fill = new StatBar(Tokens.Purple, 3);
            panel.AddChild(Ui.Col(3, Ui.Row(6, Ui.Dot(color, 22), stock, cap, Ui.Fill(), rate), fill));
            _row.AddChild(panel);
            _boxes[r.Id] = new Box(panel, stock, cap, rate, fill);
        }
        Refresh();
    }

    public void Refresh()
    {
        var storage = CryptManager.Instance.Storage;
        foreach (var r in GameManager.Instance.Content.All<ResourceDefinitionBase>())
        {
            if (!_boxes.TryGetValue(r.Id, out var b)) continue;
            var stock = storage.Get(r.Id);
            var cap = storage.Cap(r.Id);
            var rate = storage.RatePerTick(r.Id);
            var full = stock >= cap;
            b.Stock.Text = Fmt.Short(stock);
            b.Cap.Text = $"/ {Fmt.Short(cap)}";
            b.Rate.Text = $"{Fmt.Rate(rate)}/tick";
            b.Rate.AddThemeColorOverride("font_color", rate < 0 ? Tokens.Red : Tokens.Green);
            b.Fill.Value = cap > 0 ? (double)stock / cap : 0;
            b.Fill.FillColor = full ? Tokens.Yellow : Tokens.Purple;
            b.Panel.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgCardAlt, full ? Tokens.YellowBorder : Tokens.BorderCard, Tokens.RadiusButton, 10, 7));
            b.Panel.TooltipText = $"{r.Name}: {Fmt.Exact(stock)} / {Fmt.Exact(cap)}" + (full ? " (full: production stops)" : "") +
                                  $"\n{Fmt.Num2(rate)} per tick (last tick batch)";
        }
    }
}
