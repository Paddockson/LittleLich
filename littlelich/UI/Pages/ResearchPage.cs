using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Crypt.Research;
using LittleLich.Managers;
using LittleLich.UI.Components;
using LittleLich.UI.Theme;
using static LittleLich.UI.Pages.Kit;

namespace LittleLich.UI.Pages;

/// <summary>
/// Research (image 03, research revamp): found tomes, a pan-and-zoom tree of the nodes that are visible (circles for
/// one-off nodes, rounded squares with a level badge for upgrades), a hover card, the benches and the bench level card.
/// Every node of a found tome is drawn so the player can plan ahead; nodes that still need others are dimmed with
/// dashed lines. Tomes appear when found.
/// </summary>
public partial class ResearchPage : PageBase
{
    private string? _tome;

    private static ResearchManager? R => CryptMgr.Research;

    public override void Bind()
    {
        base.Bind();
        CryptMgr.ResearchNodeStarted += (_, _) => RebuildSoon();
        CryptMgr.ResearchNodeCompleted += _ => RebuildSoon();
        CryptMgr.ResearchNodeCancelled += (_, _) => RebuildSoon();
        CryptMgr.ResearchBenchesChanged += RebuildSoon;
        CryptMgr.ResearchNodeAvailable += _ => MarkDirty();
    }

    protected override void Build()
    {
        if (R is not { } r) return;
        var tomes = r.Tomes;
        if (_tome is null || tomes.All(t => t.Id != _tome)) _tome = tomes.FirstOrDefault()?.Id;
        var row = CardRow();
        row.AddChild(TomeList(r, tomes));
        row.AddChild(TreeCard(r, tomes.FirstOrDefault(t => t.Id == _tome)));
        row.AddChild(BenchColumn(r));
    }

    // ---- Left: tomes ----

    private Control TomeList(ResearchManager r, IReadOnlyList<ResearchTome> tomes)
    {
        var col = Ui.Width(Ui.Col(8), 164);
        col.AddChild(Ui.Section("Tomes"));
        foreach (var t in tomes)
        {
            var item = new NavItem(12, 9, 2);
            var count = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
            item.Body.AddChild(Ui.Row(6, Ui.Bold(t.Name, Tokens.FontBody), Ui.Fill(), count));
            item.Body.AddChild(Ui.Text(t.Definition.Note, Tokens.FontLabel, Tokens.TextMuted));
            item.Selected = t.Id == _tome;
            var id = t.Id;
            item.Pressed += () => { _tome = id; Rebuild(); };
            col.AddChild(item);
            Update(() => count.Text = $"{r.DoneCount(t)}/{t.Nodes.Count}");
        }
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        col.AddChild(Ui.Text("Done: green\nResearching: purple\nAvailable: grey ring\nLocked: dimmed, dashed line\nCircle: one-off\nSquare: repeatable (level)", Tokens.FontLabel, Tokens.TextDim, wrap: true));
        col.AddChild(Ui.Text("Plan ahead: every node is shown. A node can start when everything it needs is done. Tomes appear when found.", Tokens.FontLabel, Tokens.TextDim, wrap: true));
        return col;
    }

    // ---- Centre: tree ----

    private Control TreeCard(ResearchManager r, ResearchTome? tome)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        card.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.TreeBg, Tokens.BorderCard, Tokens.RadiusCard));
        if (tome is null) return card;
        var tree = new ResearchTree(r, tome);
        card.AddChild(tree);
        var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore };
        card.AddChild(overlay);

        var hover = new HoverCard(r);
        hover.SetAnchorsPreset(LayoutPreset.BottomLeft);
        hover.GrowVertical = GrowDirection.Begin;
        hover.OffsetLeft = 14;
        hover.OffsetBottom = -14;
        overlay.AddChild(hover);
        tree.HoverChanged += id => hover.Show(id);
        hover.Show(null);

        var zoom = Ui.Row(6, Ui.Text("Drag · zoom", Tokens.FontLabel, Tokens.TextDim));
        var plus = new ActionButton("+", padX: 8, padY: 2);
        var minus = new ActionButton("−", padX: 9, padY: 2);
        plus.Pressed += () => tree.ZoomBy(1.15f);
        minus.Pressed += () => tree.ZoomBy(1 / 1.15f);
        zoom.AddChild(plus);
        zoom.AddChild(minus);
        zoom.SetAnchorsPreset(LayoutPreset.BottomRight);
        zoom.GrowHorizontal = GrowDirection.Begin;
        zoom.GrowVertical = GrowDirection.Begin;
        zoom.OffsetRight = -14;
        zoom.OffsetBottom = -12;
        overlay.AddChild(zoom);
        Update(tree.QueueRedraw);
        Update(() => hover.Show(tree.Hovered));
        return card;
    }

    // ---- Right: benches and bench level ----

    private Control BenchColumn(ResearchManager r)
    {
        var right = Ui.Width(Ui.Col(Tokens.CardGap), 270);
        var benches = new Card(8);
        benches.Body.AddChild(Ui.Heading("Research benches"));
        foreach (var b in r.Benches) benches.Body.AddChild(BenchRow(r, b));
        right.AddChild(benches);
        right.AddChild(BenchLevelCard(r));
        right.AddChild(Ui.Text("Each bench works one node at a time. Click a bench to research by hand; assigned necromancers add work every tick. More benches come from research.",
            Tokens.FontLabel, Tokens.TextMuted, wrap: true));
        return right;
    }

    private Control BenchRow(ResearchManager r, Bench b)
    {
        var p = new BenchDropTarget(id => r.CanPlace(id, b.Index), id =>
        {
            var name = r.Node(id)?.Name ?? id;
            var busy = b.NodeId is { } cur && cur != id ? r.Node(cur)?.Name : null;
            if (r.Place(id, b.Index) == StartResult.Ok)
                Toast.Show(busy is null ? $"Researching {name} on bench {b.Index + 1}." : $"Researching {name} on bench {b.Index + 1}. {busy} keeps its progress.");
            else Toast.Show($"Cannot put {name} on bench {b.Index + 1}.", true);
            RebuildSoon();
        });
        p.Clicked = () => { if (r.Click(b.Index)) MarkDirty(); };
        var col = Ui.Col(7);
        p.AddChild(col);
        var ring = new NodeRing();
        var name = Ui.Bold("", Tokens.FontSmall);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var stepper = new Stepper(() => r.Staffed(b), n => r.AddNecromancer(b.Index, Clamp(n, Lich.State.Pool.Free)), n => r.RemoveNecromancer(b.Index, Clamp(n, r.Staffed(b))))
        {
            Free = () => Math.Min(Lich.State.Pool.Free, Math.Max(0, r.Slots - r.Staffed(b))),
            Max = () => r.Slots,
        };
        stepper.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        stepper.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        name.CustomMinimumSize = new Vector2(84, 0);
        var cancel = new ActionButton("x", padX: 6, padY: 0, fontSize: Tokens.FontLabel) { TooltipText = "Stop this node (its progress is kept)" };
        cancel.Pressed += () => { r.Cancel(b.Index); Rebuild(); };
        col.AddChild(Ui.Row(6, ring, name, stepper));
        var bar = new StatBar(Tokens.Purple, 5) { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var left = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted);
        col.AddChild(Ui.Row(8, bar, left, cancel));
        var work = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted);
        col.AddChild(work);
        Update(() =>
        {
            var node = b.NodeId is { } id ? r.Node(id) : null;
            var busy = node is not null;
            p.Restyle = hot => p.AddThemeStyleboxOverride("panel",
                hot ? Tokens.Box(Tokens.SelectedBg, Tokens.Green, Tokens.RadiusInset, 11, 9, 2)
                : busy ? Tokens.Box(Tokens.NecroBg, Tokens.PurpleDeep, Tokens.RadiusInset, 11, 9)
                : Tokens.Box(Tokens.BgInset, Tokens.YellowBorder, Tokens.RadiusInset, 11, 9));
            p.Restyle(p.Hot);
            ring.Set(busy ? Tokens.Purple : Tokens.DotOff, busy ? Tokens.Purple : Tokens.BgPanelDark);
            name.Text = busy ? node!.Name + (node.IsUpgrade ? $" {r.Level(node.Id) + 1}" : "") : "Free · pick a node";
            stepper.Refresh();
            cancel.Visible = busy;
            bar.Value = busy ? r.Progress(b) : 0;
            var ticks = r.TicksLeft(b);
            left.Text = busy ? ticks is { } t ? FormatTime(t * Core.OfflineTime.BaseTickSeconds) : r.Staffed(b) > 0 ? "no work" : "by hand" : "";
            work.Text = !busy ? "Drag a node here, or click one"
                : r.Staffed(b) > 0 ? $"Work {Fmt.Short(r.WorkNeeded(node!.Id))} · {Fmt.Num2(r.WorkPerTick(b))} per tick"
                : $"Work {Fmt.Short(r.WorkNeeded(node!.Id))} · click the bench (+{Fmt.Num2(r.ClickWork)})";
            p.MouseDefaultCursorShape = busy ? CursorShape.PointingHand : CursorShape.Arrow;
            p.TooltipText = $"Click to research by hand (+{Fmt.Num2(r.ClickWork)} work). {Fmt.Exact(r.Staffed(b))} of {Fmt.Exact(r.Slots)} necromancers · {Fmt.Num2(r.WorkPerTick(b))} work per tick";
        });
        return p;
    }

    private Control BenchLevelCard(ResearchManager r)
    {
        var card = new Card(10);
        var title = Ui.Bold("", Tokens.FontNumber);
        var speed = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
        var chips = Ui.Row(6);
        var up = new ActionButton("UPGRADE", primary: true, padY: 8, fontSize: 13, font: Tokens.LabelFont) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        up.Pressed += () => { if (!r.TryUpgradeBench() && !r.CanUpgradeBench(out var why)) Toast.Show($"Cannot upgrade the benches: {why}.", true); else Rebuild(); };
        var reason = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted);
        reason.HorizontalAlignment = HorizontalAlignment.Center;
        card.Body.AddChild(title);
        card.Body.AddChild(speed);
        card.Body.AddChild(chips);
        card.Body.AddChild(up);
        card.Body.AddChild(reason);
        var costLabels = new List<(ItemAmount Cost, Label Value)>();
        foreach (var c in r.BenchUpgradeCosts)
        {
            var chip = new PanelContainer();
            chip.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgInset, Tokens.Border, Tokens.RadiusButton, 10, 6));
            var v = Ui.Bold("", Tokens.FontSmall);
            chip.AddChild(Ui.Row(6, Ui.Dot(ColorOf(c.Id), 8), v));
            chip.TooltipText = Name(c.Id);
            chips.AddChild(chip);
            costLabels.Add((c, v));
        }
        Update(() =>
        {
            var staffed = r.Benches.Count > 0 ? Math.Min(r.Staffed(r.Benches[0]), r.Slots) : 0;
            var now = r.WorkPerTickWith(staffed, r.BenchLevel);
            title.Text = r.IsBenchMaxLevel ? $"Bench level {r.BenchLevel} (max)" : $"Bench level {r.BenchLevel} ▸ {r.BenchLevel + 1}";
            speed.Text = r.IsBenchMaxLevel ? $"Work per tick {Fmt.Num2(now)}" : $"Work per tick {Fmt.Num2(now)} ▸ {Fmt.Num2(r.WorkPerTickWith(staffed, r.BenchLevel + 1))}";
            foreach (var (c, v) in costLabels)
            {
                var have = Owned(c.Id);
                v.Text = Fmt.Short(c.Count);
                v.TooltipText = $"{Name(c.Id)}: {Fmt.Exact(have)} / {Fmt.Exact(c.Count)}";
                v.AddThemeColorOverride("font_color", have >= c.Count ? Tokens.TextMain : Tokens.Red);
            }
            var can = r.CanUpgradeBench(out var why);
            up.Disabled = !can;
            up.Text = r.IsBenchMaxLevel ? "MAX LEVEL" : "UPGRADE";
            reason.Text = can ? "" : why;
            reason.Visible = !can;
        });
        return card;
    }

    private static Color ColorOf(string id) =>
        Db.TryGet<ResourceDefinitionBase>(id, out var res) && res.Color is { } hex ? new Color(hex) : Tokens.TextMuted;
}

/// <summary>A small ring with a dot (bench job icon).</summary>
public partial class NodeRing : Control
{
    private Color _ring = Tokens.DotOff, _fill = Tokens.BgPanelDark;

    public NodeRing()
    {
        CustomMinimumSize = new Vector2(34, 34);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Set(Color ring, Color fill)
    {
        _ring = ring;
        _fill = fill;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var c = Size / 2;
        DrawCircle(c, 16, Tokens.BgPanelDark);
        DrawArc(c, 15, 0, Mathf.Tau, 40, _ring, 2, true);
        DrawCircle(c, 8, _fill);
    }
}

/// <summary>What kind of thing a node gives (inner dot colour): bone units, gold buildings, purple spells, green upgrades, blue stats.</summary>
public static class NodeKinds
{
    public static Color DotColor(ResearchNodeDefinitionBase n)
    {
        if (n.Effects.Any(e => e.Type == "unlock" && e.Kind == "unit")) return Tokens.Bone;
        if (n.Effects.Any(e => e.Type == "unlock" && e.Kind == "building")) return Tokens.Yellow;
        if (n.Effects.Any(e => e.Type == "unlock" && e.Kind == "spell")) return Tokens.Purple;
        if (n.IsUpgrade) return Tokens.UpgradeGreen;
        return Tokens.Blue;
    }
}

/// <summary>The hover card: name, time on the best bench, ONE-OFF / REPEATABLE · level / max, effect text, work, requirements.</summary>
public partial class HoverCard : PanelContainer
{
    private readonly ResearchManager _r;
    private readonly Label _name, _time, _kind, _desc, _work, _need, _hint;

    public HoverCard(ResearchManager r)
    {
        _r = r;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(196, 0);
        AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgPopup, Tokens.SelectedBorder, Tokens.RadiusInset, 12, 10));
        _name = Ui.Bold("", 14);
        _time = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
        _kind = Ui.Text("", 10, Tokens.Green, Tokens.LabelFont);
        _desc = Ui.Text("", Tokens.FontSmall, Tokens.TextSoft, wrap: true);
        _desc.CustomMinimumSize = new Vector2(172, 0);
        _work = Ui.Text("", Tokens.FontSmall, Tokens.Green, wrap: true);
        _need = Ui.Text("", Tokens.FontSmall, Tokens.Green, wrap: true);
        _need.CustomMinimumSize = new Vector2(172, 0);
        _hint = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted, wrap: true);
        AddChild(Ui.Col(4, Ui.Row(12, _name, Ui.Fill(), _time), _kind, _desc, _work, _need, _hint));
    }

    public void Show(string? nodeId)
    {
        Visible = nodeId is not null && _r.Node(nodeId) is not null;
        if (!Visible) return;
        var n = _r.Node(nodeId!)!;
        var state = _r.State(n.Id);
        _name.Text = n.Name;
        var rate = _r.Benches.FirstOrDefault(b => b.IsFree) is { } free ? _r.WorkPerTick(free) : _r.Benches.Select(_r.WorkPerTick).DefaultIfEmpty(0).Max();
        var ticks = _r.TicksFor(n.Id, rate);
        _time.Text = state == NodeState.Done ? "" : FormatTime(ticks * Core.OfflineTime.BaseTickSeconds);
        _kind.Text = n.IsUpgrade ? $"REPEATABLE · {_r.Level(n.Id)} / {_r.MaxLevel(n.Id)}" : "ONE-OFF";
        _desc.Text = n.Description;
        _work.Text = state == NodeState.Done ? "" : _r.WorkDone(n.Id) > 0
            ? $"Work {Fmt.Short(_r.WorkDone(n.Id))} / {Fmt.Short(_r.WorkNeeded(n.Id))}"
            : $"Work {Fmt.Short(_r.WorkNeeded(n.Id))}";
        _need.Text = n.Requires.Count == 0 ? "" : "Requires: " + string.Join(", ", n.Requires.Select(x => _r.Node(x)?.Name ?? x));
        _need.AddThemeColorOverride("font_color", state == NodeState.Locked ? Tokens.Red : Tokens.Green);
        _hint.Text = state switch
        {
            NodeState.Done => n.IsUpgrade ? "Highest level reached." : "Researched.",
            NodeState.Researching => $"Researching on bench {_r.Benches.First(b => b.NodeId == n.Id).Index + 1}.",
            NodeState.Available => _r.Benches.Any(b => b.IsFree) ? "Click to start on a free bench, or drag it onto a bench" : "Every bench is busy. Drag it onto a bench to swap.",
            NodeState.Locked => "Research first: " + string.Join(", ", _r.MissingRequirements(n.Id).Select(x => _r.Node(x)?.Name ?? x)) + ".",
            _ => "",
        };
    }
}

/// <summary>
/// The node tree: circles (one-off) and rounded squares with a level badge (upgrade), lines to required nodes,
/// four states. Only visible nodes are drawn. Drag to pan, wheel or +/- to zoom, click to start research.
/// Layout from the data: x across, y = layer, roots at the bottom.
/// </summary>
public partial class ResearchTree : Control
{
    private const float CellX = 118, CellY = 104, NodeSize = 50;
    private readonly ResearchManager _r;
    private readonly ResearchTome _tome;
    private Vector2 _pan;
    private float _zoom = 0.9f;
    private bool _dragging, _moved, _centered;
    /// <summary>The node under the press, if it can be dragged to a bench (available or researching).</summary>
    private string? _pressNode;

    /// <summary>Drag data prefix for a research node ("research_node:&lt;id&gt;").</summary>
    public const string DragPrefix = "research_node:";

    public static string? NodeFromDrag(Variant data) =>
        data.VariantType == Variant.Type.String && data.AsString() is { } s && s.StartsWith(DragPrefix, StringComparison.Ordinal) ? s[DragPrefix.Length..] : null;

    public ResearchTree(ResearchManager r, ResearchTome tome)
    {
        _r = r;
        _tome = tome;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        Resized += () => { if (!_centered && Size.X > 0) { Center(); QueueRedraw(); } };
    }

    public string? Hovered { get; private set; }

    public event Action<string?>? HoverChanged;

    private IEnumerable<ResearchNodeDefinitionBase> VisibleNodes => _tome.Nodes.Where(n => _r.IsVisible(n.Id));

    public void ZoomBy(float f) => ZoomAt(Size / 2, f);

    private static Vector2 World(ResearchNodeDefinitionBase n) => new((float)(n.Layout.X * CellX), (float)(-n.Layout.Y * CellY));

    private Vector2 Screen(ResearchNodeDefinitionBase n) => _pan + World(n) * _zoom;

    /// <summary>
    /// Opening view: a readable zoom centred across on the nodes the player can work on now (done, researching,
    /// available), with them near the bottom so the locked future nodes show above. Drag and zoom out for the rest.
    /// </summary>
    private void Center()
    {
        var all = VisibleNodes.ToList();
        if (all.Count == 0) return;
        var reachable = all.Where(n => _r.State(n.Id) != NodeState.Locked).ToList();
        var focus = (reachable.Count > 0 ? reachable : all).Select(World).ToList();
        var min = new Vector2(focus.Min(p => p.X), focus.Min(p => p.Y));
        var max = new Vector2(focus.Max(p => p.X), focus.Max(p => p.Y));
        var span = max - min + new Vector2(260, 260);
        _zoom = Math.Clamp(Math.Min(Size.X / span.X, Size.Y / span.Y), 0.8f, 1.05f);
        var bottom = max.Y * _zoom;                                   // lowest focus node (roots sit at the bottom)
        _pan = new Vector2(Size.X / 2 - (min.X + max.X) / 2 * _zoom, Size.Y - 90 - bottom);
    }

    public override void _Draw()
    {
        var visible = VisibleNodes.ToList();
        var ids = visible.Select(n => n.Id).ToHashSet();
        foreach (var n in visible)
            foreach (var req in n.Requires.Where(ids.Contains))
            {
                var from = _r.Node(req)!;
                var state = _r.State(n.Id);
                var color = state == NodeState.Researching ? Tokens.Purple : _r.IsResearched(req) ? Tokens.GreenDeep : Tokens.DotOff;
                if (_r.IsResearched(req) || state == NodeState.Researching) DrawLine(Screen(from), Screen(n), color, 3 * _zoom, true);
                else DrawDashedLine(Screen(from), Screen(n), color, 2 * _zoom, 7 * _zoom, true);   // not reachable yet
            }
        foreach (var n in visible) DrawNode(n);
    }

    private void DrawNode(ResearchNodeDefinitionBase n)
    {
        var p = Screen(n);
        var s = NodeSize * _zoom;
        var state = _r.State(n.Id);
        var locked = state == NodeState.Locked;   // needs other nodes first: dimmed
        var ring = state switch
        {
            NodeState.Done => Tokens.GreenDeep,
            NodeState.Researching => Tokens.Purple,
            _ when n.IsUpgrade && _r.IsResearched(n.Id) => Tokens.GreenDeep,
            _ => Tokens.DotOff,
        };
        var hovered = n.Id == Hovered;
        var glow = hovered ? Tokens.Green with { A = 0.35f } : state == NodeState.Researching ? Tokens.Purple with { A = 0.3f } : (Color?)null;
        var rect = new Rect2(p - new Vector2(s, s) / 2, new Vector2(s, s));
        var alpha = locked ? 0.45f : 1f;
        if (n.IsUpgrade)
        {
            if (glow is { } g) DrawStyleBox(Tokens.Box(g, g, (int)(12 * _zoom)), rect.Grow(5 * _zoom));
            DrawStyleBox(Tokens.Box(Tokens.BgPanelDark, (hovered ? Tokens.Green : ring) with { A = alpha }, (int)(10 * _zoom), 0, 0, Math.Max(2, (int)(3 * _zoom))), rect);
            DrawStyleBox(Tokens.Flat(NodeKinds.DotColor(n) with { A = 0.9f * alpha }, (int)(6 * _zoom)), rect.Grow(-11 * _zoom));
            var badge = $"{_r.Level(n.Id)}/{_r.MaxLevel(n.Id)}";
            var bs = (int)Math.Max(9, 10 * _zoom);
            var bw = Tokens.Bold.GetStringSize(badge, HorizontalAlignment.Left, -1, bs).X + 8;
            var br = new Rect2(p + new Vector2(s / 2 - bw + 6 * _zoom, s / 2 - bs * 0.7f), new Vector2(bw, bs + 4));
            DrawStyleBox(Tokens.Box(Tokens.BgPanelDark, Tokens.Border, 5), br);
            DrawString(Tokens.Bold, br.Position + new Vector2(4, bs + 0.5f), badge, HorizontalAlignment.Left, -1, bs, Tokens.TextMain);
        }
        else
        {
            var r = s / 2;
            if (glow is { } g) DrawCircle(p, r + 6 * _zoom, g);
            DrawCircle(p, r, Tokens.BgPanelDark);
            DrawArc(p, r - 1, 0, Mathf.Tau, 48, (hovered ? Tokens.Green : ring) with { A = alpha }, 3 * _zoom, true);
            DrawCircle(p, r * 0.66f, NodeKinds.DotColor(n) with { A = 0.95f * alpha });
        }
        if (state == NodeState.Done)
        {
            var check = p + new Vector2(s * 0.36f, s * 0.36f);
            DrawCircle(check, 9 * _zoom, Tokens.GreenDeep);
            DrawPolyline(new[] { check + new Vector2(-4, 0) * _zoom, check + new Vector2(-1, 3) * _zoom, check + new Vector2(4, -3) * _zoom }, Colors.White, 2 * _zoom, true);
        }
        var size = (int)Math.Max(9, 11 * _zoom);
        var w = Tokens.Bold.GetStringSize(n.Name, HorizontalAlignment.Left, -1, size).X;
        DrawString(Tokens.Bold, p + new Vector2(-w / 2, s / 2 + size + 5 * _zoom), n.Name, HorizontalAlignment.Left, -1, size,
            locked ? Tokens.TextDim : Tokens.TextSoft);
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } w:
                ZoomAt(w.Position, 1.1f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } w:
                ZoomAt(w.Position, 1 / 1.1f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed)
                {
                    _moved = false;
                    _pressNode = Hit(mb.Position) is { } p && _r.State(p) is NodeState.Available or NodeState.Researching ? p : null;
                    _dragging = _pressNode is null;   // pressing on a node drags the node, elsewhere pans the tree
                }
                else
                {
                    _dragging = false;
                    _pressNode = null;
                    if (!_moved && Hit(mb.Position) is { } id) Click(id);
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion mm:
                if (_dragging && mm.ButtonMask.HasFlag(MouseButtonMask.Left) && mm.Relative.LengthSquared() > 0)
                {
                    _pan += mm.Relative;
                    _moved |= mm.Relative.Length() > 1;
                    _centered = true;
                    QueueRedraw();
                }
                var hit = Hit(mm.Position);
                if (hit != Hovered)
                {
                    Hovered = hit;
                    HoverChanged?.Invoke(hit);
                    QueueRedraw();
                }
                break;
        }
    }

    /// <summary>Godot asks for drag data once the mouse moves with the button down: a node pressed on starts a drag.</summary>
    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (_pressNode is not { } id || _r.Node(id) is not { } n) return default;
        _dragging = false;
        _pressNode = null;
        SetDragPreview(new NodeDragPreview(n.Name, NodeKinds.DotColor(n)));
        return DragPrefix + id;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd) { _dragging = false; _pressNode = null; }
    }

    private void ZoomAt(Vector2 at, float f)
    {
        _centered = true;
        var before = (at - _pan) / _zoom;
        _zoom = Math.Clamp(_zoom * f, 0.4f, 2f);
        _pan = at - before * _zoom;
        QueueRedraw();
    }

    private string? Hit(Vector2 at) => VisibleNodes
        .Where(n => Screen(n).DistanceTo(at) <= NodeSize * _zoom / 2 + 4)
        .OrderBy(n => Screen(n).DistanceTo(at)).Select(n => n.Id).FirstOrDefault();

    /// <summary>Starts an available node on the first free bench.</summary>
    public void Click(string id)
    {
        var n = _r.Node(id)!;
        if (_r.State(id) == NodeState.Locked)
        {
            Toast.Show($"{n.Name} needs {string.Join(", ", _r.MissingRequirements(id).Select(x => _r.Node(x)?.Name ?? x))} first.", true);
            return;
        }
        if (_r.State(id) != NodeState.Available) return;
        var res = _r.Start(id);
        Toast.Show(res switch
        {
            StartResult.Ok => $"Researching {n.Name}.",
            StartResult.NoFreeBench => "No free bench. Stop one or wait.",
            _ => $"Cannot start {n.Name}.",
        }, res != StartResult.Ok);
    }
}

/// <summary>A bench row that accepts a research node dropped from the tree (green border while it would accept).</summary>
public partial class BenchDropTarget : PanelContainer
{
    private readonly Func<string, bool> _canDrop;
    private readonly Action<string> _drop;

    public BenchDropTarget(Func<string, bool> canDrop, Action<string> drop)
    {
        _canDrop = canDrop;
        _drop = drop;
        MouseFilter = MouseFilterEnum.Stop;
        MouseExited += () => Hot = false;
    }

    /// <summary>A node is being dragged over and would be accepted.</summary>
    public bool Hot
    {
        get => _hot;
        private set
        {
            if (_hot == value) return;
            _hot = value;
            Restyle?.Invoke(value);
        }
    }
    private bool _hot;

    /// <summary>Set by the page: applies the normal or the drop-highlight style.</summary>
    public Action<bool>? Restyle { get; set; }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        var ok = ResearchTree.NodeFromDrag(data) is { } id && _canDrop(id);
        Hot = ok;
        return ok;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        Hot = false;
        if (ResearchTree.NodeFromDrag(data) is { } id) _drop(id);
    }

    /// <summary>Set by the page: a left click on the bench (not on its stepper or buttons) researches by hand.</summary>
    public Action? Clicked { get; set; }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } && Clicked is { } c)
        {
            c();
            AcceptEvent();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd) Hot = false;
    }
}

/// <summary>What follows the mouse while a node is dragged: its colour dot and name.</summary>
public partial class NodeDragPreview : PanelContainer
{
    public NodeDragPreview(string name, Color dot)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgPopup, Tokens.Green, Tokens.RadiusInset, 10, 6, 2));
        var row = Ui.Row(8, Ui.Dot(dot, 14), Ui.Bold(name, Tokens.FontSmall));
        row.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(row);
        Modulate = new Color(1, 1, 1, 0.9f);
    }
}
