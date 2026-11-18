using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Crypt.Units;
using LittleLich.Managers;
using LittleLich.UI.Components;
using LittleLich.UI.Theme;
using static LittleLich.UI.Pages.Kit;

namespace LittleLich.UI.Pages;

/// <summary>
/// Army Builder (adjustment 03, ui-reference 14-army-builder): a planning screen. Units box on the left (Base /
/// Custom toggle, one card per unlocked unit), the 3 by 3 formation on the right (Back, Mid, Front; the enemy is to
/// the right), a hint line and the Details box that follows the pointer. Drag a card onto a slot, or click a card
/// then a slot; drag between slots to swap; x clears. Only calls the Unit Builder's commands and redraws on its events.
///
/// Geometry is the 680 px mockup scaled to the content area (1030 px): units box 25%, slot gap 6, slot height 70,
/// details min height 118, all times <see cref="S"/>. Colours, borders, radii and type sizes are tokens, mapped by role:
/// panel = BgCard / BorderCard, unit card = BgCardAlt, accent = SelectedBg / SelectedBorder / Green.
/// </summary>
public partial class ArmyPage : PageBase
{
    /// <summary>Mockup to content area scale (1030 / 680).</summary>
    public const float S = (Tokens.RefWidth - Tokens.SidebarWidth - 40) / 680f;

    public const string UnitDragPrefix = "army_unit:";
    public const string SlotDragPrefix = "army_slot:";

    private static UnitBuilder? U => CryptMgr.Units;

    private bool _custom;
    private string? _selected;
    private string? _hover;
    private bool _unitsDirty, _slotsDirty;

    private VBoxContainer? _list;
    private ActionButton? _baseTab, _customTab;
    private readonly List<ArmySlot> _slots = new();
    private PanelContainer? _details;
    private readonly List<UnitCard> _cards = new();

    public override void Bind()
    {
        base.Bind();
        CryptMgr.ArmyFormationChanged += () => _slotsDirty = true;
        CryptMgr.UnlockedUnitsChanged += () => _unitsDirty = true;
        CryptMgr.UnitTypesChanged += () => _unitsDirty = true;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_unitsDirty) { _unitsDirty = false; RefreshUnits(); }
        if (_slotsDirty) { _slotsDirty = false; RefreshSlots(); RefreshDetails(); }
    }

    protected override void Build()
    {
        _slots.Clear();
        _cards.Clear();
        if (U is not { } u) return;
        var row = Ui.Row((int)(12 * S));
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        row.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(row);

        // ---- Units box (25%, full height, scrolls inside) ----
        var box = Panel();
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.SizeFlagsStretchRatio = 1;
        box.SizeFlagsVertical = SizeFlags.ExpandFill;
        row.AddChild(box);
        var col = Ui.Col((int)(6 * S));
        box.AddChild(col);
        col.AddChild(Ui.Heading("Units", Tokens.FontCardTitle));
        col.AddChild(Toggle());
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list = Ui.Col((int)(5 * S));
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_list);
        col.AddChild(scroll);

        // ---- Formation (75%) ----
        var right = Ui.Col(0);
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        right.SizeFlagsStretchRatio = 3;
        right.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(right);
        var gap = (int)Math.Round(6 * S);
        var heads = Grid(gap);
        foreach (var h in new[] { "Back", "Mid", "Front" })
        {
            var l = Tag(h);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            heads.AddChild(l);
        }
        right.AddChild(heads);
        right.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4 * S), MouseFilter = MouseFilterEnum.Ignore });
        var grid = Grid(gap);
        grid.AddThemeConstantOverride("v_separation", gap);
        right.AddChild(grid);
        for (var i = 0; i < ArmyFormation.SlotCount; i++)
        {
            var slot = new ArmySlot(this, i) { CustomMinimumSize = new Vector2(0, 70 * S), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _slots.Add(slot);
            grid.AddChild(slot);
        }
        right.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6 * S), MouseFilter = MouseFilterEnum.Ignore });
        var hint = Tag("Drag a unit onto a slot, or click a unit then a slot. Drag between slots to swap.");
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(hint);
        right.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10 * S), MouseFilter = MouseFilterEnum.Ignore });
        _details = Panel();
        _details.CustomMinimumSize = new Vector2(0, 118 * S);
        right.AddChild(_details);

        RefreshUnits();
        RefreshSlots();
        RefreshDetails();
        Update(RefreshDetails);   // stock changes
    }

    // ---- Units box ----

    private Control Toggle()
    {
        var frame = new PanelContainer();
        frame.AddThemeStyleboxOverride("panel", Tokens.Box(Colors.Transparent, Tokens.BorderCard, Tokens.RadiusButton));
        var r = Ui.Row(0);
        frame.AddChild(r);
        _baseTab = Segment("Base", false);
        _customTab = Segment("Custom", true);
        r.AddChild(_baseTab);
        r.AddChild(_customTab);
        return frame;
    }

    private ActionButton Segment(string text, bool custom)
    {
        var b = new ActionButton(text, padX: 0, padY: (int)(5 * S)) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        b.Pressed += () =>
        {
            if (_custom == custom) return;
            _custom = custom;
            RefreshUnits();
        };
        return b;
    }

    private static void StyleSegment(ActionButton b, bool on)
    {
        var sb = Tokens.Box(on ? Tokens.SelectedBg : Colors.Transparent, Colors.Transparent, Tokens.RadiusButton, 0, (int)(5 * S), 0);
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" }) b.AddThemeStyleboxOverride(state, sb);
        b.AddThemeColorOverride("font_color", on ? Tokens.Green : Tokens.TextMuted);
        b.AddThemeColorOverride("font_hover_color", on ? Tokens.Green : Tokens.TextMain);
        b.AddThemeFontOverride("font", on ? Tokens.Bold : Tokens.Body);
    }

    private void RefreshUnits()
    {
        if (U is not { } u || _list is null) return;
        if (_baseTab is not null) StyleSegment(_baseTab, !_custom);
        if (_customTab is not null) StyleSegment(_customTab, _custom);
        foreach (var c in _list.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); }
        _cards.Clear();
        if (_selected is not null && !u.IsUnlockedUnit(_selected)) _selected = null;
        var ids = u.GetUnlockedUnitIds(_custom);
        if (ids.Count == 0)
        {
            var t = Tag(_custom ? "No custom units yet. Build them in the Unit Builder." : "No units yet. Research unlocks them.");
            t.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _list.AddChild(t);
            return;
        }
        foreach (var id in ids)
        {
            var card = new UnitCard(this, id, u.UnitName(id), $"{u.UnitClass(id)} · HP {Fmt.Short(u.BattleUnit(id).Hp)}");
            _cards.Add(card);
            _list.AddChild(card);
        }
        RestyleCards();
    }

    private void RestyleCards()
    {
        foreach (var c in _cards) c.Restyle(c.UnitId == _selected);
    }

    // ---- Called by cards and slots ----

    internal void ToggleSelected(string unitId)
    {
        _selected = _selected == unitId ? null : unitId;
        RestyleCards();
    }

    internal void ClickSlot(int index)
    {
        if (_selected is not { } id) return;
        if (!Place(index, id)) return;
        _selected = null;
        RestyleCards();
    }

    internal bool Place(int index, string unitId)
    {
        if (U is not { } u) return false;
        if (Fighting()) return false;
        if (!u.SetSlot(index, unitId)) { Toast.Show($"{u.UnitName(unitId)} is not unlocked.", true); return false; }
        return true;
    }

    internal void Swap(int from, int to)
    {
        if (U is not { } u || Fighting()) return;
        u.SwapSlots(from, to);
    }

    internal void Clear(int index)
    {
        if (U is not { } u || Fighting()) return;
        u.ClearSlot(index);
    }

    private static bool Fighting()
    {
        if (WorldManager.Instance.State?.Battle is null) return false;
        Toast.Show("The army is fighting. Change the formation after the battle.", true);
        return true;
    }

    internal void Hover(string? unitId)
    {
        if (_hover == unitId) return;
        _hover = unitId;
        RefreshDetails();
    }

    internal void Unhover(string unitId)
    {
        if (_hover != unitId) return;
        _hover = null;
        RefreshDetails();
    }

    // ---- Formation ----

    private void RefreshSlots()
    {
        if (U is not { } u) return;
        foreach (var s in _slots) s.SetUnit(u.GetSlot(s.Index) is { } id ? (id, u.UnitName(id), u.UnitClass(id)) : null);
    }

    // ---- Details box ----

    private void RefreshDetails()
    {
        if (_details is null || U is not { } u) return;
        foreach (var c in _details.GetChildren()) { _details.RemoveChild(c); c.QueueFree(); }
        if (_hover is null || !u.IsKnownUnit(_hover))
        {
            var empty = Ui.Col(4);
            empty.MouseFilter = MouseFilterEnum.Ignore;
            empty.AddChild(Ui.Heading("Details", Tokens.FontCardTitle));
            empty.AddChild(Tag("Hover over a unit to see its stats and actions."));
            _details.AddChild(empty);
            return;
        }
        var d = u.GetUnitDetails(_hover);
        var cols = Ui.Row((int)(12 * S));
        cols.MouseFilter = MouseFilterEnum.Ignore;
        _details.AddChild(cols);

        var stats = Column(cols);
        stats.AddChild(Ui.Heading(d.Name, Tokens.FontCardTitle));
        stats.AddChild(Wrap(Tag(d.IsCustom ? $"Custom · {d.BuiltFrom}" : $"Base · {d.Type}")));
        stats.AddChild(Line("Health", Fmt.Short(d.Health)));
        stats.AddChild(Line("Damage", Fmt.Num2(d.Damage)));
        stats.AddChild(Line("Attack speed", d.AttackSpeed > 0 ? $"{Fmt.Num1(d.AttackSpeed)}s" : "—"));

        var acts = Column(cols);
        acts.AddChild(Ui.Heading("Actions", Tokens.FontCardTitle));
        foreach (var a in d.Actions)
        {
            var block = Ui.Col(1);
            block.MouseFilter = MouseFilterEnum.Ignore;
            block.AddChild(Ui.Row(6, Ui.Bold(a.Name, Tokens.FontBody), Ui.Fill(), Tag($"every {Fmt.Num1(a.CooldownSeconds)}s")));
            block.AddChild(Wrap(Tag(a.Description)));
            acts.AddChild(block);
        }
        if (d.Actions.Count == 0) acts.AddChild(Tag("No actions."));

        var supply = Column(cols);
        supply.AddChild(Ui.Heading("Supply", Tokens.FontCardTitle));
        supply.AddChild(Tag("Uses"));
        supply.AddChild(Wrap(Ui.Text(d.SummonCostText.Length > 0 ? d.SummonCostText : "Nothing", Tokens.FontBody)));
        supply.AddChild(Line("Stock", Fmt.Short(d.Stock)));
        supply.AddChild(Line("In formation", $"{u.CountInFormation(d.Id)} of {ArmyFormation.SlotCount}"));
    }

    private static VBoxContainer Column(HBoxContainer into)
    {
        var c = Ui.Col(4);
        c.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        c.SizeFlagsStretchRatio = 1;
        c.MouseFilter = MouseFilterEnum.Ignore;
        into.AddChild(c);
        return c;
    }

    private static Control Line(string label, string value)
    {
        var r = Ui.Row(8, Tag(label), Ui.Fill(), Ui.Text(value, Tokens.FontBody));
        r.MouseFilter = MouseFilterEnum.Ignore;
        return r;
    }

    // ---- Small builders ----

    /// <summary>Panel role (Units box, Details box): BgCard fill, BorderCard border, padding 8 x scale.</summary>
    internal static PanelContainer Panel()
    {
        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Pass };
        p.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgCard, Tokens.BorderCard, Tokens.RadiusButton, (int)(8 * S), (int)(8 * S)));
        return p;
    }

    /// <summary>The 11 px tag style (secondary text).</summary>
    internal static Label Tag(string text, Color? color = null)
    {
        var l = Ui.Text(text, Tokens.FontLabel, color ?? Tokens.TextMuted);
        l.MouseFilter = MouseFilterEnum.Ignore;
        return l;
    }

    private static Label Wrap(Label l)
    {
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return l;
    }

    private static GridContainer Grid(int gap)
    {
        var g = new GridContainer { Columns = ArmyFormation.Columns, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        g.AddThemeConstantOverride("h_separation", gap);
        return g;
    }

    internal static string? UnitFromDrag(Variant data) => Prefixed(data, UnitDragPrefix);

    internal static int? SlotFromDrag(Variant data) => Prefixed(data, SlotDragPrefix) is { } s && int.TryParse(s, out var i) ? i : null;

    private static string? Prefixed(Variant data, string prefix) =>
        data.VariantType == Variant.Type.String && data.AsString() is { } s && s.StartsWith(prefix, StringComparison.Ordinal) ? s[prefix.Length..] : null;

    /// <summary>True while the pointer is inside the control (children such as the x button do not count as leaving).</summary>
    internal static bool PointerInside(Control c) => c.GetGlobalRect().HasPoint(c.GetGlobalMousePosition());
}

/// <summary>A unit in the Units box: name, then "Type · HP n". Drag it onto a slot; click to select (again to deselect).</summary>
public partial class UnitCard : PanelContainer
{
    private readonly ArmyPage _page;
    private bool _selected, _hot, _dragged;

    public UnitCard(ArmyPage page, string unitId, string name, string line)
    {
        _page = page;
        UnitId = unitId;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.Drag;
        var col = Ui.Col(1);
        col.MouseFilter = MouseFilterEnum.Ignore;
        var n = Ui.Bold(name, Tokens.FontBody);
        n.MouseFilter = MouseFilterEnum.Ignore;
        n.ClipText = true;
        col.AddChild(n);
        col.AddChild(ArmyPage.Tag(line));
        AddChild(col);
        MouseEntered += () => { _hot = true; Restyle(_selected); _page.Hover(UnitId); };
        MouseExited += () =>
        {
            if (ArmyPage.PointerInside(this)) return;
            _hot = false;
            Restyle(_selected);
            _page.Unhover(UnitId);
        };
        Restyle(false);
    }

    public string UnitId { get; }

    /// <summary>Unit card role: BgCardAlt, BorderCard; hover and selected: accent border; selected also accent fill.</summary>
    public void Restyle(bool selected)
    {
        _selected = selected;
        var border = selected || _hot ? Tokens.SelectedBorder : Tokens.BorderCard;
        AddThemeStyleboxOverride("panel", Tokens.Box(selected ? Tokens.SelectedBg : Tokens.BgCardAlt, border, Tokens.RadiusButton, (int)(8 * ArmyPage.S), (int)(5 * ArmyPage.S)));
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed) _dragged = false;
            else if (!_dragged) { _page.ToggleSelected(UnitId); AcceptEvent(); }
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        _dragged = true;
        SetDragPreview(new NodeDragPreview(CryptManager.Instance.Units?.UnitName(UnitId) ?? UnitId, Tokens.Green));
        return ArmyPage.UnitDragPrefix + UnitId;
    }
}

/// <summary>
/// One formation slot. Filled: accent fill and border, the unit name, its type and an x in the top right; drag it to
/// another slot to swap. Empty: no fill, dashed border, "Empty". Takes unit cards and other slots; outlined in the
/// accent colour while a drop would land. Click places the selected card.
/// </summary>
public partial class ArmySlot : PanelContainer
{
    private readonly ArmyPage _page;
    private readonly VBoxContainer _body;
    private readonly Button _x;
    private string? _unit;
    private bool _hot;

    public ArmySlot(ArmyPage page, int index)
    {
        _page = page;
        Index = index;
        MouseFilter = MouseFilterEnum.Stop;
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        _body = Ui.Col(1);
        _body.MouseFilter = MouseFilterEnum.Ignore;
        center.AddChild(_body);
        AddChild(center);
        var overlay = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(overlay);
        _x = new Button { Text = "×", Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Clear this slot", MouseDefaultCursorShape = CursorShape.PointingHand };
        _x.AddThemeFontSizeOverride("font_size", Tokens.FontBody);
        _x.AddThemeColorOverride("font_color", Tokens.TextMuted);
        _x.AddThemeColorOverride("font_hover_color", Tokens.TextMain);
        foreach (var state in new[] { "normal", "hover", "pressed", "focus" }) _x.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        _x.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        _x.OffsetLeft = -22;
        _x.OffsetRight = -2;
        _x.OffsetTop = 0;
        _x.OffsetBottom = 20;
        _x.Pressed += () => _page.Clear(Index);
        overlay.AddChild(_x);
        MouseEntered += () => { if (_unit is { } u) _page.Hover(u); };
        MouseExited += () =>
        {
            if (ArmyPage.PointerInside(this)) return;
            if (_unit is { } u) _page.Unhover(u);
            Hot = false;
        };
    }

    public int Index { get; }

    /// <summary>Shows a unit (id, name, type) or the empty state.</summary>
    public void SetUnit((string Id, string Name, string Type)? unit)
    {
        var was = _unit;
        _unit = unit?.Id;
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        if (unit is { } u)
        {
            var n = Ui.Bold(u.Name, Tokens.FontBody, Tokens.Green);
            n.HorizontalAlignment = HorizontalAlignment.Center;
            n.MouseFilter = MouseFilterEnum.Ignore;
            _body.AddChild(n);
            var t = ArmyPage.Tag(u.Type, Tokens.Green with { A = 0.8f });
            t.HorizontalAlignment = HorizontalAlignment.Center;
            _body.AddChild(t);
        }
        else _body.AddChild(ArmyPage.Tag("Empty", Tokens.TextDim));
        _x.Visible = unit is not null;
        MouseDefaultCursorShape = unit is not null ? CursorShape.Drag : CursorShape.Arrow;
        Restyle();
        // The pointer is on this slot and its unit changed: the details follow.
        if (was != _unit && ArmyPage.PointerInside(this))
        {
            if (was is not null) _page.Unhover(was);
            if (_unit is not null) _page.Hover(_unit);
        }
    }

    private bool Hot
    {
        set
        {
            if (_hot == value) return;
            _hot = value;
            Restyle();
        }
    }

    /// <summary>Filled: accent fill, accent border, solid. Empty: no fill, dashed border (drawn). Drop target: accent outline.</summary>
    private void Restyle()
    {
        var pad = (int)(4 * ArmyPage.S);
        StyleBoxFlat sb;
        if (_unit is not null) sb = Tokens.Box(Tokens.SelectedBg, _hot ? Tokens.Green : Tokens.SelectedBorder, Tokens.RadiusButton, pad, pad, _hot ? 2 : 1);
        else sb = Tokens.Box(Colors.Transparent, _hot ? Tokens.Green : Colors.Transparent, Tokens.RadiusButton, pad, pad, _hot ? 2 : 0);
        AddThemeStyleboxOverride("panel", sb);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_unit is not null || _hot) return;
        // Dashed strong border for an empty slot.
        var r = new Rect2(Vector2.One * 0.5f, Size - Vector2.One);
        var c = Tokens.BorderCard.Lightened(0.15f);
        const float dash = 5;
        DrawDashedLine(r.Position, new Vector2(r.End.X, r.Position.Y), c, 1, dash);
        DrawDashedLine(new Vector2(r.End.X, r.Position.Y), r.End, c, 1, dash);
        DrawDashedLine(r.End, new Vector2(r.Position.X, r.End.Y), c, 1, dash);
        DrawDashedLine(new Vector2(r.Position.X, r.End.Y), r.Position, c, 1, dash);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            _page.ClickSlot(Index);
            AcceptEvent();
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (_unit is null) return default;
        SetDragPreview(new NodeDragPreview(CryptManager.Instance.Units?.UnitName(_unit) ?? _unit, Tokens.Green));
        return ArmyPage.SlotDragPrefix + Index;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        var ok = ArmyPage.UnitFromDrag(data) is not null || (ArmyPage.SlotFromDrag(data) is { } from && from != Index);
        Hot = ok;
        return ok;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        Hot = false;
        if (ArmyPage.UnitFromDrag(data) is { } unit) _page.Place(Index, unit);
        else if (ArmyPage.SlotFromDrag(data) is { } from) _page.Swap(from, Index);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd) Hot = false;
    }
}
