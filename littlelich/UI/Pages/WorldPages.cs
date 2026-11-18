using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Crypt.Units;
using LittleLich.Managers;
using LittleLich.UI.Components;
using LittleLich.UI.Theme;
using LittleLich.World;
using static LittleLich.UI.Pages.Kit;

namespace LittleLich.UI.Pages;

/// <summary>Shared world state for the map, prep and battle pages: the selected area and formation.</summary>
public static class WorldUi
{
    public static WorldState? W => WorldManager.Instance.State;
    public static UnitBuilder? U => CryptMgr.Units;

    private static string? _area, _formation;

    public static string? SelectedArea
    {
        get
        {
            if (W is not { } w) return null;
            if (_area is not null && w.Area(_area) is not null) return _area;
            return w.BattleArea ?? w.Areas.FirstOrDefault(a => w.IsReachable(a.Id) && w.State(a.Id) != AreaState.Cleared)?.Id ?? w.Areas.FirstOrDefault()?.Id;
        }
        set => _area = value;
    }

    public static Formation? SelectedFormation
    {
        get => U is { } u ? (_formation is not null ? u.GetFormation(_formation) : null) ?? u.Formations.FirstOrDefault() : null;
        set => _formation = value?.Id;
    }

    /// <summary>Total HP of an army plan (count x unit HP with gear).</summary>
    public static double PlanHp(ArmyPlan plan) => U is { } u
        ? plan.Entries.Sum(e => u.Type(e.Unit) is null ? 0 : e.Count * u.BattleUnit(e.Unit).Hp) : 0;

    public static double DefenderHp(AreaDefinition a) =>
        a.Defenders.Values.SelectMany(x => x).Sum(d => Db.TryGet<UnitDefinitionBase>(d.Unit, out var u) ? d.Count * u.Hp : 0);

    public static string SendText(SendResult r) => r switch
    {
        SendResult.Ok => "The host marches.",
        SendResult.BattleRunning => "One battle at a time: wait for the current one or recall it.",
        SendResult.AlreadyCleared => "That area is already taken.",
        SendResult.NotReachable => "No path leads there yet. Take the areas on the way first.",
        SendResult.EmptyArmy => "The formation is empty. Put units in its slots.",
        SendResult.NotEnoughUnits => "The roster does not have enough units for this formation.",
        _ => r.ToString(),
    };

    public static string Clock(double seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";
}

/// <summary>World map (image 07, v0.1: paths and two areas, no rings). Map card, siege banner, area panel, Host card.</summary>
public partial class MapPage : PageBase
{
    public override void Bind()
    {
        base.Bind();
        WorldManager.Instance.BattleStarted += _ => Rebuild();
        WorldManager.Instance.BattleEnded += (_, _) => Rebuild();
        WorldManager.Instance.AreaStateChanged += _ => Rebuild();
        CryptMgr.FormationsChanged += MarkDirty;
        CryptMgr.UnitRosterChanged += (_, _) => MarkDirty();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (IsVisibleInTree() && WorldUi.W?.Battle is not null) foreach (var u in Updaters) u();
    }

    protected override void Build()
    {
        if (WorldUi.W is not { } w) return;
        var row = CardRow();

        var mapCard = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        mapCard.AddThemeStyleboxOverride("panel", Tokens.Box(new Color("#0E1310"), Tokens.BorderCard, Tokens.RadiusCard));
        row.AddChild(mapCard);
        var map = new WorldMapView(w);
        map.Selected += id => { WorldUi.SelectedArea = id; Rebuild(); };
        mapCard.AddChild(map);
        var overlay = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var side in new[] { "left", "top", "right", "bottom" }) overlay.AddThemeConstantOverride("margin_" + side, 14);
        mapCard.AddChild(overlay);
        var ov = Ui.Col(0);
        ov.MouseFilter = MouseFilterEnum.Ignore;
        overlay.AddChild(ov);
        if (w.Battle is not null && w.BattleArea is { } bid) ov.AddChild(SiegeBanner(w, bid));
        ov.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var legend = Ui.Row(14);
        foreach (var (t, c) in new[] { ("Taken", Tokens.Green), ("Besieged", Tokens.Purple), ("Available", Tokens.TextMain), ("Locked", Tokens.DotOff) })
            legend.AddChild(Ui.Row(5, Ui.Dot(c, 8), Ui.Text(t, Tokens.FontLabel, Tokens.TextSoft)));
        ov.AddChild(legend);
        ov.AddChild(Ui.Text("Areas are attacked along paths from the crypt. Take an area to open the paths beyond it.", Tokens.FontLabel, Tokens.TextMuted));
        Update(map.QueueRedraw);

        var right = Ui.Width(Ui.Col(Tokens.CardGap), 300);
        row.AddChild(right);
        if (WorldUi.SelectedArea is { } areaId && w.Area(areaId) is { } area)
        {
            right.AddChild(AreaCard(w, area));
            right.AddChild(HostCard(w, area));
        }
    }

    private Control SiegeBanner(WorldState w, string areaId)
    {
        var p = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkBegin, MouseFilter = MouseFilterEnum.Stop };
        p.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.NecroBg, Tokens.NecroBorder, Tokens.RadiusInset, 12, 10));
        var walls = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
        var time = Ui.Text("", Tokens.FontSmall, Tokens.Orange);
        var watch = new ActionButton("Watch battle", primary: true, padX: 26, padY: 6);
        watch.Pressed += () => Navigate("Battle");
        var recall = new ActionButton("Recall", padX: 12, padY: 6);
        recall.Pressed += () => { if (!WorldManager.Instance.Retreat()) Toast.Show("Nothing to recall.", true); };
        p.AddChild(Ui.Col(6,
            Ui.Row(18, Ui.Text("SIEGE IN PROGRESS", Tokens.FontLabel, Tokens.NecroText, Tokens.LabelFont), Ui.Fill(), time),
            Ui.Row(6, Ui.Bold(w.Area(areaId)?.Name ?? areaId, 14), walls),
            Ui.Row(8, watch, recall)));
        Update(() =>
        {
            if (w.Battle is not { } b) return;
            var max = b.SideMaxHp(BattleSide.Defender);
            walls.Text = $"Defenders {(max > 0 ? b.SideHp(BattleSide.Defender) / max : 0):P0}";
            time.Text = $"Fight {WorldUi.Clock(b.NowSeconds)}";
        });
        return p;
    }

    private static Control AreaCard(WorldState w, AreaDefinition a)
    {
        var card = new Card(8);
        var state = w.State(a.Id);
        var reachable = w.IsReachable(a.Id);
        var pill = new Pill(state switch { AreaState.Cleared => "TAKEN", AreaState.InProgress => "BESIEGED", _ => reachable ? "AVAILABLE" : "LOCKED" });
        pill.SetKind(state == AreaState.Cleared ? PillKind.Selected : state == AreaState.InProgress ? PillKind.Warning : reachable ? PillKind.Running : PillKind.Neutral);
        card.Body.AddChild(Ui.Row(8, Ui.Heading(a.Name, Tokens.FontTitle), Ui.Fill(), pill));
        card.Body.AddChild(Ui.Muted(a.Description));
        card.Body.AddChild(Ui.Section("Defences"));
        foreach (var section in Sections.All)
        {
            if (!a.Defenders.TryGetValue(section, out var list) || list.Count == 0) continue;
            var r = new InsetRow();
            r.Body.AddChild(Ui.Text(char.ToUpperInvariant(section[0]) + section[1..], Tokens.FontSmall, Tokens.TextSoft));
            r.Body.AddChild(Ui.Fill());
            r.Body.AddChild(Ui.Bold(string.Join(", ", list.Select(d => $"{Fmt.Short(d.Count)} {(Db.TryGet<UnitDefinitionBase>(d.Unit, out var u) ? u.Name : d.Unit)}")), Tokens.FontSmall, Tokens.Yellow));
            card.Body.AddChild(r);
        }
        var (rw, rv) = KeyValue("Rewards", Tokens.TextMain);
        rv.Text = string.Join(", ", a.Rewards.Select(x => $"{Fmt.Short(x.Value)} {Name(x.Key).ToLowerInvariant()}"));
        rv.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rv.HorizontalAlignment = HorizontalAlignment.Right;
        rv.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.Body.AddChild(rw);
        return card;
    }

    private Control HostCard(WorldState w, AreaDefinition a)
    {
        var card = new Card(8);
        card.Body.AddChild(Ui.Heading("Your Host", 15));
        var f = WorldUi.SelectedFormation;
        var (r1, units) = KeyValue("Units in the roster");
        var (r2, strength) = KeyValue($"HP: {f?.Name ?? "no formation"} vs defenders", Tokens.Green);
        var bar = new StatBar(Tokens.Green, 6);
        var note = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted, wrap: true);
        foreach (var c in new Control[] { r1, r2, bar, note }) card.Body.AddChild(c);
        var send = new ActionButton("Send the Host", primary: true, padY: 10, fontSize: 14) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        send.Pressed += () =>
        {
            if (WorldUi.SelectedFormation is not { } fm) { Navigate("BossPrep"); return; }
            var res = w.Send(a.Id, fm);
            Toast.Show(WorldUi.SendText(res), res != SendResult.Ok);
            if (res == SendResult.Ok) Navigate("Battle");
        };
        var edit = new ActionButton("Edit formation", padY: 8) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        edit.Pressed += () => Navigate("BossPrep");
        card.Body.AddChild(send);
        card.Body.AddChild(edit);
        Update(() =>
        {
            var total = WorldUi.U?.Roster.Values.Sum() ?? 0;
            units.Text = Fmt.Short(total);
            var plan = WorldUi.SelectedFormation is { } fm ? w.PlanFor(fm) : new ArmyPlan();
            var mine = WorldUi.PlanHp(plan);
            var theirs = WorldUi.DefenderHp(a);
            strength.Text = $"{Fmt.Short(mine)} vs {Fmt.Short(theirs)}";
            strength.AddThemeColorOverride("font_color", mine >= theirs ? Tokens.Green : Tokens.Yellow);
            bar.Value = theirs > 0 ? mine / (mine + theirs) : 1;
            var state = w.State(a.Id);
            send.Disabled = state != AreaState.NotStarted || !w.IsReachable(a.Id) || w.Battle is not null;
            note.Text = state == AreaState.Cleared ? "Already taken." : !w.IsReachable(a.Id) ? "No path leads here yet." :
                w.Battle is not null ? "One battle at a time." : mine == 0 ? "The formation is empty or the roster has none of its units." :
                mine >= theirs ? "More HP than the defenders. One battle runs at a time." : "Fewer HP than the defenders: expect losses.";
        });
        return card;
    }
}

/// <summary>Draws the map nodes and paths; click a node with an area to select it.</summary>
public partial class WorldMapView : Control
{
    private readonly WorldState _w;
    private string? _hover;

    public WorldMapView(WorldState w)
    {
        _w = w;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public event Action<string>? Selected;

    private (Vector2 Min, Vector2 Max) Bounds()
    {
        var xs = _w.Nodes.Select(n => (float)n.Position[0]).ToList();
        var ys = _w.Nodes.Select(n => (float)n.Position.ElementAtOrDefault(1)).ToList();
        return (new Vector2(xs.Min(), ys.Min()), new Vector2(xs.Max(), ys.Max()));
    }

    private Vector2 Pos(MapNodeDefinition n)
    {
        var (min, max) = Bounds();
        var span = max - min;
        var pad = new Vector2(90, 120);
        var area = Size - pad * 2;
        var scale = Math.Min(span.X > 0 ? area.X / span.X : 1, span.Y > 0 ? area.Y / span.Y : area.X);
        var p = new Vector2((float)n.Position[0], (float)n.Position.ElementAtOrDefault(1));
        var center = Size / 2 - (min + max) / 2 * scale;
        return center + p * scale;
    }

    private Color NodeColor(MapNodeDefinition n)
    {
        if (n.Home) return Tokens.Yellow;
        if (n.Area is not { } a) return Tokens.TextMuted;
        return _w.State(a) switch
        {
            AreaState.Cleared => Tokens.Green,
            AreaState.InProgress => Tokens.Purple,
            _ => _w.IsReachable(a) ? Tokens.TextMain : Tokens.DotOff,
        };
    }

    private static float Radius(MapNodeDefinition n) => n.Home ? 20 : n.Area is not null ? 16 : 8;

    public override void _Draw()
    {
        if (_w.Nodes.Count == 0) return;
        // faint rings for depth (decoration only; v0.1 has no rings)
        var c = Size / 2;
        for (var i = 1; i <= 3; i++) DrawArc(c, Math.Min(Size.X, Size.Y) * 0.16f * i, 0, Mathf.Tau, 64, Tokens.Border with { A = 0.35f }, 1, true);
        var byId = _w.Nodes.ToDictionary(n => n.Id);
        foreach (var n in _w.Nodes)
            foreach (var l in n.Links)
                if (byId.TryGetValue(l, out var m))
                {
                    var open = Passable(n) && Passable(m);
                    var reach = Passable(n) || Passable(m);
                    if (open) DrawLine(Pos(n), Pos(m), Tokens.GreenDeep, 3, true);
                    else DrawDashedLine(Pos(n), Pos(m), reach ? Tokens.TextMuted : Tokens.Border, 2, 7, true);
                }
        var selected = WorldUi.SelectedArea;
        var font = Tokens.Bold;
        foreach (var n in _w.Nodes)
        {
            var p = Pos(n);
            var r = Radius(n);
            var color = NodeColor(n);
            if (n.Area is not null && n.Area == selected) DrawCircle(p, r + 8, Tokens.Green with { A = 0.18f });
            DrawCircle(p, r, Tokens.BgPanelDark);
            DrawArc(p, r, 0, Mathf.Tau, 40, color, n.Area == selected || n.Id == _hover ? 3 : 2, true);
            DrawCircle(p, r * 0.42f, color);
            var size = n.Area is not null || n.Home ? 12 : 11;
            var w = font.GetStringSize(n.Name, HorizontalAlignment.Left, -1, size).X;
            DrawString(font, p + new Vector2(-w / 2, r + size + 5), n.Name, HorizontalAlignment.Left, -1, size, n.Area is null && !n.Home ? Tokens.TextMuted : Tokens.TextMain);
        }
    }

    private bool Passable(MapNodeDefinition n) => n.Home || n.Area is null || _w.State(n.Area) == AreaState.Cleared;

    public override void _GuiInput(InputEvent e)
    {
        var hit = e is InputEventMouse m ? _w.Nodes.FirstOrDefault(n => Pos(n).DistanceTo(m.Position) <= Radius(n) + 6) : null;
        if (e is InputEventMouseMotion && hit?.Id != _hover)
        {
            _hover = hit?.Id;
            TooltipText = hit?.Area is { } a ? $"{_w.Area(a)?.Name}: click to select" : hit?.Name ?? "";
            QueueRedraw();
        }
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && hit?.Area is { } area) Selected?.Invoke(area);
    }
}

/// <summary>
/// Battle prep (image 08, v0.1: area battles, no bosses): the area's defenders; the formation as eight slots
/// (2 front, 3 middle, 3 back) with unit and count; saved formations; supply check against the roster; Begin fight.
/// </summary>
public partial class BossPrepPage : PageBase
{
    public override void Bind()
    {
        base.Bind();
        CryptMgr.FormationsChanged += MarkDirty;
        CryptMgr.UnitTypesChanged += Rebuild;
        CryptMgr.UnitRosterChanged += (_, _) => MarkDirty();
        WorldManager.Instance.BattleStarted += _ => MarkDirty();
        WorldManager.Instance.BattleEnded += (_, _) => MarkDirty();
    }

    protected override void Build()
    {
        if (WorldUi.W is not { } w || WorldUi.U is not { } u || WorldUi.SelectedArea is not { } areaId || w.Area(areaId) is not { } area) return;
        var row = CardRow();
        row.AddChild(AreaCard(w, area));
        row.AddChild(LoadoutCard(u));
        var right = Ui.Width(Ui.Col(Tokens.CardGap), 296);
        row.AddChild(right);
        SupplyCard(right, u);
        var rules = new Card(6);
        rules.Body.AddChild(Ui.Heading("Rules", 15));
        foreach (var r in new[] { "Re-summon when a unit dies", "Retry after a loss", "Swap units when the enemy shields" })
        {
            var ir = new InsetRow();
            ir.Body.AddChild(Ui.Expand(Ui.Text(r, Tokens.FontSmall, Tokens.TextSoft)));
            ir.Body.AddChild(Ui.Bold("OFF", Tokens.FontSmall, Tokens.TextDim));
            rules.Body.AddChild(ir);
        }
        Ui.SetLocked(rules, true);
        rules.TooltipText = "Battle rules are not in v0.1";
        right.AddChild(rules);
        var begin = new ActionButton("Begin fight", primary: true, padY: 11, fontSize: 14) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        begin.Pressed += () =>
        {
            if (WorldUi.SelectedFormation is not { } f) { Toast.Show("Make a formation first.", true); return; }
            var res = w.Send(areaId, f);
            Toast.Show(WorldUi.SendText(res), res != SendResult.Ok);
            if (res == SendResult.Ok) Navigate("Battle");
        };
        right.AddChild(begin);
        Update(() => begin.Disabled = w.Battle is not null || w.State(areaId) != AreaState.NotStarted || !w.IsReachable(areaId));
    }

    private static Control AreaCard(WorldState w, AreaDefinition a)
    {
        var card = new Card(10) { CustomMinimumSize = new Vector2(276, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        var portrait = new PanelContainer { CustomMinimumSize = new Vector2(66, 66) };
        portrait.AddThemeStyleboxOverride("panel", Tokens.Box(new Color("#241A14"), Tokens.Orange, Tokens.RadiusInset, 0, 0, 2));
        var pc = new CenterContainer();
        pc.AddChild(Ui.Dot(Tokens.Orange, 30));
        portrait.AddChild(pc);
        var pill = new Pill("AREA");
        pill.SetKind(PillKind.Warning);
        pill.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        card.Body.AddChild(Ui.Row(12, portrait, Ui.Col(4, Ui.Heading(a.Name, 17), Ui.Text(w.State(a.Id) == AreaState.Cleared ? "Taken" : "Defended", Tokens.FontSmall, Tokens.TextMuted), pill)));
        var defenders = a.Defenders.Values.SelectMany(x => x).Sum(d => d.Count);
        var boxes = Ui.Row(8);
        foreach (var (l, v) in new[] { ("Defenders", Fmt.Short(defenders)), ("Health", Fmt.Short(WorldUi.DefenderHp(a))), ("Sections", a.Defenders.Count.ToString()) })
        {
            var p = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            p.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgInset, Tokens.Border, Tokens.RadiusButton, 10, 6));
            p.AddChild(Ui.Col(2, Ui.Text(l.ToUpperInvariant(), 10, Tokens.TextMuted, Tokens.LabelFont), Ui.Bold(v, 14)));
            boxes.AddChild(p);
        }
        card.Body.AddChild(boxes);
        card.Body.AddChild(Ui.Section("What you know"));
        foreach (var section in Sections.All)
            foreach (var d in a.Defenders.GetValueOrDefault(section) ?? new List<UnitCount>())
            {
                if (!Db.TryGet<UnitDefinitionBase>(d.Unit, out var ud)) continue;
                var r = new InsetRow();
                r.SetStyle(Tokens.BgInset, Tokens.SelectedBorder);
                var acts = string.Join(", ", ud.ResolvedActions.Select(x => $"{x.Name} every {Fmt.Num1(x.CooldownSeconds)}s"));
                r.Body.AddChild(Ui.Expand(Ui.Text($"{char.ToUpperInvariant(section[0])}{section[1..]}: {Fmt.Short(d.Count)} {ud.Name} · {Fmt.Short(ud.Hp)} HP · {acts}", Tokens.FontSmall, Tokens.TextSoft, wrap: true)));
                card.Body.AddChild(r);
            }
        card.Body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        card.Body.AddChild(Ui.Text(w.LastResult is { } lr ? $"Last battle: {lr.Outcome} at {lr.AreaId}, {Fmt.Short(lr.Lost)} lost" : "No battles yet.", Tokens.FontLabel, Tokens.TextMuted));
        return card;
    }

    private Control LoadoutCard(UnitBuilder u)
    {
        var card = new Card(10) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        card.Body.AddChild(Ui.Row(8, Ui.Heading("Encounter loadout", 18), Ui.Fill(), Ui.Text("The formation sent into battle", Tokens.FontSmall, Tokens.TextMuted)));

        // Saved formations
        var saved = Ui.Row(6);
        foreach (var fm in u.Formations)
        {
            var b = new ActionButton(fm.Name, padX: 12, padY: 5);
            b.SetSelected(fm.Id == WorldUi.SelectedFormation?.Id);
            b.AddThemeColorOverride("font_color", fm.Id == WorldUi.SelectedFormation?.Id ? Tokens.Green : Tokens.TextSoft);
            var f = fm;
            b.Pressed += () => { WorldUi.SelectedFormation = f; Rebuild(); };
            saved.AddChild(b);
        }
        var add = new ActionButton("Save as new", padX: 12, padY: 5);
        add.Pressed += () =>
        {
            var nf = u.CreateFormation("");
            if (WorldUi.SelectedFormation is { } cur)
                foreach (var s in cur.Slots) u.SetSlot(nf.Id, s.Index, s.UnitType, s.Count);
            WorldUi.SelectedFormation = nf;
            Rebuild();
        };
        saved.AddChild(add);
        card.Body.AddChild(Ui.Section("Saved formations"));
        card.Body.AddChild(saved);

        if (WorldUi.SelectedFormation is not { } form)
        {
            card.Body.AddChild(Ui.Muted("No formation yet. Press Save as new to make one."));
            return card;
        }
        card.Body.AddChild(Ui.Section("Slots · 2 front, 3 middle, 3 back"));
        card.Body.AddChild(new StepSelector());
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        scroll.AddChild(grid);
        card.Body.AddChild(scroll);
        foreach (var section in Sections.All)
        {
            var slots = form.Slots.Where(s => s.Section == section).ToList();
            foreach (var s in slots) grid.AddChild(SlotCard(u, form, s));
            for (var i = slots.Count; i < 3; i++) grid.AddChild(new Control());
        }
        return card;
    }

    private Control SlotCard(UnitBuilder u, Formation form, FormationSlot s)
    {
        var p = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var filled = s.UnitType is not null && s.Count > 0;
        p.AddThemeStyleboxOverride("panel", filled ? Tokens.Box(Tokens.NecroBg, Tokens.NecroBorder, Tokens.RadiusInset, 10, 8)
                                                   : Tokens.Box(Tokens.BgInset, Tokens.Border, Tokens.RadiusInset, 10, 8));
        var col = Ui.Col(5);
        p.AddChild(col);
        col.AddChild(Ui.Text($"SLOT {s.Index + 1} · {s.Section.ToUpperInvariant()}", 10, Tokens.TextMuted, Tokens.LabelFont));
        var types = u.Types.Where(t => !u.IsBaseLocked(t.Base)).ToList();
        var name = s.UnitType is { } id ? u.Type(id)?.Name ?? id : "Empty";
        var prev = new ActionButton("‹", padX: 6, padY: 0);
        var next = new ActionButton("›", padX: 6, padY: 0);
        void Cycle(int dir)
        {
            var i = s.UnitType is null ? -1 : types.FindIndex(t => t.Id == s.UnitType);
            var n = i + dir;
            if (n < -1) n = types.Count - 1;
            if (n >= types.Count) n = -1;
            var t = n < 0 ? null : types[n].Id;
            u.SetSlot(form.Id, s.Index, t, t is null ? 0 : Math.Max(s.Count, Math.Min(10, u.Count(t))));
            Rebuild();
        }
        prev.Pressed += () => Cycle(-1);
        next.Pressed += () => Cycle(1);
        var nameLabel = Ui.Bold(name, Tokens.FontSmall, filled ? Tokens.TextMain : Tokens.TextDim);
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nameLabel.ClipText = true;
        col.AddChild(Ui.Row(4, prev, nameLabel, next));
        var stepper = new Stepper(() => s.Count,
            n => { if (s.UnitType is { } t) u.SetSlot(form.Id, s.Index, t, n == long.MaxValue ? u.Count(t) : Sat(s.Count, n)); return n; },
            n => { if (s.UnitType is { } t) u.SetSlot(form.Id, s.Index, t, Math.Max(0, s.Count - n)); return n; })
        { Locked = s.UnitType is null };
        col.AddChild(stepper);
        var roster = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted);
        col.AddChild(roster);
        Update(() =>
        {
            stepper.Refresh();
            roster.Text = s.UnitType is { } t ? $"{Fmt.Short(u.Count(t))} in the roster" : types.Count == 0 ? "Build units first" : "Pick a unit with ‹ ›";
            roster.AddThemeColorOverride("font_color", s.UnitType is { } t2 && u.Count(t2) < s.Count ? Tokens.Yellow : Tokens.TextMuted);
        });
        return p;
    }

    private static long Sat(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;

    private void SupplyCard(VBoxContainer into, UnitBuilder u)
    {
        var card = new Card(6);
        card.Body.AddChild(Ui.Heading("Supply check", 15));
        card.Body.AddChild(Ui.Muted("Units this formation needs, against the roster."));
        var list = Ui.Col(6);
        card.Body.AddChild(list);
        var summary = Ui.Text("", Tokens.FontSmall, Tokens.Yellow, wrap: true);
        card.Body.AddChild(summary);
        into.AddChild(card);
        Update(() =>
        {
            foreach (var c in list.GetChildren()) c.QueueFree();
            var f = WorldUi.SelectedFormation;
            var need = f?.Slots.Where(s => s.UnitType is not null && s.Count > 0).GroupBy(s => s.UnitType!).ToDictionary(g => g.Key, g => g.Sum(s => s.Count))
                       ?? new Dictionary<string, long>();
            var shortOf = new List<string>();
            foreach (var (t, n) in need)
            {
                var have = u.Count(t);
                var ok = have >= n;
                if (!ok) shortOf.Add(u.Type(t)?.Name ?? t);
                var (row, v) = KeyValue(u.Type(t)?.Name ?? t, ok ? Tokens.Green : Tokens.Yellow);
                v.Text = $"{Fmt.Short(have)} / {Fmt.Short(n)} · {(ok ? "ready" : "short")}";
                var bar = new StatBar(ok ? Tokens.Green : Tokens.Yellow, 4) { Value = n > 0 ? (double)have / n : 1 };
                list.AddChild(row);
                list.AddChild(bar);
            }
            summary.Text = need.Count == 0 ? "The formation is empty." : shortOf.Count == 0 ? "Everything is ready." : $"Short of {string.Join(", ", shortOf)}: summon more in the Unit Builder.";
            summary.AddThemeColorOverride("font_color", need.Count > 0 && shortOf.Count == 0 ? Tokens.Green : Tokens.Yellow);
        });
    }
}

/// <summary>
/// Battle view (image 09, v0.1 rule: sections with up to 3 actions each on cooldown bars). Battlefield with our
/// sections on the left and the defenders on the right, the next enemy action, section panels, log and Retreat.
/// </summary>
public partial class BattlePage : PageBase
{
    private readonly List<string> _log = new();
    private readonly Dictionary<object, (string Text, double Left)> _floaters = new();
    private Label? _logLabel;

    public override void Bind()
    {
        base.Bind();
        WorldManager.Instance.BattleStarted += _ => { _log.Clear(); Rebuild(); };
        WorldManager.Instance.BattleEnded += (_, _) => Rebuild();
        WorldManager.Instance.ActionFired += OnAction;
    }

    private void OnAction(BattleAction a)
    {
        var verb = a.Action.Effect switch { "heal" => "healed", "reduce_next_incoming" => "braced", _ => "hit" };
        var target = a.Target is { } t && !ReferenceEquals(t, a.Source) ? $" {t.Unit.Name}" : "";
        _log.Insert(0, $"{WorldUi.Clock(a.AtMs / 1000.0)} {a.Source.Unit.Name} {a.Action.Name}: {verb}{target}{(a.Amount > 0 ? $" for {Fmt.Short(a.Amount)}" : "")}");
        if (_log.Count > 40) _log.RemoveAt(_log.Count - 1);
        if (a.Target is { } tg && a.Amount > 0) _floaters[tg] = ((a.Action.Effect == "heal" ? "+" : "-") + Fmt.Short(a.Amount), 1.0);
        if (_logLabel is not null && IsInstanceValid(_logLabel)) _logLabel.Text = string.Join("\n", _log.Take(14));
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        foreach (var k in _floaters.Keys.ToList())
        {
            var (t, left) = _floaters[k];
            if (left - delta <= 0) _floaters.Remove(k); else _floaters[k] = (t, left - delta);
        }
        if (IsVisibleInTree() && WorldUi.W?.Battle is not null) foreach (var u in Updaters) u();
    }

    protected override void Build()
    {
        if (WorldUi.W is not { } w) return;
        var col = Ui.Col(Tokens.CardGap);
        col.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(col);
        if (w.Battle is not { } b || w.BattleArea is not { } areaId)
        {
            var card = new Card(10);
            card.Body.AddChild(Ui.Heading("No battle running", Tokens.FontTitle));
            if (w.LastResult is { } r)
            {
                card.Body.AddChild(Ui.Text($"Last battle: {r.Outcome} at {w.Area(r.AreaId)?.Name ?? r.AreaId}.", Tokens.FontBody, r.Outcome == BattleOutcome.Victory ? Tokens.Green : Tokens.Yellow));
                card.Body.AddChild(Ui.Text($"Sent {Fmt.Short(r.Sent.Values.Sum())}, lost {Fmt.Short(r.Lost)}, {Fmt.Short(r.Survivors.Values.Sum())} came home.", Tokens.FontSmall, Tokens.TextMuted));
                if (r.Rewards.Count > 0)
                    card.Body.AddChild(Ui.Text("Rewards: " + string.Join(", ", r.Rewards.Select(x => $"{Fmt.Short(x.Value)} {Name(x.Key).ToLowerInvariant()}")), Tokens.FontSmall, Tokens.Green));
            }
            else card.Body.AddChild(Ui.Muted("Send the Host from the map to start one."));
            var back = new ActionButton("‹ Back to map", padY: 7) { SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
            back.Pressed += () => Navigate("Map");
            card.Body.AddChild(back);
            col.AddChild(card);
            return;
        }

        col.AddChild(Field(b, w.Area(areaId)?.Name ?? areaId));
        var groups = Ui.Row(10);
        foreach (var g in b.GroupsOf(BattleSide.Attacker).Take(4)) groups.AddChild(GroupPanel(b, g));
        col.AddChild(groups);
        var bottom = Ui.Row(Tokens.CardGap);
        bottom.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(bottom);
        var enemies = new Card(6) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        enemies.Body.AddChild(Ui.Section("Defenders"));
        var eg = new GridContainer { Columns = 3 };
        eg.AddThemeConstantOverride("h_separation", 8);
        foreach (var g in b.GroupsOf(BattleSide.Defender)) eg.AddChild(GroupPanel(b, g, compact: true));
        enemies.Body.AddChild(eg);
        bottom.AddChild(enemies);
        var log = new Card(4) { CustomMinimumSize = new Vector2(250, 0) };
        _logLabel = Ui.Text(string.Join("\n", _log.Take(14)), Tokens.FontSmall, Tokens.TextSoft);
        _logLabel.ClipText = true;
        _logLabel.VerticalAlignment = VerticalAlignment.Top;
        _logLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
        log.Body.AddChild(_logLabel);
        bottom.AddChild(log);
        var retreat = new Button { Text = "Retreat", CustomMinimumSize = new Vector2(96, 0), FocusMode = FocusModeEnum.None };
        retreat.AddThemeStyleboxOverride("normal", Tokens.Box(new Color("#2A1A15"), new Color("#5C3226"), Tokens.RadiusCard));
        retreat.AddThemeStyleboxOverride("hover", Tokens.Box(new Color("#341F18"), Tokens.Orange, Tokens.RadiusCard));
        retreat.AddThemeStyleboxOverride("pressed", Tokens.Box(new Color("#341F18"), Tokens.Orange, Tokens.RadiusCard));
        retreat.AddThemeColorOverride("font_color", Tokens.Orange);
        retreat.AddThemeFontOverride("font", Tokens.Bold);
        retreat.Pressed += () => WorldManager.Instance.Retreat();
        bottom.AddChild(retreat);
    }

    private Control Field(BattleController b, string areaName)
    {
        var card = new Card(8);
        var hpBar = Ui.Width(new StatBar(Tokens.Yellow, 10), 420);
        hpBar.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        var hpText = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
        hpText.HorizontalAlignment = HorizontalAlignment.Center;
        var title = Ui.Heading(areaName, 17);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var clock = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
        var top = Ui.Row(8, new Control { CustomMinimumSize = new Vector2(120, 0) }, Ui.Expand(Ui.Col(4, title, hpBar, hpText)), Ui.Width(clock, 120));
        clock.HorizontalAlignment = HorizontalAlignment.Right;
        clock.VerticalAlignment = VerticalAlignment.Top;
        card.Body.AddChild(top);

        var field = Ui.Row(18);
        field.CustomMinimumSize = new Vector2(0, 116);
        foreach (var g in b.GroupsOf(BattleSide.Attacker)) field.AddChild(Unit(g, Tokens.NecroBorder, Tokens.NecroBg));
        field.AddChild(Ui.Fill());
        foreach (var g in b.GroupsOf(BattleSide.Defender)) field.AddChild(Unit(g, Tokens.Orange, new Color("#241A14")));
        card.Body.AddChild(field);

        var tele = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(360, 0) };
        tele.AddThemeStyleboxOverride("panel", Tokens.Box(new Color("#2A1A15"), new Color("#5C3226"), Tokens.RadiusButton, 12, 6));
        var next = Ui.Bold("", Tokens.FontSmall, Tokens.Orange);
        var when = Ui.Text("", Tokens.FontSmall, Tokens.TextSoft);
        tele.AddChild(Ui.Row(18, next, Ui.Fill(), when));
        card.Body.AddChild(tele);
        Update(() =>
        {
            var max = b.SideMaxHp(BattleSide.Defender);
            var hp = b.SideHp(BattleSide.Defender);
            hpBar.Value = max > 0 ? hp / max : 0;
            hpText.Text = $"{Fmt.Short(hp)} / {Fmt.Short(max)}";
            clock.Text = $"Fight {WorldUi.Clock(b.NowSeconds)}\nLimit {WorldUi.Clock(b.TimeLimitMs / 1000.0)}";
            var soon = b.GroupsOf(BattleSide.Defender).Where(g => g.IsAlive)
                .SelectMany(g => g.Unit.Actions.Select((a, i) => (g, a, at: g.NextFireMs[i]))).OrderBy(x => x.at).FirstOrDefault();
            next.Text = soon.a is null ? "Next: nothing" : $"Next: {soon.a.Name} ({soon.g.Unit.Name})";
            when.Text = soon.a is null ? "" : $"in {Fmt.Num1(Math.Max(0, soon.at - b.NowMs) / 1000.0)}s · targets {soon.a.Target}";
        });
        return card;
    }

    private Control Unit(BattleGroup g, Color border, Color fill)
    {
        var col = Ui.Col(4);
        col.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        var floater = Ui.Bold("", 14, Tokens.TextMain);
        floater.HorizontalAlignment = HorizontalAlignment.Center;
        var hp = Ui.Width(new StatBar(g.Side == BattleSide.Attacker ? Tokens.Green : Tokens.Red, 4), 54);
        var box = new PanelContainer { CustomMinimumSize = new Vector2(54, 60) };
        var label = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(floater);
        col.AddChild(hp);
        col.AddChild(box);
        col.AddChild(label);
        Update(() =>
        {
            hp.Value = g.MaxHp > 0 ? g.Hp / g.MaxHp : 0;
            box.AddThemeStyleboxOverride("panel", g.IsAlive ? Tokens.Box(fill, border, 14, 0, 0, 2) : Tokens.Box(Tokens.LockedFill, Tokens.LockedBorder, 14));
            label.Text = $"{g.Unit.Name} ×{Fmt.Short(g.Alive)}";
            var f = _floaters.TryGetValue(g, out var fl) ? fl.Text : "";
            floater.Text = f;
            floater.AddThemeColorOverride("font_color", f.StartsWith('+') ? Tokens.Green : g.Side == BattleSide.Attacker ? Tokens.Red : Tokens.TextMain);
            col.TooltipText = $"{g.Section}: {g.Unit.Name}, {Fmt.Exact(g.Alive)} of {Fmt.Exact(g.StartCount)} alive, {Fmt.Short(g.Hp)} / {Fmt.Short(g.MaxHp)} HP";
        });
        return col;
    }

    private Control GroupPanel(BattleController b, BattleGroup g, bool compact = false)
    {
        var p = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        p.AddThemeStyleboxOverride("panel", g.Side == BattleSide.Attacker ? Tokens.Box(Tokens.NecroBg, Tokens.NecroBorder, Tokens.RadiusInset, 12, 9)
                                                                           : Tokens.Box(Tokens.BgInset, Tokens.Border, Tokens.RadiusInset, 10, 7));
        var col = Ui.Col(5);
        p.AddChild(col);
        var state = Ui.Bold("", Tokens.FontSmall, Tokens.Green);
        col.AddChild(Ui.Row(8, Ui.Bold($"{char.ToUpperInvariant(g.Section[0])}{g.Section[1..]} · {g.Unit.Name}", compact ? Tokens.FontSmall : Tokens.FontBody), Ui.Fill(), state));
        var hp = new StatBar(g.Side == BattleSide.Attacker ? Tokens.Purple : Tokens.Red, compact ? 4 : 6);
        col.AddChild(hp);
        var bars = new List<(StatBar Bar, int Index)>();
        for (var i = 0; i < g.Unit.Actions.Count; i++)
        {
            var a = g.Unit.Actions[i];
            var bar = Ui.Width(new StatBar(Tokens.Teal, 3), 70);
            bar.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            bar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            col.AddChild(Ui.Row(6, Ui.Text($"{a.Name} {Fmt.Num1(a.Cooldown)}s", Tokens.FontLabel, Tokens.TextMuted), Ui.Fill(), bar));
            bars.Add((bar, i));
        }
        Update(() =>
        {
            hp.Value = g.MaxHp > 0 ? g.Hp / g.MaxHp : 0;
            state.Text = g.IsAlive ? $"{Fmt.Short(g.Alive)}/{Fmt.Short(g.StartCount)}" : "Fallen";
            state.AddThemeColorOverride("font_color", g.IsAlive ? Tokens.Green : Tokens.Red);
            foreach (var (bar, i) in bars)
            {
                var cd = g.Unit.Actions[i].Cooldown * 1000;
                bar.Value = cd > 0 ? 1 - Math.Clamp((g.NextFireMs[i] - b.NowMs) / cd, 0, 1) : 1;
            }
        });
        return p;
    }
}
