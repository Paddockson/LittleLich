using System;
using System.Globalization;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Gear;
using LittleLich.Managers;

namespace LittleLich.UI.Dev;

/// <summary>
/// Phase 6: inventory and gear. Left: the seven lich slots with what is worn and an Unequip button.
/// Right: inventory stacks (type, count, slots it fits, what it does) with Equip and Use, and inventory space.
/// Bottom: the stat bonuses coming from worn gear.
/// </summary>
public partial class GearPanel : HSplitContainer, IDevPanel
{
    public string Title => "Inventory & gear";
    public Control Root => this;

    private readonly GridContainer _slots;
    private readonly Tree _items;
    private readonly Label _space;
    private readonly Label _bonuses;
    private readonly Button _equip;
    private readonly Button _use;

    private static LichManager Lich => LichManager.Instance;

    public GearPanel()
    {
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(270, 0) };
        AddChild(left);
        left.AddChild(new Label { Text = "Equipped", ThemeTypeVariation = "HeaderSmall" });
        _slots = new GridContainer { Columns = 3 };
        left.AddChild(_slots);
        left.AddChild(new HSeparator());
        left.AddChild(new Label { Text = "Bonuses from gear", ThemeTypeVariation = "HeaderSmall" });
        _bonuses = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        left.AddChild(_bonuses);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(right);
        var head = new HBoxContainer();
        right.AddChild(head);
        head.AddChild(new Label { Text = "Inventory", ThemeTypeVariation = "HeaderSmall" });
        _space = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right, ClipText = true, MouseFilter = MouseFilterEnum.Pass };
        head.AddChild(_space);
        _items = new Tree
        {
            Columns = 5,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 200),
            SelectMode = Tree.SelectModeEnum.Row,
        };
        string[] titles = { "Item", "Type", "Count", "Fits", "Effect" };
        for (var i = 0; i < titles.Length; i++) _items.SetColumnTitle(i, titles[i]);
        for (var c = 1; c < 4; c++) _items.SetColumnExpand(c, false);
        _items.SetColumnCustomMinimumWidth(1, 80);
        _items.SetColumnCustomMinimumWidth(2, 60);
        _items.SetColumnCustomMinimumWidth(3, 90);
        _items.SetColumnClipContent(4, true);
        _items.ItemSelected += UpdateButtons;
        right.AddChild(_items);

        var buttons = new HFlowContainer();
        right.AddChild(buttons);
        _equip = new Button { Text = "Equip selected" };
        _equip.Pressed += () =>
        {
            if (SelectedId() is not { } id || Lich.Equipment is not { } eq) return;
            var r = eq.EquipAuto(id);
            if (r != EquipResult.Ok) DevLog.Error($"Cannot equip {Name(id)}: {Explain(r)}.");
        };
        buttons.AddChild(_equip);
        _use = new Button { Text = "Use selected" };
        _use.Pressed += () =>
        {
            if (SelectedId() is not { } id || Lich.Items is not { } items) return;
            var r = items.Use(id);
            if (r != UseResult.Ok) DevLog.Error($"Cannot use {Name(id)}: {(r == UseResult.NotUsable ? "only potions can be used" : "none left")}.");
        };
        buttons.AddChild(_use);
        right.AddChild(new Label
        {
            Text = "Add items with the console: give <item> <n>. Bombs are for battles, runes attach later.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1, 1, 1, 0.7f),
        });
    }

    public void Bind()
    {
        Lich.EquipmentChanged += _ => Refresh();
        Lich.ItemUsed += _ => Refresh();
        Lich.StatChanged += (_, _) => RefreshBonuses();
        CryptManager.Instance.InventoryChanged += (_, _) => { if (!TickManager.Instance.Scheduler.IsRunning) Refresh(); };
        TickManager.Instance.TickEnded += (_, _) => Refresh();
        GameManager.Instance.GameStarted += (_, _) => Refresh();
    }

    public void Refresh()
    {
        if (!IsVisibleInTree() || Lich.Equipment is not { } eq) return;
        var content = GameManager.Instance.Content;

        // Slots
        foreach (var c in _slots.GetChildren()) c.QueueFree();
        foreach (var slot in LichSlots.All)
        {
            var def = eq.WornDefinition(slot);
            _slots.AddChild(new Label { Text = Cap(slot) });
            _slots.AddChild(new Label
            {
                Text = def?.Name ?? "-",
                TooltipText = def is null ? $"{eq.CandidatesFor(slot).Count()} item(s) in the inventory fit here" : Effects(def),
                MouseFilter = MouseFilterEnum.Pass,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });
            var off = new Button { Text = "Unequip", Disabled = def is null };
            var s = slot;
            off.Pressed += () =>
            {
                if (!eq.Unequip(s)) DevLog.Error($"Cannot unequip {Cap(s)}: the inventory is full.");
            };
            _slots.AddChild(off);
        }

        // Inventory
        var inv = CryptManager.Instance.Inventory;
        var selected = SelectedId();
        _items.Clear();
        var root = _items.CreateItem();
        foreach (var (id, count) in inv.Stacks)
        {
            content.TryGet<ItemDefinitionBase>(id, out var def);
            var it = _items.CreateItem(root);
            it.SetText(0, def?.Name ?? id);
            it.SetMetadata(0, id);
            it.SetText(1, def?.Kind ?? "?");
            it.SetText(2, DevFormat.Exact(count));
            it.SetText(3, def is EquippableDefinitionBase e ? (e.FitsLich ? string.Join(", ", e.Slots) : "units (phase 9)") : "");
            it.SetText(4, def is null ? "" : Effects(def));
            it.SetTooltipText(0, $"{def?.Name ?? id} ({id}), stack size {def?.StackSize}");
            it.SetTooltipText(4, it.GetText(4));
            if (id == selected) it.Select(0);
        }
        _space.Text = inv.SlotCapacity == long.MaxValue
            ? $"{DevFormat.Exact(inv.SlotsUsed)} slots used"
            : $"{DevFormat.Exact(inv.SlotsUsed)} / {DevFormat.Exact(inv.SlotCapacity)} slots";
        _space.TooltipText = "Inventory slots used. Each item id uses ceil(count / stack size) slots; Magical Storage levels add more.";
        UpdateButtons();
        RefreshBonuses();
    }

    private void RefreshBonuses()
    {
        if (!IsVisibleInTree()) return;
        var gear = Lich.State.Stats.Modifiers.Where(m => m.Source.StartsWith("gear:", StringComparison.Ordinal)).ToList();
        _bonuses.Text = gear.Count == 0 ? "None." : string.Join("\n", gear.GroupBy(m => m.StatId).Select(g =>
            $"{g.Key}: {string.Join(", ", g.Select(m => (m.Op == Core.Lich.ModifierOp.Mul ? "x" : "+") + DevFormat.Exact(m.Value) + $" ({m.Source[5..]})"))}" +
            $"  -> {DevFormat.Exact(Lich.State.Stats.Get(g.Key))}"));
    }

    private void UpdateButtons()
    {
        var id = SelectedId();
        var content = GameManager.Instance.Content;
        _equip.Disabled = id is null || !(content.TryGet<EquippableDefinitionBase>(id, out var e) && e.FitsLich);
        _use.Disabled = id is null || !content.Contains<PotionDefinition>(id);
    }

    private string? SelectedId() => _items.GetSelected() is { } it ? it.GetMetadata(0).AsString() : null;

    private static string Effects(ItemDefinitionBase def) => def switch
    {
        EquippableDefinitionBase e when e.Modifiers.Count > 0 => string.Join(", ", e.Modifiers),
        WeaponDefinition w => $"damage {w.Damage.ToString("0.##", CultureInfo.InvariantCulture)}",
        ArmourDefinition a => $"defence {a.Defence.ToString("0.##", CultureInfo.InvariantCulture)}",
        PotionDefinition p => string.Join(", ", new[] { p.Heal > 0 ? $"heal {p.Heal:0.##}" : null }
            .Concat(p.Effects.Select(x => x.ToString())).OfType<string>()) + (p.Effects.Count > 0 ? $" for {p.DurationTicks} ticks" : ""),
        BombDefinition b => $"damage {b.Damage.ToString("0.##", CultureInfo.InvariantCulture)} (battles)",
        RuneDefinition r => $"trait {r.Trait} (attach later)",
        _ => "",
    };

    private static string Explain(EquipResult r) => r switch
    {
        EquipResult.NotEquippable => "the lich cannot wear it",
        EquipResult.WrongSlot => "it does not fit that slot",
        EquipResult.NotInInventory => "none in the inventory",
        EquipResult.InventoryFull => "no room in the inventory for the item being replaced",
        EquipResult.UnknownSlot => "no such slot",
        _ => r.ToString(),
    };

    private static string Name(string id) => StockIds.NameOf(GameManager.Instance.Content, id);
    private static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree()) Refresh();
    }
}
