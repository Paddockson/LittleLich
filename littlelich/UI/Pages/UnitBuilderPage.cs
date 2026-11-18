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
/// Unit Builder (image 04): base units on the left; the selected unit's stats, loadout and gear list; its stack,
/// gear supply and summoning on the right. One loadout per base at a time: equipping builds (or reuses) the unit
/// type for that base and gear, and the stack panel works on that type.
/// </summary>
public partial class UnitBuilderPage : PageBase
{
    private string? _base;
    private string _tab = "Weapons";
    private readonly Dictionary<string, string> _current = new();

    private static UnitBuilder? U => CryptMgr.Units;
    private static SummoningCircle? Circle => CryptMgr.Building<SummoningCircle>();

    public override void Bind()
    {
        base.Bind();
        CryptMgr.UnitTypesChanged += Rebuild;
        CryptMgr.UnitRosterChanged += (_, _) => MarkDirty();
        CryptMgr.FormationsChanged += MarkDirty;
        CryptMgr.Unlocks.Unlocked += (_, _) => RebuildSoon();
    }

    /// <summary>The type the page shows for a base: the one picked here, one a circle slot summons, or the biggest stack.</summary>
    private static string? TypeFor(UnitBuilder u, string baseId, Dictionary<string, string> picked)
    {
        if (picked.TryGetValue(baseId, out var t) && u.Type(t) is not null) return t;
        if (Circle?.SummonSlots.Select(s => s.UnitId).FirstOrDefault(id => id is not null && u.Type(id)?.Base == baseId) is { } c) return c;
        return u.Types.Where(x => x.Base == baseId).OrderByDescending(x => u.Count(x.Id)).Select(x => x.Id).FirstOrDefault();
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
        re.Body.AddChild(Ui.Muted("Swap the loadout on units already summoned. Comes after v0.1: new gear applies to new summons."));
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

    // ---- Right: stack and supply ----

    /// <summary>Circle slots (1-based) that summon this type.</summary>
    private static List<int> CircleSlotsFor(string? typeId) =>
        typeId is null || Circle is not { } c ? new List<int>()
            : c.SummonSlots.Where(s => s.UnitId == typeId).Select(s => s.Index + 1).ToList();

    private void StackCard(VBoxContainer into, UnitBuilder u, UnitDefinitionBase def)
    {
        var card = new Card(8);
        card.Body.AddChild(Ui.Heading($"{def.Name} stack", 15));
        var (r1, host) = KeyValue("In the host");
        var (r3, slot) = KeyValue("Formation slot");
        var (r4, by) = KeyValue("Summoned by");
        foreach (var c in new Control[] { r1, r3, r4 }) card.Body.AddChild(c);
        card.Body.AddChild(Ui.Section("Summoning Circle"));
        var toCircle = new ActionButton("Put in a circle slot", padX: 10, padY: 4);
        toCircle.TooltipText = "Puts this loadout in the first empty Summoning Circle slot";
        toCircle.Pressed += () =>
        {
            if (EnsureType(u, def) is not { } t || Circle is not { } c) return;
            if (CircleSlotsFor(t).Count > 0) { Toast.Show($"{u.Type(t)!.Name} is already in the circle."); return; }
            var free = c.SummonSlots.FirstOrDefault(s => s.IsEmpty);
            if (free is null || !c.SetUnit(free.Index, t))
            {
                Toast.Show(free is null ? "Every circle slot is in use. Clear one in the Workshop." : $"The circle cannot summon {u.Type(t)!.Name} yet.", true);
                return;
            }
            Toast.Show($"Circle slot {free.Index + 1} now summons {u.Type(t)!.Name}.");
            MarkDirty();
        };
        var summon = new ActionButton("Summon now", primary: true, padX: 10, padY: 4);
        summon.Pressed += () =>
        {
            var t = EnsureType(u, def);
            if (t is null) return;
            var n = Math.Min(StepSelector.Current, u.Affordable(t).Count);
            if (n <= 0 || !u.TrySummon(t, n)) Toast.Show($"Cannot summon {def.Name}: short of {Name(u.Affordable(t).Limiting)}.", true);
            else Toast.Show($"Summoned {Fmt.Short(n)} {u.Type(t)!.Name}.");
        };
        card.Body.AddChild(Ui.Row(8, toCircle, Ui.Fill(), summon));
        card.Body.AddChild(new StepSelector());
        var cost = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted, wrap: true);
        card.Body.AddChild(cost);
        into.AddChild(card);
        if (u.IsBaseLocked(def.Id)) { Ui.SetLocked(card, true); summon.Disabled = true; toCircle.Disabled = true; }
        Update(() =>
        {
            var t = TypeFor(u, def.Id, _current);
            var count = t is null ? 0 : u.Count(t);
            host.Text = Fmt.Short(count);
            host.TooltipText = Fmt.Exact(count);
            var fs = t is null ? null : u.Formations.SelectMany(fm => fm.Slots).FirstOrDefault(s => s.UnitType == t);
            slot.Text = fs is not null ? $"{Cap(fs.Section)} {fs.Index + 1}" : "None";
            var slots = CircleSlotsFor(t);
            by.Text = slots.Count > 0 ? $"Circle slot {string.Join(", ", slots)}" : "Nobody";
            var baseCost = u.BaseCost(def.Id);
            cost.Text = t is null ? $"{(baseCost.Count > 0 ? Costs(baseCost) : "Nothing")} per summon, plus its gear." : $"Each summon: {Costs(u.CostPerUnit(t))}.";
        });
    }

    private string? EnsureType(UnitBuilder u, UnitDefinitionBase def)
    {
        if (TypeFor(u, def.Id, _current) is { } t) return t;
        var (res, nt) = u.Build(def.Id);
        if (nt is null) { Toast.Show($"Cannot build {def.Name}: {res}.", true); return null; }
        _current[def.Id] = nt.Id;
        return nt.Id;
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
                : $"Out of {shortOf}. A circle slot summoning this waits until more is made.";
            warn.AddThemeColorOverride("font_color", shortOf is null ? Tokens.TextMuted : Tokens.Yellow);
        });
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
