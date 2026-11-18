using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Gear;
using LittleLich.Managers;
using LittleLich.Skills;
using LittleLich.UI.Components;
using LittleLich.UI.Theme;
using static LittleLich.UI.Pages.Kit;

namespace LittleLich.UI.Pages;

/// <summary>
/// Lich (image 06): throne with seven slots and the health bar; item grid with filters and Auto Equip; stats and
/// equipment bonuses. Items have no rarity in v0.1, so tiles are coloured by kind.
/// </summary>
public partial class LichPage : PageBase
{
    private const int TileSize = 50;
    private string _filter = "All items";
    private string? _selected;

    private static readonly string[] Filters = { "All items", "Gear", "Potions", "Materials", "Other" };

    public override void Bind()
    {
        base.Bind();
        Lich.EquipmentChanged += _ => Rebuild();
        Lich.StatChanged += (_, _) => MarkDirty();
        Lich.HealthChanged += (_, _) => MarkDirty();
        CryptMgr.InventoryChanged += (_, _) => { if (!TickManager.Instance.Scheduler.IsRunning) Rebuild(); };
    }

    public static Color KindColor(ItemDefinitionBase? d) => d switch
    {
        WeaponDefinition => Tokens.Orange,
        ArmourDefinition => Tokens.TextSoft,
        AccessoryDefinition => Tokens.Purple,
        EquippableDefinitionBase => Tokens.Blue,
        PotionDefinition => Tokens.Red,
        BombDefinition => Tokens.Yellow,
        RuneDefinition => Tokens.Teal,
        _ => Tokens.TextMuted,
    };

    private static string FilterOf(ItemDefinitionBase? d) => d switch
    {
        EquippableDefinitionBase => "Gear",
        PotionDefinition => "Potions",
        MaterialDefinition => "Materials",
        _ => "Other",
    };

    protected override void Build()
    {
        var row = CardRow();
        row.AddChild(Throne());
        row.AddChild(Items());
        row.AddChild(Stats());
    }

    // ---- Throne ----

    private Control Throne()
    {
        var card = new Card(12) { CustomMinimumSize = new Vector2(262, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        var throne = new PanelContainer();
        throne.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.NecroBg, Tokens.NecroBorder, Tokens.RadiusCard, 12, 12, 2));
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 8);
        throne.AddChild(new CenterContainer { CustomMinimumSize = new Vector2(0, 320) });
        throne.GetChild(0).AddChild(grid);
        // Layout as the mockup: hood; amulet robe ring; staff (lich) grimoire; boots.
        string?[] layout = { null, LichSlots.Hood, null, LichSlots.Amulet, LichSlots.Robe, LichSlots.Ring, LichSlots.Staff, "", LichSlots.Grimoire, null, LichSlots.Boots, null };
        foreach (var slot in layout)
        {
            if (slot is null) { grid.AddChild(new Control { CustomMinimumSize = new Vector2(56, 0) }); continue; }
            if (slot == "") { grid.AddChild(Ui.Text("LICH", Tokens.FontLabel, Tokens.NecroText, Tokens.LabelFont)); ((Label)grid.GetChild(grid.GetChildCount() - 1)).HorizontalAlignment = HorizontalAlignment.Center; continue; }
            grid.AddChild(SlotButton(slot));
        }
        card.Body.AddChild(throne);

        var health = new Card(6);
        health.SetStyle(Tokens.BgInset, Tokens.Border);
        var hp = Ui.Bold("", Tokens.FontBody);
        health.Body.AddChild(Ui.Row(8, Ui.Text("Health", Tokens.FontBody, Tokens.TextMuted), Ui.Fill(), hp));
        var bar = new StatBar(Tokens.Red, 8);
        health.Body.AddChild(bar);
        var skill = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted);
        health.Body.AddChild(Ui.Row(8, skill, Ui.Fill(), Ui.Text("+0 /s", Tokens.FontLabel, Tokens.TextDim)));
        card.Body.AddChild(health);
        Update(() =>
        {
            var s = Lich.State;
            hp.Text = $"{Fmt.Short(s.Health)} / {Fmt.Short(s.MaxHealth)}";
            bar.Value = s.MaxHealth > Num.Zero ? s.Health.ToDouble() / s.MaxHealth.ToDouble() : 0;
            var h = Lich.Skill(SkillIds.Health);
            skill.Text = $"Health skill {h?.Level ?? 1} / {XpCurve.MaxLevel}";
        });
        return card;
    }

    private Control SlotButton(string slot)
    {
        var col = Ui.Col(4);
        var eq = Lich.Equipment;
        var def = eq?.WornDefinition(slot);
        var box = new PanelContainer { CustomMinimumSize = new Vector2(56, 56), MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        box.AddThemeStyleboxOverride("panel", def is null
            ? Tokens.Box(Tokens.BgPanelDark, Tokens.Border, Tokens.RadiusInset)
            : Tokens.Box(Tokens.BgPanelDark, KindColor(def), Tokens.RadiusInset, 0, 0, 2));
        var c = new CenterContainer();
        c.AddChild(Ui.Dot(def is null ? Tokens.Track : KindColor(def), 24));
        box.AddChild(c);
        var candidates = eq?.CandidatesFor(slot).Count() ?? 0;
        box.TooltipText = def is null
            ? $"{Cap(slot)}: empty. {candidates} item(s) in the inventory fit here."
            : $"{def.Name}\n{Effects(def)}\nClick to take it off.";
        box.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || def is null || eq is null) return;
            if (!eq.Unequip(slot)) Toast.Show($"Cannot take off {def.Name}: the inventory is full.", true);
        };
        col.AddChild(box);
        var label = Ui.Text(Cap(slot), Tokens.FontLabel, Tokens.TextMuted);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(label);
        return col;
    }

    // ---- Items ----

    private Control Items()
    {
        var card = new Card(10) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var items = new ActionButton("Items", padX: 14, padY: 5);
        items.SetSelected(true);
        var loadouts = new ActionButton("Loadouts", padX: 14, padY: 5) { Disabled = true, TooltipText = "Loadouts come after v0.1" };
        var inv = CryptMgr.Inventory;
        var selectedDef = _selected is not null && Db.TryGet<ItemDefinitionBase>(_selected, out var sd) ? sd : null;
        var auto = new ActionButton(selectedDef is PotionDefinition ? "Use" : "Auto Equip", primary: true, padX: 14, padY: 5);
        auto.Disabled = selectedDef is null || !(selectedDef is PotionDefinition || selectedDef is EquippableDefinitionBase { FitsLich: true });
        auto.TooltipText = "Select an item: gear goes into its slot, potions are used.";
        auto.Pressed += () =>
        {
            if (_selected is null) return;
            if (selectedDef is PotionDefinition)
            {
                var r = Lich.Items?.Use(_selected);
                Toast.Show(r == UseResult.Ok ? $"Used {selectedDef.Name}." : $"Cannot use {selectedDef.Name}: {r}.", r != UseResult.Ok);
            }
            else if (Lich.Equipment is { } eq)
            {
                var r = eq.EquipAuto(_selected);
                if (r != EquipResult.Ok) Toast.Show($"Cannot equip {selectedDef!.Name}: {r}.", true);
            }
            Rebuild();
        };
        card.Body.AddChild(Ui.Row(8, items, loadouts, Ui.Fill(), auto));

        var filters = Ui.Row(6);
        foreach (var f in Filters)
        {
            var b = new ActionButton(f, padX: 10, padY: 3);
            b.SetSelected(f == _filter);
            var ff = f;
            b.Pressed += () => { _filter = ff; Rebuild(); };
            filters.AddChild(b);
        }
        filters.AddChild(Ui.Fill());
        var space = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted);
        filters.AddChild(space);
        card.Body.AddChild(filters);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var grid = new GridContainer { Columns = 8 };
        grid.AddThemeConstantOverride("h_separation", 5);
        grid.AddThemeConstantOverride("v_separation", 5);
        scroll.AddChild(grid);
        card.Body.AddChild(scroll);
        var shown = 0;
        foreach (var (id, count) in inv.Stacks)
        {
            Db.TryGet<ItemDefinitionBase>(id, out var def);
            if (_filter != "All items" && FilterOf(def) != _filter) continue;
            grid.AddChild(Tile(id, count, def));
            shown++;
        }
        for (var i = shown; i < Math.Max(40, (shown + 7) / 8 * 8); i++) grid.AddChild(EmptyTile());

        var sell = new NavItem(14, 10);
        sell.Body.AddChild(Ui.Bold("Sell · items are sold by the Undead Market (open it)", Tokens.FontBody, Tokens.Yellow));
        sell.AddThemeStyleboxOverride("panel", Tokens.Box(new Color("#1A170F"), Tokens.YellowBorder, Tokens.RadiusInset, 14, 10));
        sell.Pressed += () => Navigate("Crypt/Workshop:undead_market");
        card.Body.AddChild(sell);
        Update(() => space.Text = inv.SlotCapacity == long.MaxValue ? $"{Fmt.Short(inv.SlotsUsed)} slots used" : $"{Fmt.Short(inv.SlotsUsed)} / {Fmt.Short(inv.SlotCapacity)} slots");
        return card;
    }

    private static Control EmptyTile()
    {
        var p = new PanelContainer { CustomMinimumSize = new Vector2(TileSize, TileSize), MouseFilter = MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgPanelDark, Tokens.LockedBorder, Tokens.RadiusButton));
        return p;
    }

    private Control Tile(string id, long count, ItemDefinitionBase? def)
    {
        var selected = id == _selected;
        var p = new PanelContainer { CustomMinimumSize = new Vector2(TileSize, TileSize), MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        var color = KindColor(def);
        p.AddThemeStyleboxOverride("panel", Tokens.Box(selected ? Tokens.SelectedBg : Tokens.BgPanelDark, selected ? Tokens.Green : def is EquippableDefinitionBase ? color : Tokens.Border, Tokens.RadiusButton, 0, 0, selected ? 2 : 1));
        var c = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        c.AddChild(Ui.Dot(color, 18));
        p.AddChild(c);
        if (count > 1)
        {
            var n = Ui.Bold("x" + Fmt.Short(count), 10, Tokens.TextMain);
            n.SetAnchorsPreset(LayoutPreset.BottomRight);
            n.HorizontalAlignment = HorizontalAlignment.Right;
            n.VerticalAlignment = VerticalAlignment.Bottom;
            var holder = Ui.Col(0, new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore }, n);
            holder.MouseFilter = MouseFilterEnum.Ignore;
            n.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            n.AddThemeConstantOverride("outline_size", 3);
            n.AddThemeColorOverride("font_outline_color", Tokens.BgPanelDark);
            p.AddChild(holder);
        }
        p.TooltipText = Tooltip(id, count, def);
        p.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
            _selected = id;
            if (mb.DoubleClick && def is EquippableDefinitionBase { FitsLich: true } && Lich.Equipment is { } eq) eq.EquipAuto(id);
            Rebuild();
        };
        return p;
    }

    private static string Tooltip(string id, long count, ItemDefinitionBase? def)
    {
        if (def is null) return $"{id} x{Fmt.Exact(count)}";
        var lines = new List<string> { def.Name, $"{def.Kind.ToUpperInvariant()} · {Fmt.Exact(count)} owned" };
        var fx = Effects(def);
        if (fx.Length > 0) lines.Add(fx);
        if (def is EquippableDefinitionBase e && Lich.Equipment is { } eq)
        {
            if (!e.FitsLich) lines.Add("For units (Unit Builder)");
            else
            {
                var worn = e.Slots.Select(eq.WornDefinition).FirstOrDefault(w => w is not null);
                lines.Add(worn is null ? $"Fits: {string.Join(", ", e.Slots.Select(Cap))} (empty)" : $"Replaces {worn.Name}");
                lines.Add("Double-click to equip");
            }
        }
        if (def is PotionDefinition) lines.Add("Select, then Use");
        return string.Join("\n", lines);
    }

    public static string Effects(ItemDefinitionBase def) => def switch
    {
        WeaponDefinition w => $"+{Fmt.Num2(w.Damage)} damage" + ModText(w.Modifiers),
        ArmourDefinition a => $"+{Fmt.Num2(a.Defence)} defence" + ModText(a.Modifiers),
        EquippableDefinitionBase e => ModText(e.Modifiers).TrimStart('\n'),
        PotionDefinition p => string.Join("\n", new[] { p.Heal > 0 ? $"Heals {Fmt.Num2(p.Heal)}" : null }.Concat(p.Effects.Select(ModLine)).OfType<string>())
                              + (p.Effects.Count > 0 ? $"\nfor {p.DurationTicks} ticks" : ""),
        BombDefinition b => $"{Fmt.Num2(b.Damage)} damage in battles",
        RuneDefinition r => $"Trait: {r.Trait}",
        _ => "",
    };

    private static string ModText(List<StatModifierDef> mods) => mods.Count == 0 ? "" : "\n" + string.Join("\n", mods.Select(ModLine));
    private static string ModLine(StatModifierDef m) => (m.Op is "mul" or "mult" or "multiply" ? $"x{Fmt.Num2(m.Value)} " : $"+{Fmt.Num2(m.Value)} ") + StatName(m.Stat);

    public static string StatName(string stat) => stat.Replace('_', ' ') is var s && s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
    private static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    // ---- Stats ----

    private Control Stats()
    {
        var card = new Card(10) { CustomMinimumSize = new Vector2(262, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        card.Body.AddChild(Ui.Heading("Lich stats"));
        var box = new Card(4);
        box.SetStyle(Tokens.BgInset, Tokens.Border);
        var (r1, maxHp) = KeyValue("Max Health", null, Tokens.FontBody);
        var (r2, fromSkill) = KeyValue("Health skill", Tokens.TextMuted, Tokens.FontLabel);
        var (r3, fromGear) = KeyValue("Gear", Tokens.TextMuted, Tokens.FontLabel);
        var (r4, insight) = KeyValue("Max Insight", null, Tokens.FontBody);
        foreach (var r in new[] { r1, r2, r3, r4 }) box.Body.AddChild(r);
        card.Body.AddChild(box);
        card.Body.AddChild(Ui.Section("Equipment bonuses"));
        var list = Ui.Col(8);
        card.Body.AddChild(list);
        Update(() =>
        {
            var s = Lich.State;
            maxHp.Text = Fmt.Short(s.MaxHealth);
            var gear = s.Stats.Modifiers.Where(m => m.Source.StartsWith("gear:", StringComparison.Ordinal)).ToList();
            var gearHp = gear.Where(m => m.StatId == LichStats.MaxHealth && m.Op == ModifierOp.Add).Sum(m => m.Value.ToDouble());
            fromGear.Text = Fmt.Short(gearHp);
            fromSkill.Text = Fmt.Short(s.MaxHealth.ToDouble() - gearHp);
            insight.Text = Fmt.Short(s.MaxInsight);
            foreach (var c in list.GetChildren()) c.QueueFree();
            if (gear.Count == 0) list.AddChild(Ui.Muted("Nothing worn gives a bonus yet."));
            foreach (var g in gear.GroupBy(m => m.StatId))
            {
                var color = g.Key.Contains("health") ? Tokens.Red : g.Key.Contains("mana") || g.Key.Contains("spell") || g.Key.Contains("insight") ? Tokens.Blue
                          : g.Key.Contains("summon") ? Tokens.Yellow : Tokens.Green;
                var add = g.Where(m => m.Op == ModifierOp.Add).Sum(m => m.Value.ToDouble());
                var mul = g.Where(m => m.Op != ModifierOp.Add).Aggregate(1.0, (a, m) => a * m.Value.ToDouble());
                var text = (add != 0 ? $"+{Fmt.Num2(add)}" : "") + (mul != 1 ? $"{(add != 0 ? " " : "")}+{(mul - 1) * 100:0.##}%" : "");
                var row = new InsetRow();
                row.SetStyle(Tokens.BgInset, color with { A = 0.55f });
                row.Body.AddChild(Ui.Text(StatName(g.Key), Tokens.FontSmall, color));
                row.Body.AddChild(Ui.Fill());
                row.Body.AddChild(Ui.Bold(text, Tokens.FontSmall, color));
                row.TooltipText = string.Join("\n", g.Select(m => $"{m.Source[5..]}: {(m.Op == ModifierOp.Add ? "+" : "x")}{Fmt.Num2(m.Value.ToDouble())}"));
                list.AddChild(row);
            }
        });
        return card;
    }
}
