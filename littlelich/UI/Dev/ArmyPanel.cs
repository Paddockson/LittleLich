using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using LittleLich.Crypt.Units;
using LittleLich.Managers;

namespace LittleLich.UI.Dev;

/// <summary>
/// Phase 9: the Army Builder. Pick or create a formation; eight slots (2 front, 3 middle, 3 back), each a unit type
/// and a count, with the roster beside it. Below: what each section will be able to do in battle (the actions of the
/// units placed there). Send it from the Map tab.
/// </summary>
public partial class ArmyPanel : ScrollContainer, IDevPanel
{
    public string Title => "Army";
    public Control Root => this;

    private sealed record SlotRow(OptionButton Type, LineEdit Count, Label Have);

    private readonly OptionButton _formations;
    private readonly LineEdit _name;
    private readonly GridContainer _grid;
    private readonly List<SlotRow> _slots = new();
    private readonly Label _summary;
    private bool _filling;

    private static UnitBuilder? U => CryptManager.Instance.Units;

    public ArmyPanel()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(col);

        var top = new HFlowContainer();
        col.AddChild(top);
        _formations = new OptionButton { CustomMinimumSize = new Vector2(180, 0) };
        _formations.ItemSelected += _ => Fill();
        top.AddChild(_formations);
        _name = new LineEdit { PlaceholderText = "name", CustomMinimumSize = new Vector2(160, 0) };
        _name.TextSubmitted += n => { if (Selected() is { } f) { f.Name = n; Refresh(); } };
        top.AddChild(_name);
        var create = new Button { Text = "New formation" };
        create.Pressed += () =>
        {
            var f = U?.CreateFormation(_name.Text);
            if (f is not null) { Refresh(); SelectId(f.Id); }
        };
        top.AddChild(create);
        var delete = new Button { Text = "Delete" };
        delete.Pressed += () => { if (Selected() is { } f) U!.DeleteFormation(f.Id); };
        top.AddChild(delete);

        _grid = new GridContainer { Columns = 5 };
        col.AddChild(_grid);
        foreach (var h in new[] { "Slot", "Unit type", "Count", "In roster", "" }) _grid.AddChild(new Label { Text = h });
        for (var i = 0; i < Formation.SlotCount; i++)
        {
            var index = i;
            _grid.AddChild(new Label { Text = $"{i + 1}  {Formation.SlotSections[i]}" });
            var type = new OptionButton { CustomMinimumSize = new Vector2(220, 0) };
            var count = new LineEdit { CustomMinimumSize = new Vector2(90, 0), Text = "0" };
            var have = new Label { CustomMinimumSize = new Vector2(70, 0) };
            var all = new Button { Text = "All" };
            type.ItemSelected += _ => Apply(index);
            count.TextSubmitted += _ => Apply(index);
            count.FocusExited += () => Apply(index);
            all.Pressed += () =>
            {
                if (TypeAt(index) is { } t) { count.Text = U!.Count(t).ToString(CultureInfo.InvariantCulture); Apply(index); }
            };
            _grid.AddChild(type);
            _grid.AddChild(count);
            _grid.AddChild(have);
            _grid.AddChild(all);
            _slots.Add(new SlotRow(type, count, have));
        }
        _summary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        col.AddChild(_summary);
    }

    public void Bind()
    {
        var c = CryptManager.Instance;
        c.FormationsChanged += Refresh;
        c.UnitTypesChanged += Refresh;
        c.UnitRosterChanged += (_, _) => { if (!TickManager.Instance.Scheduler.IsRunning) Refresh(); };
        TickManager.Instance.TickEnded += (_, _) => Refresh();
        GameManager.Instance.GameStarted += (_, _) => Refresh();
    }

    public void Refresh()
    {
        if (!IsVisibleInTree() || U is not { } u) return;
        var keep = Selected()?.Id;
        _formations.Clear();
        foreach (var f in u.Formations)
        {
            _formations.AddItem(f.Name);
            _formations.SetItemMetadata(_formations.ItemCount - 1, f.Id);
        }
        if (keep is not null) SelectId(keep);
        else if (_formations.ItemCount > 0) _formations.Select(0);
        Fill();
    }

    private void Fill()
    {
        if (U is not { } u) return;
        _filling = true;
        var f = Selected();
        _name.Text = f?.Name ?? "";
        for (var i = 0; i < _slots.Count; i++)
        {
            var row = _slots[i];
            row.Type.Clear();
            row.Type.AddItem("(empty)");
            row.Type.SetItemMetadata(0, "");
            foreach (var t in u.Types)
            {
                row.Type.AddItem(t.Name);
                row.Type.SetItemMetadata(row.Type.ItemCount - 1, t.Id);
                if (f?.Slots[i].UnitType == t.Id) row.Type.Select(row.Type.ItemCount - 1);
            }
            if (f?.Slots[i].UnitType is null) row.Type.Select(0);
            row.Count.Text = (f?.Slots[i].Count ?? 0).ToString(CultureInfo.InvariantCulture);
            row.Have.Text = f?.Slots[i].UnitType is { } id ? DevFormat.Exact(u.Count(id)) : "";
            row.Type.Disabled = f is null;
            row.Count.Editable = f is not null;
        }
        _filling = false;
        Summarise();
    }

    private void Apply(int index)
    {
        if (_filling || U is not { } u || Selected() is not { } f) return;
        var type = TypeAt(index);
        var n = DevFormat.TryParseCount(_slots[index].Count.Text, out var c) ? c : 0;
        u.SetSlot(f.Id, index, type, n);
    }

    private void Summarise()
    {
        if (U is not { } u || Selected() is not { } f) { _summary.Text = "No formation: create one, fill its slots, then send it from the Map tab."; return; }
        var lines = new List<string>();
        foreach (var section in Content.Definitions.Sections.All)
        {
            var slots = f.Slots.Where(s => s.Section == section && s.UnitType is not null && s.Count > 0).ToList();
            var actions = slots.SelectMany(s => u.BattleUnit(s.UnitType!).Actions.Select(a => $"{a.Name} ({u.Type(s.UnitType!)!.Name})")).Distinct();
            lines.Add($"{section}: " + (slots.Count == 0 ? "empty" :
                $"{DevFormat.Exact(slots.Sum(s => s.Count))} units; actions: {string.Join(", ", actions)}"));
        }
        var shortOf = f.Slots.Where(s => s.UnitType is not null).GroupBy(s => s.UnitType!)
            .Where(g => g.Sum(s => s.Count) > u.Count(g.Key)).Select(g => $"{u.Type(g.Key)!.Name} {DevFormat.Exact(u.Count(g.Key))}/{DevFormat.Exact(g.Sum(s => s.Count))}").ToList();
        _summary.Text = string.Join("\n", lines) + (shortOf.Count > 0 ? "\nNot enough in the roster: " + string.Join(", ", shortOf) : "\nReady: send it from the Map tab.");
    }

    private Formation? Selected() =>
        _formations.Selected >= 0 && U is { } u ? u.GetFormation(_formations.GetItemMetadata(_formations.Selected).AsString()) : null;

    private void SelectId(string id)
    {
        for (var i = 0; i < _formations.ItemCount; i++)
            if (_formations.GetItemMetadata(i).AsString() == id) _formations.Select(i);
    }

    private string? TypeAt(int index)
    {
        var o = _slots[index].Type;
        return o.Selected <= 0 ? null : o.GetItemMetadata(o.Selected).AsString();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree()) Refresh();
    }
}
