using System;
using System.Globalization;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Crypt.Buildings;
using LittleLich.Crypt.Units;
using LittleLich.Managers;

namespace LittleLich.UI.Dev;

/// <summary>
/// Phase 9: the Unit Builder. Base bodies (locked ones show the research they need), a loadout picker
/// (weapon, armour, one item) with a stat preview, and the unit types: roster, cost per unit, how many the stock
/// pays for, Summon N, keep a stack topped up with the Summoning Circle, delete.
/// </summary>
public partial class UnitsPanel : ScrollContainer, IDevPanel
{
    public string Title => "Units";
    public Control Root => this;

    private readonly ItemList _bases;
    private readonly OptionButton _weapon;
    private readonly OptionButton _armour;
    private readonly OptionButton _item;
    private readonly Label _preview;
    private readonly Tree _types;
    private readonly LineEdit _count;
    private readonly Label _selected;

    private static UnitBuilder? U => CryptManager.Instance.Units;

    public UnitsPanel()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(col);

        var top = new HBoxContainer();
        col.AddChild(top);
        _bases = new ItemList { CustomMinimumSize = new Vector2(150, 110) };
        _bases.ItemSelected += _ => Preview();
        top.AddChild(_bases);
        var form = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        top.AddChild(form);
        var pick = new HFlowContainer();
        form.AddChild(pick);
        _weapon = new OptionButton { TooltipText = "Weapon (used up per summon)" };
        _armour = new OptionButton { TooltipText = "Armour (used up per summon)" };
        _item = new OptionButton { TooltipText = "Extra item (used up per summon; no battle effect yet)" };
        foreach (var o in new[] { _weapon, _armour, _item })
        {
            o.ItemSelected += _ => Preview();
            pick.AddChild(o);
        }
        var build = new Button { Text = "Build unit type" };
        build.Pressed += Build;
        pick.AddChild(build);
        _preview = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        form.AddChild(_preview);

        col.AddChild(new HSeparator());
        _types = new Tree
        {
            Columns = 5,
            ColumnTitlesVisible = true,
            HideRoot = true,
            CustomMinimumSize = new Vector2(0, 150),
            SelectMode = Tree.SelectModeEnum.Row,
        };
        string[] titles = { "Unit type", "Roster", "Cost per unit", "Can afford", "Stats" };
        for (var i = 0; i < titles.Length; i++) _types.SetColumnTitle(i, titles[i]);
        _types.SetColumnClipContent(2, true);
        _types.SetColumnClipContent(4, true);
        _types.ItemSelected += UpdateSelected;
        col.AddChild(_types);

        var actions = new HFlowContainer();
        col.AddChild(actions);
        _selected = new Label();
        actions.AddChild(_selected);
        _count = new LineEdit { Text = "10", CustomMinimumSize = new Vector2(90, 0) };
        actions.AddChild(_count);
        var summon = new Button { Text = "Summon now", TooltipText = "Pays mana and uses up the gear for every unit, or does nothing." };
        summon.Pressed += () => WithSelected(t =>
        {
            if (!DevFormat.TryParseCount(_count.Text, out var n) || n <= 0) { DevLog.Error($"'{_count.Text}' is not a count."); return; }
            if (!U!.TrySummon(t.Id, n))
                DevLog.Error(U.IsBaseLocked(t.Base) ? $"{t.Base} is locked behind research."
                    : $"Can only afford {DevFormat.Exact(U.Affordable(t.Id).Count)} {t.Name} (short of {U.Affordable(t.Id).Limiting}).");
            else DevLog.Add(LogCategory.Workshop, $"Summoned {DevFormat.Exact(n)} {t.Name}.");
        });
        actions.AddChild(summon);
        var circle = new Button { Text = "Circle: keep stack at this count", TooltipText = "The Summoning Circle summons this type over ticks until the roster has this many." };
        circle.Pressed += () => WithSelected(t =>
        {
            if (!DevFormat.TryParseCount(_count.Text, out var n) || n < 0) return;
            CryptManager.Instance.Building<SummoningCircle>()?.SetUnitTarget(t.Id, n);
            DevLog.Add(LogCategory.Workshop, $"Summoning Circle: keeping {t.Name} at {DevFormat.Exact(n)}.");
        });
        actions.AddChild(circle);
        var delete = new Button { Text = "Delete type" };
        delete.Pressed += () => WithSelected(t =>
        {
            if (!U!.Remove(t.Id)) DevLog.Error($"{t.Name} still has units, is fighting, or is in a formation.");
        });
        actions.AddChild(delete);
    }

    public void Bind()
    {
        var c = CryptManager.Instance;
        c.UnitTypesChanged += Refresh;
        c.UnitRosterChanged += (_, _) => { if (!TickManager.Instance.Scheduler.IsRunning) Refresh(); };
        c.InventoryChanged += (_, _) => { if (!TickManager.Instance.Scheduler.IsRunning) Refresh(); };
        c.ResearchNodeCompleted += _ => Refresh();
        TickManager.Instance.TickEnded += (_, _) => Refresh();
        GameManager.Instance.GameStarted += (_, _) => Refresh();
    }

    public void Refresh()
    {
        if (!IsVisibleInTree() || U is not { } u) return;
        var content = GameManager.Instance.Content;

        var bases = u.Bases.ToList();
        var selectedBase = _bases.GetSelectedItems().FirstOrDefault(-1);
        if (_bases.ItemCount != bases.Count)
        {
            _bases.Clear();
            foreach (var _ in bases) _bases.AddItem("");
            Fill(_weapon, "(no weapon)", content.All<WeaponDefinition>().Where(x => !x.FitsLich));
            Fill(_armour, "(no armour)", content.All<ArmourDefinition>().Where(x => !x.FitsLich));
            Fill(_item, "(no item)", content.All<ItemDefinitionBase>().Where(x => x is not EquippableDefinitionBase));
            if (selectedBase < 0) selectedBase = 0;
        }
        for (var i = 0; i < bases.Count; i++)
        {
            var locked = u.IsBaseLocked(bases[i].Id);
            _bases.SetItemText(i, bases[i].Name + (locked ? " (research)" : ""));
            _bases.SetItemCustomFgColor(i, locked ? new Color(1, 1, 1, 0.45f) : Colors.White);
        }
        if (selectedBase >= 0 && selectedBase < bases.Count) _bases.Select(selectedBase);

        var keep = _types.GetSelected()?.GetMetadata(0).AsString();
        _types.Clear();
        var root = _types.CreateItem();
        foreach (var t in u.Types)
        {
            var it = _types.CreateItem(root);
            it.SetText(0, t.Name);
            it.SetMetadata(0, t.Id);
            it.SetTooltipText(0, t.Id);
            it.SetText(1, DevFormat.Exact(u.Count(t.Id)));
            var cost = string.Join(", ", u.CostPerUnit(t.Id).Select(c => $"{c.Count} {StockIds.NameOf(content, c.Id)}"));
            it.SetText(2, cost);
            it.SetTooltipText(2, cost);
            var (can, limiting) = u.Affordable(t.Id);
            it.SetText(3, u.IsBaseLocked(t.Base) ? "locked" : can == long.MaxValue ? "unlimited" : $"{DevFormat.Exact(can)} ({StockIds.NameOf(content, limiting ?? "")})");
            var stats = Stats(u.BattleUnit(t.Id));
            it.SetText(4, stats);
            it.SetTooltipText(4, stats);
            if (t.Id == keep) it.Select(0);
        }
        Preview();
        UpdateSelected();
    }

    private void Preview()
    {
        if (U is not { } u || SelectedBase() is not { } b) return;
        var content = GameManager.Instance.Content;
        var weapon = Picked(_weapon);
        var armour = Picked(_armour);
        var damage = weapon is null ? 0 : content.Get<WeaponDefinition>(weapon).Damage;
        var defence = armour is null ? 0 : content.Get<ArmourDefinition>(armour).Defence;
        var hp = b.Hp + defence * UnitBuilder.HpPerDefence;
        _preview.Text =
            $"{b.Name} ({b.Type}, {b.Role}){(u.IsBaseLocked(b.Id) ? $"  [locked: needs research {b.Unlock?.Research}]" : "")}   " +
            $"mana {b.ManaCost} per summon, {b.SummonWork:0.##} circle work\n" +
            (b.Description.Length > 0 ? b.Description + "\n" : "") +
            $"Gear: {(b.WeaponSlots.Count > 0 ? "weapon" : "no weapon")}, {(b.ArmourSlots.Count > 0 ? "armour" : "no armour")}.   " +
            $"HP {b.Hp:0} -> {hp:0}   " +
            string.Join("   ", b.ResolvedActions.Select(a => a.Effect == "damage"
                ? $"{a.Name} {a.Value:0.##} -> {a.Value + damage:0.##} every {a.CooldownSeconds:0.##}s ({a.Range})"
                : $"{a.Name} {a.Effect} {a.Value:0.##} every {a.CooldownSeconds:0.##}s"));
    }

    private void Build()
    {
        if (U is not { } u || SelectedBase() is not { } b) return;
        var item = Picked(_item);
        var (r, t) = u.Build(b.Id, Picked(_weapon), Picked(_armour), item is null ? null : new[] { item });
        if (r != BuildResult.Ok) DevLog.Error($"Cannot build {b.Name}: {r}.");
        else DevLog.Add(LogCategory.Workshop, $"Unit type {t!.Name} ({t.Id}).");
    }

    private void UpdateSelected()
    {
        var t = SelectedType();
        _selected.Text = t is null ? "Select a unit type:" : $"{t.Name}:";
    }

    private void WithSelected(Action<UnitType> act)
    {
        if (SelectedType() is { } t) act(t);
        else DevLog.Error("Select a unit type first.");
        Refresh();
    }

    private UnitType? SelectedType() => _types.GetSelected() is { } it && U is { } u ? u.Type(it.GetMetadata(0).AsString()) : null;

    private UnitDefinitionBase? SelectedBase()
    {
        var i = _bases.GetSelectedItems().FirstOrDefault(-1);
        return i >= 0 && U is { } u ? u.Bases.ElementAtOrDefault(i) : null;
    }

    private static void Fill(OptionButton o, string none, System.Collections.Generic.IEnumerable<ItemDefinitionBase> defs)
    {
        o.Clear();
        o.AddItem(none);
        o.SetItemMetadata(0, "");
        foreach (var d in defs)
        {
            o.AddItem(d.Name);
            o.SetItemMetadata(o.ItemCount - 1, d.Id);
        }
        o.Select(0);
    }

    private static string? Picked(OptionButton o) =>
        o.Selected <= 0 ? null : o.GetItemMetadata(o.Selected).AsString() is { Length: > 0 } s ? s : null;

    private static string Stats(World.BattleUnit u) =>
        $"{u.Hp:0} hp; " + string.Join(", ", u.Actions.Select(a => $"{a.Name} {a.Value.ToString("0.##", CultureInfo.InvariantCulture)}/{a.Cooldown:0.##}s"));

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree()) Refresh();
    }
}
