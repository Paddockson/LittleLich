using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Crypt.Buildings;
using LittleLich.Managers;
using LittleLich.UI.Components;
using LittleLich.UI.Shell;
using LittleLich.UI.Theme;
using static LittleLich.UI.Pages.Kit;

namespace LittleLich.UI.Pages;

/// <summary>
/// Workshop (images 01, 02): buildings list (w172) with status dots, then the selected building's own page.
/// Mana Well and Summoning Circle (adjustment 01: circle, summon slots, level) are drawn as in the mockups; the others get a level/staff card and a card with
/// their own settings until they are designed.
/// </summary>
public partial class WorkshopPage : PageBase, ISubPage
{
    private string _selected = BuildingIds.ManaWell;
    private readonly Dictionary<string, (NavItem Item, Dot Dot)> _list = new();

    public override void Bind()
    {
        base.Bind();
        CryptMgr.BuildingStatusChanged += (_, _, _) => MarkDirty();
        CryptMgr.BuildingLevelChanged += (_, _) => Rebuild();
        CryptMgr.UnitTypesChanged += () => { if (_selected == BuildingIds.SummoningCircle) Rebuild(); };
        CryptMgr.Unlocks.Unlocked += (kind, _) => { if (kind is LittleLich.Core.Unlocks.Building or LittleLich.Core.Unlocks.Feature or LittleLich.Core.Unlocks.Unit) RebuildSoon(); };
    }

    public void Select(string id)
    {
        if (CryptMgr.Building(id) is not { IsUnlocked: true }) return;
        _selected = id;
        Rebuild();
    }

    protected override void Build()
    {
        _list.Clear();
        var row = CardRow();

        // Buildings list
        var list = Ui.Width(Ui.Col(8), 172);
        list.AddChild(Ui.Section("Buildings"));
        foreach (var b in CryptMgr.Buildings.Where(b => b.IsUnlocked))   // nothing locked is drawn
        {
            var item = new NavItem(13, 10);
            var dot = Ui.Dot(StatusColor(b), 8);
            item.Body.AddChild(Ui.Row(8, Ui.Bold(b.Name, Tokens.FontBody), Ui.Fill(), dot));
            item.Selected = b.Id == _selected;
            var id = b.Id;
            item.Pressed += () => Select(id);
            list.AddChild(item);
            _list[b.Id] = (item, dot);
        }
        list.AddChild(Ui.Muted("Green dot: running. Yellow: needs input.", Tokens.FontLabel));
        row.AddChild(list);
        Update(() =>
        {
            foreach (var b in CryptMgr.Buildings.Where(b => b.IsUnlocked))
                if (_list.TryGetValue(b.Id, out var e))
                {
                    e.Dot.Color = StatusColor(b);
                    e.Item.TooltipText = $"{b.Name}, level {b.Level}: {StatusText(b)}";
                }
        });

        var building = CryptMgr.Building(_selected) is { IsUnlocked: true } sel ? sel : CryptMgr.Buildings.FirstOrDefault(b => b.IsUnlocked);
        if (building is null) return;
        switch (building)
        {
            case ManaWell w: BuildManaWell(row, w); break;
            case SummoningCircle c: BuildCircle(row, c); break;
            default: BuildGeneric(row, building); break;
        }
    }

    // ---- Shared pieces ----

    private void Header(VBoxContainer into, BuildingBase b, string title, string description)
    {
        var pill = new Pill();
        into.AddChild(Ui.Row(8, Ui.Heading(title, Tokens.FontTitle), Ui.Fill(), pill));
        into.AddChild(Ui.Muted(description));
        Update(() =>
        {
            var (text, kind) = StatusPill(b);
            pill.Text = text;
            pill.SetKind(kind);
            pill.TooltipText = StatusText(b);
        });
    }

    /// <summary>"Lvl. 11 | 3 / 11 necromancers | +7,244 /tick | [Level up · 4.2k gold]".</summary>
    private void StatsRow(VBoxContainer into, BuildingBase b, Func<string> output, string outputTip)
    {
        var inset = new InsetRow(14);
        var lvl = Ui.Bold("", Tokens.FontNumber);
        var staff = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
        var rate = Ui.Bold("", Tokens.FontNumber, Tokens.Blue);
        rate.TooltipText = outputTip;
        var up = new ActionButton("", primary: true, padX: 10, padY: 6);
        up.Pressed += () =>
        {
            if (!CryptMgr.TryUpgrade(b.Id)) Toast.Show($"Cannot level up {b.Name}: needs {Costs(b.UpgradeCosts)}.", true);
            else Toast.Show($"{b.Name} is now level {b.Level}.");
        };
        inset.Body.AddChild(lvl);
        inset.Body.AddChild(staff);
        inset.Body.AddChild(rate);
        inset.Body.AddChild(Ui.Fill());
        inset.Body.AddChild(up);
        into.AddChild(inset);
        Update(() =>
        {
            lvl.Text = $"Lvl. {b.Level}";
            lvl.TooltipText = $"Level {b.Level} of {b.MaxLevel}";
            staff.Text = $"{Fmt.Short(b.Staffed)} / {Fmt.Short(b.Slots)} necromancers";
            staff.TooltipText = $"{Fmt.Exact(b.Staffed)} of {Fmt.Exact(b.Slots)} slots staffed";
            rate.Text = output();
            up.Text = b.IsMaxLevel ? "Max level" : $"Level up · {CostText(b.UpgradeCosts)}";
            up.Disabled = b.IsMaxLevel || !b.CanAffordUpgrade;
            up.TooltipText = b.IsMaxLevel ? "Highest level reached (research raises the cap)." :
                string.Join("\n", b.UpgradeCosts.Select(c => $"{Name(c.Id)}: {Fmt.Exact(Owned(c.Id))} / {Fmt.Exact(c.Count)}"));
        });
    }

    /// <summary>Section + step selector + staff stepper + per-necromancer note.</summary>
    private void StaffBlock(VBoxContainer into, BuildingBase b, string label)
    {
        if (b.Slots <= 0 && b.Staffed <= 0) return;
        into.AddChild(Ui.Section(label));
        var stepper = StaffStepper(b);
        var note = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
        into.AddChild(Ui.Row(10, stepper, note));
        into.AddChild(new StepSelector());
        Update(() =>
        {
            stepper.Refresh();
            note.Text = $"+{Fmt.Num2(b.Definition.WorkPerNecromancer * b.LevelMultiplier)} work each · {Fmt.Short(b.FreeSlots)} slots open";
        });
    }

    private static string NextLevelText(BuildingBase b) =>
        b.IsMaxLevel ? "Highest level reached." :
        $"Next level ({b.Level + 1}): {(b.Definition.SlotsPerLevel > 0 ? $"+{Fmt.Short(b.Definition.SlotsPerLevel)} slots and " : "")}" +
        $"+{b.Definition.LevelBonus:P0} output.";

    // ---- 1a. Mana Well (image 01, adjustment 02) ----

    private void BuildManaWell(HBoxContainer row, ManaWell w)
    {
        row.AddChild(WellCard(w));
        row.AddChild(WellNecroCard(w));
        row.AddChild(LevelCard(w, "Well level", "Each level adds output and room for more necromancers."));
    }

    /// <summary>"+8.00 /tick" (two decimals; short form from 1,000).</summary>
    private static string PerTick(double v) =>
        (v < 1000 ? "+" + v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "+" + Fmt.Short(v)) + " /tick";

    /// <summary>Card A: the clickable well, the OUTPUT box and the mana stored bar.</summary>
    private Control WellCard(ManaWell w)
    {
        var (card, body) = ScrollCard(280, gap: 10);
        Header(body, w, "Mana Well", "Click the well to channel mana yourself. Necromancers channel it on their own every tick.");

        var well = new CircleButton("", 200, ClickBlockStyle.Well);
        well.OnClick = () => w.Click() is > 0 and var n ? $"+{Fmt.Short(n)}" : null;
        body.AddChild(well);

        var outBox = new PanelContainer();
        outBox.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgInset, Tokens.WellBorder, Tokens.RadiusInset, 12, 10));
        var output = Ui.Bold("", 20, Tokens.Blue);
        outBox.AddChild(Ui.Row(8, Ui.Dot(Tokens.Blue, 18), Ui.Text("OUTPUT", Tokens.FontLabel, Tokens.TextDim, Tokens.LabelFont), Ui.Fill(), output));
        body.AddChild(outBox);

        var (kv, stored) = KeyValue($"{Name(w.Output)} stored");
        body.AddChild(kv);
        var bar = new StatBar(Tokens.Blue, 8);
        body.AddChild(bar);

        Update(() =>
        {
            well.Hint.Text = $"+{Fmt.Num2(w.ClickPower)} {Name(w.Output).ToLowerInvariant()} per click";
            output.Text = PerTick(w.OutputPerTick());
            output.TooltipText = $"{Fmt.Exact(w.OutputPerTick())} {Name(w.Output).ToLowerInvariant()} per tick";
            var stock = CryptMgr.Storage.Get(w.Output);
            var cap = CryptMgr.Storage.Cap(w.Output);
            stored.Text = $"{Fmt.Short(stock)} / {Fmt.Short(cap)}";
            stored.TooltipText = $"{Fmt.Exact(stock)} / {Fmt.Exact(cap)}. Mana stops at the cap.";
            bar.Value = cap > 0 ? (double)stock / cap : 0;
        });
        return card;
    }

    /// <summary>Card B: the necromancer block (count / max with - and +), Assign by, and the output breakdown.</summary>
    private Control WellNecroCard(ManaWell w)
    {
        var (card, body) = ScrollCard(276, gap: 10);
        body.AddChild(Ui.Heading("Necromancers"));
        body.AddChild(Ui.Muted("Each necromancer at the well adds mana every tick. Raise the well's level to hold more."));

        var block = new PanelContainer();
        block.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.NecroBg, Tokens.NecroBorder, Tokens.RadiusInset, 12, 12));
        var icon = new PanelContainer { CustomMinimumSize = new Vector2(46, 46), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        icon.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.LockedFill, Tokens.PurpleDeep, Tokens.RadiusButton, 0, 0, 2));
        var iconText = Ui.Bold("NECRO", 10, Tokens.NecroText);
        iconText.HorizontalAlignment = HorizontalAlignment.Center;
        iconText.VerticalAlignment = VerticalAlignment.Center;
        icon.AddChild(iconText);
        var count = Ui.Bold("", 22);
        count.MouseFilter = MouseFilterEnum.Pass;
        var minus = new ActionButton("−", padX: 0, padY: 0, radius: 6, fontSize: 16) { CustomMinimumSize = new Vector2(30, 30), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var plus = new ActionButton("+", primary: true, padX: 0, padY: 0, radius: 6, fontSize: 16) { CustomMinimumSize = new Vector2(30, 30), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        minus.Pressed += () => { w.RemoveNecromancer(Clamp(StepSelector.Current, w.Staffed)); MarkDirty(); };
        plus.Pressed += () => { w.AddNecromancer(Clamp(StepSelector.Current, Lich.State.Pool.Free)); MarkDirty(); };
        block.AddChild(Ui.Row(10, icon, count, Ui.Fill(), minus, plus));
        body.AddChild(block);
        body.AddChild(new StepSelector());

        body.AddChild(Ui.Section("Output breakdown"));
        var (_, perNecro) = BreakdownRow(body, "Per necromancer", Tokens.Blue);
        var (necroLabel, necroVal) = BreakdownRow(body, "Necromancers", Tokens.Blue);
        var (_, level) = BreakdownRow(body, "Level bonus", Tokens.Blue);
        var (modLabel, modVal) = BreakdownRow(body, "Other modifiers", Tokens.Blue);
        var (_, click) = BreakdownRow(body, "Click power", Tokens.TextMain);

        body.AddChild(Ui.Expand(new Control { SizeFlagsVertical = SizeFlags.ExpandFill }));
        body.AddChild(Ui.Text("Free necromancers come from the Summoning Circle. The pool is shown in the top bar.",
            Tokens.FontLabel, Tokens.TextDim, wrap: true));

        Update(() =>
        {
            var staffed = w.AssignedNecromancers;
            var max = w.MaxNecromancers;
            count.Text = $"{Fmt.Short(staffed)} / {Fmt.Short(max)}";
            count.TooltipText = $"{Fmt.Exact(staffed)} assigned, the well holds {Fmt.Exact(max)} at level {w.Level}";
            var free = Lich.State.Pool.Free;
            minus.Disabled = staffed <= 0;
            plus.Disabled = free <= 0 || w.FreeSlots <= 0;
            plus.TooltipText = w.FreeSlots <= 0 ? "The well is full. Raise its level to hold more."
                : free <= 0 ? "No free necromancers. Summon more in the Summoning Circle." : "Assign necromancers (Assign by sets how many)";
            minus.TooltipText = "Take necromancers off the well";
            perNecro.Text = PerTick(w.OutputPerNecromancer);
            necroLabel.Text = $"Necromancers ({Fmt.Short(staffed)})";
            necroVal.Text = PerTick(Math.Min(staffed, max) * w.OutputPerNecromancer);
            level.Text = Bonus(w.LevelBonusAt(w.Level));
            modVal.Text = $"x{Fmt.Num2(w.OutputModifier)}";
            ((Control)modLabel.GetParent().GetParent()).Visible = Math.Abs(w.OutputModifier - 1) > 1e-9;
            click.Text = Fmt.Num2(w.ClickPower);
        });
        return card;
    }

    /// <summary>One OUTPUT BREAKDOWN row: muted label left, bold value right.</summary>
    private static (Label Label, Label Value) BreakdownRow(VBoxContainer into, string label, Color valueColor)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgInset, Tokens.Border, Tokens.RadiusButton, 10, 7));
        var l = Ui.Text(label, Tokens.FontSmall, Tokens.TextMuted);
        var v = Ui.Bold("", Tokens.FontSmall, valueColor);
        p.AddChild(Ui.Row(8, l, Ui.Fill(), v));
        into.AddChild(p);
        return (l, v);
    }

    // ---- 1b. Summoning Circle (image 02, adjustment 01) ----

    private Control? _chooser, _chooserScrim, _tooltip;

    private void BuildCircle(HBoxContainer row, SummoningCircle c)
    {
        _chooser = _chooserScrim = _tooltip = null;
        row.AddChild(CircleCard(c));
        row.AddChild(SlotsCard(c));
        row.AddChild(LevelCard(c, "Circle level", "Each level adds output. New slots and units appear as they are unlocked."));
    }

    /// <summary>Card A: the clickable circle, the necromancer stepper and the power footer.</summary>
    private Control CircleCard(SummoningCircle c)
    {
        var (card, body) = ScrollCard(246, gap: 10);
        Header(body, c, "Summoning Circle",
            "Click the circle to add click power to the unit being summoned. Necromancers add power on their own every tick.");

        var circle = new CircleButton("");
        circle.OnClick = () => c.Click() ? $"+{Fmt.Num2(c.ClickPower)}" : null;
        body.AddChild(circle);

        var staff = new InsetRow(8);
        var stepper = StaffStepper(c);
        stepper.Max = () => c.Slots;
        stepper.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        staff.Body.AddChild(stepper);
        staff.Body.AddChild(Ui.Fill());
        staff.Body.AddChild(Ui.Text("necromancers", Tokens.FontLabel, Tokens.TextMuted));
        body.AddChild(staff);
        body.AddChild(new StepSelector());

        body.AddChild(Ui.Expand(new Control { SizeFlagsVertical = SizeFlags.ExpandFill }));
        var perNecro = Ui.Bold("", Tokens.FontSmall);
        var click = Ui.Bold("", Tokens.FontSmall);
        body.AddChild(Ui.Row(4, Ui.Text("Power per tick", Tokens.FontSmall, Tokens.TextMuted), perNecro));
        body.AddChild(Ui.Row(4, Ui.Text("Click power", Tokens.FontSmall, Tokens.TextMuted), click));
        body.AddChild(Ui.Muted("Summoned necromancers join the free pool."));

        Update(() =>
        {
            circle.Hint.Text = $"+{Fmt.Num2(c.ClickPower)} power per click";
            stepper.Refresh();
            perNecro.Text = $"+{Fmt.Num2(c.PowerPerNecromancerPerTick)} per necromancer";
            perNecro.TooltipText = $"{Fmt.Num2(c.PowerPerTick)} power per tick in total, split across the filled slots";
            click.Text = Fmt.Num2(c.ClickPower);
        });
        return card;
    }

    /// <summary>Card B: one row per unlocked slot (icon opens the chooser, bar with %, clear button).</summary>
    private Control SlotsCard(SummoningCircle c)
    {
        var (card, body) = ScrollCard(340, gap: 9);
        body.AddChild(Ui.Heading("Summon slots"));
        body.AddChild(Ui.Muted("Click a slot's icon to choose the unit it summons. Each slot fills its bar and then summons one unit."));
        foreach (var slot in c.SummonSlots) body.AddChild(SlotRow(c, slot));
        body.AddChild(Ui.Expand(new Control { SizeFlagsVertical = SizeFlags.ExpandFill }));
        body.AddChild(Ui.Text("New slots appear here as they are unlocked. A slot waits when it cannot pay its cost.",
            Tokens.FontLabel, Tokens.TextDim, wrap: true));

        // Slots unlocked by research, or emptied because their unit went away, need a rebuild.
        var shape = SlotShape(c);
        Update(() =>
        {
            if (SlotShape(c) == shape) return;
            shape = SlotShape(c);
            Callable.From(Rebuild).CallDeferred();
        });
        return card;
    }

    private static string SlotShape(SummoningCircle c) => string.Join(",", c.SummonSlots.Select(s => s.UnitId ?? "-"));

    private Control SlotRow(SummoningCircle c, SummonSlot slot)
    {
        var filled = !slot.IsEmpty;
        var opt = slot.UnitId is { } id ? c.Option(id) : null;
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", filled
            ? Tokens.Box(Tokens.NecroBg, Tokens.PurpleDeep, Tokens.RadiusInset, 10, 8)
            : Tokens.Box(Tokens.BgInset, Tokens.Yellow, Tokens.RadiusInset, 10, 8));
        var h = Ui.Row(9);
        row.AddChild(h);

        var accent = filled ? Tokens.Purple : Tokens.Yellow;
        var icon = new Button
        {
            Text = filled ? Abbrev(opt?.Name ?? slot.UnitId!) : "PICK",
            CustomMinimumSize = new Vector2(44, 44),
            FocusMode = FocusModeEnum.None,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = "Choose the unit this slot summons",
        };
        foreach (var (state, border) in new[] { ("normal", accent), ("hover", Tokens.TextMain), ("pressed", Tokens.TextMain) })
            icon.AddThemeStyleboxOverride(state, Tokens.Box(Tokens.LockedFill, border, Tokens.RadiusButton, 0, 0, 2));
        icon.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        icon.AddThemeFontOverride("font", Tokens.Bold);
        icon.AddThemeFontSizeOverride("font_size", 10);
        var fg = filled ? Tokens.NecroText : Tokens.Yellow;
        foreach (var k in new[] { "font_color", "font_hover_color", "font_pressed_color" }) icon.AddThemeColorOverride(k, fg);
        icon.Pressed += () => OpenChooser(c, slot.Index, icon);
        h.AddChild(icon);

        var col = Ui.Col(5);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        col.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var name = Ui.Bold(filled ? opt?.Name ?? slot.UnitId! : "Choose a unit", Tokens.FontSmall);
        var right = Ui.Text("", Tokens.FontSmall, filled ? Tokens.TextMuted : Tokens.Yellow);
        col.AddChild(Ui.Row(8, name, Ui.Fill(), right));
        var bar = new PercentBar(Tokens.SummonBar);
        col.AddChild(bar);
        h.AddChild(col);

        if (filled)
        {
            var clear = new Button
            {
                Text = "x",
                CustomMinimumSize = new Vector2(20, 20),
                FocusMode = FocusModeEnum.None,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                TooltipText = "Empty this slot (its progress is lost)",
            };
            foreach (var state in new[] { "normal", "hover", "pressed" })
                clear.AddThemeStyleboxOverride(state, Tokens.Box(Tokens.DangerBg, state == "normal" ? Tokens.DangerBorder : Tokens.Red, 5));
            clear.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            clear.AddThemeFontSizeOverride("font_size", Tokens.FontLabel);
            foreach (var k in new[] { "font_color", "font_hover_color", "font_pressed_color" }) clear.AddThemeColorOverride(k, Tokens.Red);
            clear.Pressed += () => { c.ClearSlot(slot.Index); Rebuild(); };
            h.AddChild(clear);
        }

        Update(() =>
        {
            if (slot.IsEmpty)
            {
                right.Text = "Idle";
                bar.Value = 0;
                bar.Text = null;
                return;
            }
            bar.Value = slot.Fraction;
            bar.Text = $"{Math.Floor(slot.Fraction * 100):0}%";
            if (slot.WaitingFor is { } w)
            {
                right.Text = w == SummoningCircle.PoolFull ? "Waiting: pool full" : $"Waiting: {Name(w).ToLowerInvariant()}";
                right.AddThemeColorOverride("font_color", Tokens.Yellow);
                bar.FillColor = Tokens.YellowBorder;
            }
            else
            {
                var active = c.SummonSlots.Count(s => !s.IsEmpty);
                var share = active > 0 ? c.PowerPerTick / active : 0;
                right.Text = share > 0 ? $"{Seconds(slot.Required, share)} per unit" : $"{Fmt.Num2(slot.Required)} power per unit";
                right.AddThemeColorOverride("font_color", Tokens.TextMuted);
                bar.FillColor = Tokens.SummonBar;
            }
            bar.TooltipText = $"{Fmt.Num2(slot.Progress)} / {Fmt.Num2(slot.Required)} power";
        });
        return row;
    }

    private static string Abbrev(string name)
    {
        var s = new string(name.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        return s.Length <= 5 ? s : s[..5];
    }

    // ---- Unit chooser (pop-up under the slot icon) and its hover tooltip ----

    private void OpenChooser(SummoningCircle c, int slotIndex, Control anchor)
    {
        CloseChooser();
        var scrim = new Control { TopLevel = true, MouseFilter = MouseFilterEnum.Stop, ZIndex = 50 };
        scrim.Position = Vector2.Zero;
        scrim.Size = GetViewportRect().Size;
        scrim.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) CloseChooser(); };
        AddChild(scrim);
        _chooserScrim = scrim;

        var panel = new PanelContainer { TopLevel = true, ZIndex = 51, MouseFilter = MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgPopup, Tokens.NecroBorder, Tokens.RadiusInset, 8, 8));
        panel.CustomMinimumSize = new Vector2(236, 0);
        var col = Ui.Col(6);
        panel.AddChild(col);
        col.AddChild(Ui.Section("Choose unit"));
        var current = slotIndex < c.SummonSlots.Count ? c.SummonSlots[slotIndex].UnitId : null;
        foreach (var opt in c.AvailableUnits()) col.AddChild(OptionRow(c, slotIndex, opt, opt.Id == current, panel));
        AddChild(panel);
        _chooser = panel;

        var view = GetViewportRect().Size;
        var size = panel.GetCombinedMinimumSize();
        var pos = anchor.GlobalPosition + new Vector2(38, anchor.Size.Y + 4);
        if (pos.Y + size.Y > view.Y - 8) pos.Y = Math.Max(8, anchor.GlobalPosition.Y - size.Y - 4);
        if (pos.X + size.X > view.X - 8) pos.X = view.X - size.X - 8;
        panel.Position = pos;
    }

    private Control OptionRow(SummoningCircle c, int slotIndex, UnitOption opt, bool current, Control chooser)
    {
        var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        void Style(bool hot) => row.AddThemeStyleboxOverride("panel", hot || current
            ? Tokens.Box(Tokens.SelectedBg, Tokens.SelectedBorder, Tokens.RadiusButton, 9, 7)
            : Tokens.Box(Tokens.BgCard, Tokens.Border, Tokens.RadiusButton, 9, 7));
        Style(false);
        var col = Ui.Col(1);
        col.MouseFilter = MouseFilterEnum.Ignore;
        var name = Ui.Bold(opt.Name, Tokens.FontBody);
        var cost = Ui.Text(opt.Cost.Count == 0 ? "Free" : Costs(opt.Cost), Tokens.FontLabel, Tokens.TextMuted);
        name.MouseFilter = cost.MouseFilter = MouseFilterEnum.Ignore;
        col.AddChild(name);
        col.AddChild(cost);
        row.AddChild(col);
        row.MouseEntered += () => { Style(true); ShowTooltip(opt, row, chooser); };
        row.MouseExited += () => { Style(false); HideTooltip(); };
        row.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            c.SetUnit(slotIndex, opt.Id);
            CloseChooser();
            Rebuild();
        };
        return row;
    }

    /// <summary>Name, description and one "owned / needed" line per cost (red when short, green when met).</summary>
    private void ShowTooltip(UnitOption opt, Control row, Control chooser)
    {
        HideTooltip();
        var tip = new PanelContainer { TopLevel = true, ZIndex = 52, MouseFilter = MouseFilterEnum.Ignore };
        tip.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgPopup, Tokens.SelectedBorder, Tokens.RadiusInset, 10, 9));
        tip.CustomMinimumSize = new Vector2(232, 0);
        var col = Ui.Col(5);
        tip.AddChild(col);
        col.AddChild(Ui.Bold(opt.Name, Tokens.FontBody));
        if (opt.Description.Length > 0)
        {
            var d = Ui.Text(opt.Description, Tokens.FontLabel, Tokens.TextMuted, wrap: true);
            d.CustomMinimumSize = new Vector2(212, 0);
            col.AddChild(d);
        }
        foreach (var cst in opt.Cost)
        {
            var have = Owned(cst.Id);
            col.AddChild(Ui.Row(0,
                Ui.Bold(Fmt.Short(have), Tokens.FontSmall, have >= cst.Count ? Tokens.Green : Tokens.Red),
                Ui.Text($" / {Fmt.Short(cst.Count)} {Name(cst.Id).ToLowerInvariant()}", Tokens.FontSmall, Tokens.TextMain)));
        }
        if (opt.Cost.Count == 0) col.AddChild(Ui.Text("No cost", Tokens.FontSmall, Tokens.Green));
        AddChild(tip);
        _tooltip = tip;
        var view = GetViewportRect().Size;
        var size = tip.GetCombinedMinimumSize();
        var pos = new Vector2(chooser.GlobalPosition.X + chooser.Size.X + 8, row.GlobalPosition.Y);
        if (pos.X + size.X > view.X - 8) pos.X = chooser.GlobalPosition.X - size.X - 8;
        if (pos.Y + size.Y > view.Y - 8) pos.Y = view.Y - size.Y - 8;
        tip.Position = pos;
    }

    private void HideTooltip()
    {
        if (_tooltip is not null && IsInstanceValid(_tooltip)) _tooltip.QueueFree();
        _tooltip = null;
    }

    private void CloseChooser()
    {
        HideTooltip();
        foreach (var n in new[] { _chooser, _chooserScrim })
            if (n is not null && IsInstanceValid(n)) n.QueueFree();
        _chooser = _chooserScrim = null;
    }

    /// <summary>
    /// Level card (images 01, 02): "Lvl N ▸ Lvl N+1", the output change, cost rows (owned / needed, red when short,
    /// green when met), UPGRADE, and the short reason it is disabled. The building gives every number.
    /// </summary>
    private Control LevelCard(BuildingBase b, string title, string description)
    {
        var (card, body) = ScrollCard(expand: true, gap: 10);
        body.AddChild(Ui.Heading(title));
        var levels = Ui.Bold("", 16);
        var output = Ui.Bold("", Tokens.FontSmall, Tokens.Green);
        output.TooltipText = "Output bonus now and at the next level";
        body.AddChild(Ui.Row(8, levels, Ui.Fill(), output));
        body.AddChild(Ui.Muted(description));
        body.AddChild(Ui.Section("Cost"));
        var costs = Ui.Col(6);
        body.AddChild(costs);
        foreach (var cst in b.IsMaxLevel ? new List<ItemAmount>() : b.UpgradeCosts.ToList())
        {
            var r = new InsetRow(8);
            r.Body.AddChild(Ui.Dot(ColorOf(cst.Id), 8));
            r.Body.AddChild(Ui.Text(Name(cst.Id), Tokens.FontSmall, Tokens.TextMain));
            r.Body.AddChild(Ui.Fill());
            var v = Ui.Bold("", Tokens.FontSmall);
            r.Body.AddChild(v);
            costs.AddChild(r);
            var id = cst.Id;
            var need = cst.Count;
            Update(() =>
            {
                var have = Owned(id);
                v.Text = $"{Fmt.Short(have)} / {Fmt.Short(need)}";
                v.TooltipText = $"{Fmt.Exact(have)} owned, {Fmt.Exact(need)} needed";
                v.AddThemeColorOverride("font_color", have >= need ? Tokens.Green : Tokens.Red);
            });
        }
        if (b.IsMaxLevel) costs.AddChild(Ui.Muted("Highest level reached."));
        body.AddChild(Ui.Expand(new Control { SizeFlagsVertical = SizeFlags.ExpandFill }));
        var up = new ActionButton("UPGRADE", primary: true, padX: 10, padY: 10, fontSize: 13, font: Tokens.LabelFont);
        up.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        up.Pressed += () =>
        {
            if (!CryptMgr.TryUpgrade(b.Id) && !b.CanUpgrade(out var why)) Toast.Show($"Cannot upgrade {b.Name}: {why}.", true);
        };
        body.AddChild(up);
        var reason = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted);
        reason.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(reason);

        Update(() =>
        {
            levels.Text = b.IsMaxLevel ? $"Lvl {b.Level} (max)" : $"Lvl {b.Level} ▸ Lvl {b.Level + 1}";
            output.Text = b.IsMaxLevel ? Bonus(b.LevelBonusAt(b.Level)) : $"{Bonus(b.LevelBonusAt(b.Level))} ▸ {Bonus(b.LevelBonusAt(b.Level + 1))}";
            var can = b.CanUpgrade(out var why);
            up.Text = b.IsMaxLevel ? "MAX LEVEL" : "UPGRADE";
            up.Disabled = !can;
            reason.Text = can ? "" : why;
            reason.Visible = !can;
        });
        return card;
    }

    /// <summary>"+10%", or two decimals below 1% ("+0.03%").</summary>
    private static string Bonus(double v) => v * 100 < 1 && v > 0 ? $"+{v * 100:0.00}%" : $"+{v * 100:0.#}%";

    /// <summary>A resource's own colour, or the item kind colour.</summary>
    private static Color ColorOf(string id) =>
        Db.TryGet<ResourceDefinitionBase>(id, out var r) && r.Color is { } hex ? new Color(hex)
        : Db.TryGet<ItemDefinitionBase>(id, out var item) ? LichPage.KindColor(item) : Tokens.TextMuted;

    private static long Sat(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;

    // ---- Other buildings: level/staff card + their own settings ----

    private void BuildGeneric(HBoxContainer row, BuildingBase b)
    {
        var (a, body) = ScrollCard(420, gap: 12);
        Header(body, b, b.Name, b switch
        {
            Infuser => "Necromancers infuse mana into the chosen element.",
            SoulWell => "Binds one of each element into a soul.",
            Transmuter => "Turns one resource or material into another.",
            UndeadMarket => "Sells items from the sell list for gold, keeping a reserve of each.",
            MagicalStorage => "Raises every resource cap and the inventory size.",
            _ => "",
        });
        StatsRow(body, b, () => b.Ticks ? $"{Fmt.Num2(b.WorkPerTick)} work/tick" : "Passive", "Work per tick: base + staff, times the level bonus");
        StaffBlock(body, b, "Necromancers");
        body.AddChild(Ui.Expand(new Control { SizeFlagsVertical = SizeFlags.ExpandFill }));
        var footer = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted, wrap: true);
        body.AddChild(footer);
        Update(() => footer.Text = NextLevelText(b));
        row.AddChild(a);

        var (c, cb) = ScrollCard(expand: true, gap: 10);
        row.AddChild(c);
        switch (b)
        {
            case Infuser i: InfuserSettings(cb, i); break;
            case SoulWell s: ConverterSettings(cb, s.Conversion, () => s.Progress, s); break;
            case Transmuter t: TransmuterSettings(cb, t); break;
            case UndeadMarket m: MarketSettings(cb, m); break;
            case MagicalStorage st: StorageSettings(cb, st); break;
        }
    }

    private void ProgressBlock(VBoxContainer into, BuildingBase b, Func<double> progress, double work)
    {
        var bar = new StatBar(Tokens.Green, 6);
        var left = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
        into.AddChild(bar);
        into.AddChild(left);
        Update(() =>
        {
            bar.Value = progress();
            left.Text = b.WorkPerTick > 0 ? $"{Seconds(work, b.WorkPerTick)} each · {StatusText(b)}" : StatusText(b);
        });
    }

    private void StockRows(VBoxContainer into, IEnumerable<string> ids)
    {
        foreach (var id in ids.Distinct())
        {
            var (kv, v) = KeyValue(Name(id));
            into.AddChild(kv);
            Update(() =>
            {
                var isRes = CryptMgr.Storage.IsResource(id);
                v.Text = isRes ? $"{Fmt.Short(CryptMgr.Storage.Get(id))} / {Fmt.Short(CryptMgr.Storage.Cap(id))}" : Fmt.Short(Owned(id));
                v.TooltipText = Fmt.Exact(Owned(id));
            });
        }
    }

    private void InfuserSettings(VBoxContainer into, Infuser i)
    {
        var d = i.Infusion;
        into.AddChild(Ui.Heading("Infusion"));
        into.AddChild(Ui.Muted($"Turns {Fmt.Short(d.Input.Count)} {Name(d.Input.Id).ToLowerInvariant()} into 1 of the chosen element."));
        into.AddChild(Ui.Section("Element"));
        foreach (var e in d.Elements)
        {
            var r = new InsetRow();
            r.SetSelected(i.Element == e);
            r.Body.AddChild(Ui.Bold(Name(e), Tokens.FontBody));
            r.Body.AddChild(Ui.Fill());
            r.Body.AddChild(Ui.Text(i.Element == e ? "Active" : "", Tokens.FontSmall, Tokens.Green));
            r.MouseFilter = MouseFilterEnum.Stop;
            var el = e;
            r.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true }) { i.Select(el); Rebuild(); } };
            r.MouseDefaultCursorShape = CursorShape.PointingHand;
            into.AddChild(r);
        }
        into.AddChild(Ui.Section("Progress"));
        ProgressBlock(into, i, () => i.Progress, d.WorkPerOp);
        into.AddChild(Ui.Section("Stock"));
        StockRows(into, d.Elements.Prepend(d.Input.Id));
    }

    private void ConverterSettings(VBoxContainer into, ConverterDefinition d, Func<double> progress, BuildingBase b)
    {
        into.AddChild(Ui.Heading("Conversion"));
        into.AddChild(Ui.Muted($"Turns {Costs(d.Inputs)} into {Fmt.Short(d.Output.Count)} {Name(d.Output.Id).ToLowerInvariant()}."));
        into.AddChild(Ui.Section("Progress"));
        ProgressBlock(into, b, progress, d.WorkPerOp);
        into.AddChild(Ui.Section("Stock"));
        StockRows(into, d.Inputs.Select(x => x.Id).Append(d.Output.Id));
    }

    private void TransmuterSettings(VBoxContainer into, Transmuter t)
    {
        into.AddChild(Ui.Heading("Recipes"));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = Ui.Col(8);
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(list);
        into.AddChild(scroll);
        foreach (var r in t.Recipes)
        {
            var row = new InsetRow();
            var locked = t.IsLocked(r.Id);
            var col = Ui.Col(3);
            col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var on = t.SelectedRecipe == r.Id;
            var action = new ActionButton(on ? "Stop" : "Assign", padX: 8, padY: 2);
            var id = r.Id;
            action.Pressed += () => { t.Select(on ? null : id); Rebuild(); };
            action.Visible = !locked;
            col.AddChild(Ui.Row(8, Ui.Bold(r.Name, Tokens.FontBody), Ui.Fill(),
                Ui.Bold(on ? "Running" : locked ? "Locked" : "", Tokens.FontSmall, on ? Tokens.Green : Tokens.TextDim), action));
            col.AddChild(Ui.Text($"{Costs(r.Inputs)} → {Fmt.Short(r.Output.Count)} {Name(r.Output.Id).ToLowerInvariant()}", Tokens.FontSmall, Tokens.TextMuted));
            row.Body.AddChild(col);
            row.SetSelected(on);
            Ui.SetLocked(row, locked);
            list.AddChild(row);
        }
        if (t.Recipe is { } cur)
        {
            into.AddChild(Ui.Section("Progress"));
            ProgressBlock(into, t, () => t.Progress, cur.Work);
        }
    }

    private void MarketSettings(VBoxContainer into, UndeadMarket m)
    {
        into.AddChild(Ui.Heading("Sell list"));
        into.AddChild(Ui.Muted("Each unit of work sells one item, top to bottom, keeping the reserve. Stops when gold is full."));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = Ui.Col(6);
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(list);
        into.AddChild(scroll);
        foreach (var id in m.Sellable)
        {
            var e = m.Entry(id);
            var row = new InsetRow();
            var on = e?.Enabled ?? false;
            row.SetSelected(on);
            var stock = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
            var toggle = new ActionButton(on ? "Selling" : "Sell", primary: on, padX: 8, padY: 2);
            var reserve = new Stepper(() => m.Entry(id)?.Reserve ?? 0,
                n => { m.SetSell(id, m.Entry(id)?.Enabled ?? false, Sat(m.Entry(id)?.Reserve ?? 0, n == long.MaxValue ? Owned(id) : n)); return n; },
                n => { m.SetSell(id, m.Entry(id)?.Enabled ?? false, Math.Max(0, (m.Entry(id)?.Reserve ?? 0) - n)); return n; });
            reserve.TooltipText = "Reserve: how many to keep. +/- move the Assign by amount.";
            toggle.Pressed += () => { m.SetSell(id, !(m.Entry(id)?.Enabled ?? false), m.Entry(id)?.Reserve ?? 0); Rebuild(); };
            var col = Ui.Col(2, Ui.Bold(Name(id), Tokens.FontBody), stock);
            col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.Body.AddChild(col);
            row.Body.AddChild(reserve);
            row.Body.AddChild(toggle);
            list.AddChild(row);
            Update(() =>
            {
                stock.Text = $"{Fmt.Short(Owned(id))} owned · {Fmt.Short(m.PriceOf(id))} gold each";
                reserve.Refresh();
            });
        }
    }

    private void StorageSettings(VBoxContainer into, MagicalStorage st)
    {
        into.AddChild(Ui.Heading("Caps"));
        var (kv, mult) = KeyValue("Cap multiplier", Tokens.Green);
        into.AddChild(kv);
        into.AddChild(Ui.Muted($"Every resource cap is its base cap times the multiplier (+{Fmt.Num2(((MagicalStorageDefinition)st.Definition).CapBonusPerLevel)} per level)."));
        Update(() => mult.Text = $"x{Fmt.Num2(st.CapMultiplier)}");
        into.AddChild(Ui.Section("Resources"));
        StockRows(into, Db.All<ResourceDefinitionBase>().Select(r => r.Id));
    }
}
