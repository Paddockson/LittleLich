using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Managers;

namespace LittleLich.UI.Dev;

/// <summary>
/// Minimal inventory view (phase 4 pulled a stack-only inventory forward for crafted items).
/// Phase 6 replaces it with gear slots, equip and use.
/// </summary>
public partial class InventoryPanel : VBoxContainer, IDevPanel
{
    public string Title => "Inventory";
    public Control Root => this;

    private readonly Tree _tree;

    public InventoryPanel()
    {
        AddChild(new Label { Text = "Item stacks (no space limit or equipping until phase 6). Add with the console: give <item> <n>" });
        _tree = new Tree
        {
            Columns = 4,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 200),
        };
        string[] titles = { "Item", "Id", "Kind", "Count" };
        for (var i = 0; i < titles.Length; i++) _tree.SetColumnTitle(i, titles[i]);
        AddChild(_tree);
    }

    public void Bind()
    {
        CryptManager.Instance.InventoryChanged += (_, _) => { if (!TickManager.Instance.Scheduler.IsRunning) Refresh(); };
        TickManager.Instance.TickEnded += (_, _) => Refresh();
        GameManager.Instance.GameStarted += (_, _) => Refresh();
    }

    public void Refresh()
    {
        if (!IsVisibleInTree()) return;
        var content = GameManager.Instance.Content;
        _tree.Clear();
        var root = _tree.CreateItem();
        foreach (var (id, count) in CryptManager.Instance.Inventory.Stacks)
        {
            var it = _tree.CreateItem(root);
            content.TryGet<ItemDefinitionBase>(id, out var def);
            it.SetText(0, def?.Name ?? id);
            it.SetText(1, id);
            it.SetText(2, def?.Kind ?? "?");
            it.SetText(3, DevFormat.Exact(count));
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree()) Refresh();
    }
}
