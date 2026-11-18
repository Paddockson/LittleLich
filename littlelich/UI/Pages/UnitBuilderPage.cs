using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Crypt.Buildings;
using LittleLich.Crypt.Units;
using LittleLich.Managers;
using LittleLich.UI.Components;
using LittleLich.UI.Theme;
using static LittleLich.UI.Pages.Kit;

namespace LittleLich.UI.Pages;

/// <summary>
/// Unit Builder (image 04): base units on the left; the selected unit's stats, loadout and gear list; its place in
/// the army and gear supply on the right. One loadout per base at a time: equipping builds (or reuses) the unit
/// type for that base and gear. Built types appear under Custom on the Army page.
/// </summary>
public partial class UnitBuilderPage : PageBase
{
    private string? _base;
    private string _tab = "Weapons";
    private readonly Dictionary<string, string> _current = new();

    private static UnitBuilder? U => CryptMgr.Units;

    public override void Bind()
    {
        base.Bind();
        CryptMgr.UnitTypesChanged += Rebuild;
        CryptMgr.ArmyFormationChanged += MarkDirty;
        CryptMgr.ArmyFiguresChanged += MarkDirty;
        CryptMgr.Unlocks.Unlocked += (_, _) => RebuildSoon();
    }

    /// <summary>The type the page shows for a base: the one picked here, one in the army formation, or the latest built.</summary>
    private static string? TypeFor(UnitBuilder u, string baseId, Dictionary<string, string> picked)
    {
        if (picked.TryGetValue(baseId, out var t) && u.Type(t) is not null) return t;
        if (u.Army.Slots.FirstOrDefault(id => id is not null && u.Type(id)?.Base == baseId) is { } a) return a;
        return u.Types.LastOrDefault(x => x.Base == baseId)?.Id;
    }

    protected override void Build()
    {
        if (U is not { } u) return;
        var bases = u.Bases.Where(b => !u.IsBaseLocked(b.Id)).ToList();   // nothing locked is drawn
        if (_base is null || bases.All(b => b.Id != _base)) _base = bases.FirstOrDefault()?.Id;
        var row = CardRow();

        // Unit list
        var list = Ui.Width(Ui.Col(8), 172);
        list.AddChild(Ui.Section("Units"));
        foreach (var b in bases)
        {
            var item = new NavItem(13, 10);
            var dot = Ui.Dot(Tokens.DotOff, 8);
            item.Body.AddChild(Ui.Row(8, Ui.Bold(b.Name, Tokens.FontBody), Ui.Fill(), dot));
            item.Selected = b.Id == _base;
            var locked = u.IsBaseLocked(b.Id);
            Ui.SetLocked(item, locked);
            item.TooltipText = locked ? "Locked: comes from research." : b.Description;
            var id = b.Id;
            item.Pressed += () => { _base = id; Rebuild(); };
            list.AddChild(item);
            Update(() => dot.Color = locked ? Tokens.DotOff : GearShort(u, TypeFor(u, id, _current)) ? Tokens.Yellow : Tokens.Green);
        }
        list.AddChild(Ui.Muted(bases.Count == 0 ? "No units yet. Research unlocks them (Necromancy: Summoning)." : "Yellow dot: gear running short. More units come from research.", Tokens.FontLabel));
        row.AddChild(list);

        if (bases.FirstOrDefault(b => b.Id == _base) is not { } def) return;
        row.AddChild(UnitCard(u, def));
        var right = Ui.Width(Ui.Col(Tokens.CardGap), 292);
        row.AddChild(right);
        StackCard(right, u, def);
        SupplyCard(right, u, def);
        var re = new Card(8);
        re.Body.AddChild(Ui.Heading("Re-equip the field", 15));
        re.Body.AddChild(Ui.Muted("Swap the loadout on figures already summoned. Comes after v0.1: new gear applies to new summons."));
        re.Body.AddChild(new ActionButton("Re-equip", padY: 7) { Disabled = true });
        Ui.SetLocked(re, true);
        right.AddChild(re);
    }

    private static bool GearShort(UnitBuilder u, string? typeId)
    {
        if (typeId is null || u.Type(typeId) is not { } t) return false;
        return new[] { t.Weapon, t.Armour }.OfType<string>().Any(g => Owned(g) < 10);
    }

    // ---- Centre: the unit ----

    private Control UnitCard(UnitBuilder u, UnitDefinitionBase def)
    {
        var card = new Card(10) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var typeId = TypeFor(u, def.Id, _current);
        var type = typeId is null ? null : u.Type(typeId);
        var locked = u.IsBaseLocked(def.Id);

        var portrait = new PanelContainer { CustomMinimumSize = new Vector2(74, 74) };
        portrait.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgPanelDark, Tokens.Yellow, Tokens.RadiusInset, 0, 0, 2));
        var pc = new CenterContainer();
        pc.AddChild(Ui.Dot(Tokens.TextMain, 34));
        portrait.AddChild(pc);
        var role = new Pill(def.Role.Length > 0 ? def.Role.ToUpperInvariant() : "UNIT");
        role.SetKind(PillKind.Neutral);
        var desc = Ui.Muted(def.Description.Length > 0 ? def.Description : $"{Costs(def.SummonCost)} per summon.");
        var head = Ui.Col(6, Ui.Row(10, Ui.Heading(def.Name, Tokens.FontTitle), role), desc);
        head.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.Body.AddChild(Ui.Row(14, portrait, head));
        if (locked) card.Body.AddChild(Ui.Text($"Locked: {(def.Unlock?.Research is { } r && Db.TryGet<ResearchNodeDefinition>(r, out var rd) ? $"research {rd.Name}" : "research")}.", Tokens.FontSmall, Tokens.Yellow));

        // Stats: base ▸ with loadout
        var baseHp = def.Hp;
        var baseDmg = def.ResolvedActions.FirstOrDefault(a => a.Effect == "damage")?.Value ?? 0;
        var cd = def.ResolvedActions.FirstOrDefault(a => a.Effect == "damage")?.CooldownSeconds ?? def.ResolvedActions.FirstOrDefault()?.CooldownSeconds ?? 0;
        var weapon = type?.Weapon is { } w && Db.TryGet<WeaponDefinition>(w, out var wd) ? wd : null;
        var armour = type?.Armour is { } a2 && Db.TryGet<ArmourDefinition>(a2, out var ad) ? ad : null;
        var stats = Ui.Row(8);
        stats.AddChild(StatBox("Health", Fmt.Short(baseHp), Fmt.Short(baseHp + (armour?.Defence ?? 0) * UnitBuilder.HpPerDefence)));
        stats.AddChild(StatBox("Damage", Fmt.Num2(baseDmg), Fmt.Num2(baseDmg + (weapon?.Damage ?? 0))));
        stats.AddChild(StatBox("Attack", $"{Fmt.Num1(cd)}s", $"{Fmt.Num1(cd)}s"));
        stats.AddChild(StatBox("Armour", "0", Fmt.Num2(armour?.Defence ?? 0)));
        card.Body.AddChild(stats);

        card.Body.AddChild(Ui.Section("Loadout"));
        card.Body.AddChild(Ui.Row(10, LoadoutBox("Weapon", weapon, def.WeaponSlots.Count > 0), LoadoutBox("Armour", armour, def.ArmourSlots.Count > 0)));

        // Gear list
        var tabs = Ui.Row(6);
        foreach (var t in new[] { "Weapons", "Armour" })
        {
            var b = new ActionButton(t, padX: 12, padY: 3, radius: 999);
            b.SetSelected(t == _tab);
            b.AddThemeColorOverride("font_color", t == _tab ? Tokens.Green : Tokens.TextSoft);
            var tt = t;
            b.Pressed += () => { _tab = tt; Rebuild(); };
            tabs.AddChild(b);
        }
        tabs.AddChild(Ui.Fill());
        tabs.AddChild(Ui.Text("Gear from Smithing", Tokens.FontSmall, Tokens.TextMuted));
        card.Body.AddChild(tabs);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var gear = Ui.Col(6);
        gear.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(gear);
        card.Body.AddChild(scroll);
        IEnumerable<EquippableDefinitionBase> items = _tab == "Weapons"
            ? Db.All<WeaponDefinition>().Where(x => !x.FitsLich)
            : Db.All<ArmourDefinition>().Where(x => !x.FitsLich);
        var hasSlot = _tab == "Weapons" ? def.WeaponSlots.Count > 0 : def.ArmourSlots.Count > 0;
        if (!hasSlot) gear.AddChild(Ui.Muted($"{def.Name} cannot use {_tab.ToLowerInvariant()}."));
        else foreach (var it in items) gear.AddChild(GearRow(u, def, type, it, locked));
        return card;
    }

    private static Control StatBox(string label, string from, string to)
    {
        var p = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        p.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgInset, Tokens.Border, Tokens.RadiusInset, 11, 7));
        var value = Ui.Row(4, Ui.Text(from, Tokens.FontBody, Tokens.TextMuted));
        if (from != to) { value.AddChild(Ui.Text("▸", Tokens.FontSmall, Tokens.TextDim)); value.AddChild(Ui.Bold(to, Tokens.FontBody, Tokens.Green)); }
        p.AddChild(Ui.Col(2, Ui.Text(label.ToUpperInvariant(), 10, Tokens.TextMuted, Tokens.LabelFont), value));
        return p;
    }

    private static Control LoadoutBox(string label, EquippableDefinitionBase? item, bool hasSlot)
    {
        var p = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        p.AddThemeStyleboxOverride("panel", item is null ? Tokens.Box(Tokens.BgInset, Tokens.Border, Tokens.RadiusInset, 13, 10)
                                                         : Tokens.Box(Tokens.NecroBg, Tokens.Purple, Tokens.RadiusInset, 13, 10));
        var col = Ui.Col(4, Ui.Text(label.ToUpperInvariant(), 10, Tokens.TextMuted, Tokens.LabelFont),
            Ui.Bold(item?.Name ?? (hasSlot ? "Nothing" : "No slot"), 14, item is null ? Tokens.TextDim : Tokens.TextMain));
        if (item is not null) col.AddChild(Ui.Text(LichPage.Effects(item).Replace("\n", " · "), Tokens.FontLabel, Tokens.Green));
        p.AddChild(col);
        if (!hasSlot) Ui.SetLocked(p, true);
        return p;
    }

    private Control GearRow(UnitBuilder u, UnitDefinitionBase def, UnitType? type, EquippableDefinitionBase item, bool locked)
    {
        var equipped = _tab == "Weapons" ? type?.Weapon == item.Id : type?.Armour == item.Id;
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", equipped ? Tokens.Box(Tokens.NecroBg, Tokens.NecroBorder, Tokens.RadiusInset, 12, 8)
                                                       : Tokens.Box(Tokens.BgInset, Tokens.Border, Tokens.RadiusInset, 12, 8));
        var stock = Ui.Bold("", Tokens.FontSmall);
        var name = Ui.Col(1, Ui.Bold(item.Name, Tokens.FontBody), Ui.Text(LichPage.Effects(item).Replace("\n", " · "), Tokens.FontLabel, Tokens.TextMuted));
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var action = new ActionButton(equipped ? "Equipped" : "Equip", primary: equipped, padX: 12, padY: 4);
        action.Disabled = locked;
        action.Pressed += () =>
        {
            var w = _tab == "Weapons" ? (equipped ? null : item.Id) : type?.Weapon;
            var a = _tab == "Armour" ? (equipped ? null : item.Id) : type?.Armour;
            var (res, t) = u.Build(def.Id, w, a, type?.Items);
            if (t is null) { Toast.Show($"Cannot equip {item.Name}: {res}.", true); return; }
            _current[def.Id] = t.Id;
            Rebuild();
        };
        action.TooltipText = equipped ? "Press to take it off" : "Use this in the loadout";
        row.AddChild(Ui.Row(10, Icon(LichPage.KindColor(item), 32), name, Ui.Row(3, stock, Ui.Text("in stock", Tokens.FontLabel, Tokens.TextMuted)), action));
        Update(() => stock.Text = Fmt.Short(Owned(item.Id)));
        return row;
    }

    // ---- Right: army and supply ----

    /// <summary>Where this loadout stands in the army formation, and what one figure costs the lich.</summary>
    private void StackCard(VBoxContainer into, UnitBuilder u, UnitDefinitionBase def)
    {
        var card = new Card(8);
        card.Body.AddChild(Ui.Heading($"{def.Name} in the army", 15));
        var (r1, slots) = KeyValue("Formation slots");
        var (r2, figures) = KeyValue("Figures standing");
        var (r3, stock) = KeyValue("Stock");
        foreach (var c in new Control[] { r1, r2, r3 }) card.Body.AddChild(c);
        var cost = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted, wrap: true);
        card.Body.AddChild(cost);
        var open = new ActionButton("Open the Army page", primary: true, padX: 10, padY: 4);
        open.TooltipText = "Drag base units and built loadouts into the 3 by 3 formation";
        open.Pressed += () => Navigate("Crypt/Army");
        card.Body.AddChild(Ui.Row(8, Ui.Fill(), open));
        into.AddChild(card);
        if (u.IsBaseLocked(def.Id)) { Ui.SetLocked(card, true); open.Disabled = true; }
        Update(() =>
        {
            var t = TypeFor(u, def.Id, _current) ?? def.Id;
            var held = Enumerable.Range(0, ArmyFormation.SlotCount).Where(i => u.GetSlot(i) == t).ToList();
            slots.Text = held.Count == 0 ? "None" : $"{held.Count} of {ArmyFormation.SlotCount}";
            figures.Text = $"{Fmt.Short(held.Sum(i => u.Figures(i)))} of {Fmt.Short(held.Count * ArmyFormation.SquadSize)}";
            var (n, limiting) = u.Affordable(t);
            stock.Text = n > 0 ? Fmt.Short(n) : $"0 (out of {Name(limiting).ToLowerInvariant()})";
            stock.AddThemeColorOverride("font_color", n > 0 ? Tokens.TextMain : Tokens.Yellow);
            cost.Text = $"The lich summons a figure every {Fmt.Num1(LichSummoner.BaseSecondsPerSummon)}s into the squad missing the most. Each costs {Costs(u.CostPerUnit(t))}.";
        });
    }

    private void SupplyCard(VBoxContainer into, UnitBuilder u, UnitDefinitionBase def)
    {
        var card = new Card(6);
        card.Body.AddChild(Ui.Heading("Gear supply", 15));
        var (r1, wv) = KeyValue("Weapons", Tokens.Green);
        var wb = new StatBar(Tokens.Green, 5);
        var (r2, av) = KeyValue("Armour", Tokens.Green);
        var ab = new StatBar(Tokens.Green, 5);
        var warn = Ui.Text("", Tokens.FontSmall, Tokens.Yellow, wrap: true);
        foreach (var c in new Control[] { r1, wb, r2, ab, warn }) card.Body.AddChild(c);
        into.AddChild(card);
        Update(() =>
        {
            var t = TypeFor(u, def.Id, _current) is { } id ? u.Type(id) : null;
            string? shortOf = null;
            foreach (var (gear, val, bar) in new[] { (t?.Weapon, wv, wb), (t?.Armour, av, ab) })
            {
                if (gear is null) { val.Text = "None equipped"; val.AddThemeColorOverride("font_color", Tokens.TextDim); bar.Value = 0; continue; }
                var have = Owned(gear);
                var enough = have > 0;
                val.Text = $"{Fmt.Short(have)} in stock";
                val.AddThemeColorOverride("font_color", enough ? Tokens.Green : Tokens.Yellow);
                bar.FillColor = enough ? Tokens.Green : Tokens.Yellow;
                bar.Value = enough ? 1 : 0;
                if (!enough) shortOf ??= Name(gear);
            }
            warn.Text = shortOf is null ? "Each summon uses one weapon and one armour."
                : $"Out of {shortOf}. The lich skips this unit until more is made.";
            warn.AddThemeColorOverride("font_color", shortOf is null ? Tokens.TextMuted : Tokens.Yellow);
        });
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
