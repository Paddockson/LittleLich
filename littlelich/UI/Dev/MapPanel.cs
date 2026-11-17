using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Managers;
using LittleLich.World;

namespace LittleLich.UI.Dev;

/// <summary>Draws the map: nodes at their positions with paths. Home blue, cleared green, fighting purple,
/// attackable gold, out of reach grey, plain nodes white. Click to select.</summary>
public partial class MapView : Control
{
    private const float Cell = 70;
    private readonly Dictionary<string, Vector2> _pos = new();
    public string? Selected { get; private set; }
    public event Action<string>? NodeSelected;

    public void Build(WorldState w)
    {
        _pos.Clear();
        foreach (var n in w.Nodes) _pos[n.Id] = new Vector2(40 + (float)n.Position[0] * Cell, 20 + (float)n.Position[1] * Cell);
        CustomMinimumSize = new Vector2(_pos.Values.Max(p => p.X) + 110, _pos.Values.Max(p => p.Y) + 40);
        QueueRedraw();
    }

    public void Select(string id) { Selected = id; QueueRedraw(); }

    public override void _Draw()
    {
        if (WorldManager.Instance.State is not { } w) return;
        var font = ThemeDB.FallbackFont;
        foreach (var n in w.Nodes)
            foreach (var l in n.Links)
                if (_pos.TryGetValue(l, out var to)) DrawLine(_pos[n.Id], to, new Color(0.6f, 0.55f, 0.45f), 3);
        foreach (var n in w.Nodes)
        {
            var p = _pos[n.Id];
            var color = n.Home ? new Color(0.4f, 0.6f, 1f)
                : n.Area is not { } a ? new Color(0.85f, 0.85f, 0.85f)
                : w.State(a) == AreaState.Cleared ? new Color(0.35f, 0.8f, 0.35f)
                : w.State(a) == AreaState.InProgress ? new Color(0.7f, 0.4f, 0.95f)
                : w.IsReachable(a) ? new Color(0.95f, 0.8f, 0.3f) : new Color(0.4f, 0.4f, 0.4f);
            var r = n.Area is null ? 9f : 16f;
            DrawCircle(p, r, color);
            if (n.Id == Selected) DrawArc(p, r + 4, 0, Mathf.Tau, 32, Colors.White, 2);
            DrawString(font, p + new Vector2(-30, r + 14), n.Name, HorizontalAlignment.Left, -1, 12);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
        var hit = _pos.Where(kv => kv.Value.DistanceTo(mb.Position) <= 22).Select(kv => kv.Key).FirstOrDefault();
        if (hit is null) return;
        Select(hit);
        NodeSelected?.Invoke(hit);
        AcceptEvent();
    }
}

/// <summary>
/// Phase 8: the world map. The map, the selected node's area (state, defenders, rewards), the garrison stub with a
/// simple army builder (count and section per unit; phase 9 brings real formations) and Send.
/// </summary>
public partial class MapPanel : ScrollContainer, IDevPanel
{
    public string Title => "Map";
    public Control Root => this;

    private readonly MapView _map;
    private readonly Label _details;
    private readonly GridContainer _army;
    private readonly Button _send;
    private readonly Label _result;
    private readonly Dictionary<string, (LineEdit Count, OptionButton Section)> _rows = new();

    private static WorldState? W => WorldManager.Instance.State;

    public MapPanel()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(col);
        var top = new HBoxContainer();
        col.AddChild(top);
        _map = new MapView();
        _map.NodeSelected += _ => Refresh();
        top.AddChild(_map);
        _details = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        top.AddChild(_details);

        col.AddChild(new HSeparator());
        col.AddChild(new Label { Text = "Garrison (stub until phase 9): choose how many to send and where", ThemeTypeVariation = "HeaderSmall" });
        _army = new GridContainer { Columns = 4 };
        col.AddChild(_army);
        var buttons = new HFlowContainer();
        col.AddChild(buttons);
        _send = new Button { Text = "Send army to selected area" };
        _send.Pressed += Send;
        buttons.AddChild(_send);
        var all = new Button { Text = "Fill: whole garrison" };
        all.Pressed += () => { foreach (var (unit, row) in _rows) row.Count.Text = (W?.InGarrison(unit) ?? 0).ToString(CultureInfo.InvariantCulture); };
        buttons.AddChild(all);
        buttons.AddChild(new Label { Text = "Add units: addunits <unit> <n>" });
        _result = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        col.AddChild(_result);
    }

    public void Bind()
    {
        var wm = WorldManager.Instance;
        wm.GarrisonChanged += Refresh;
        wm.AreaStateChanged += _ => Refresh();
        wm.BattleEnded += (_, _) => Refresh();
        GameManager.Instance.GameStarted += (_, _) => { _rows.Clear(); Refresh(); };
    }

    public void Refresh()
    {
        if (!IsVisibleInTree() || W is not { } w) return;
        if (_map.Selected is null)
        {
            _map.Build(w);
            _map.Select(w.Nodes.FirstOrDefault(n => n.Area is { } a && w.IsReachable(a))?.Id ?? w.Nodes[0].Id);
        }
        _map.QueueRedraw();
        var content = GameManager.Instance.Content;
        var node = w.Nodes.First(n => n.Id == _map.Selected);
        if (node.Area is { } areaId && w.Area(areaId) is { } area)
        {
            var defenders = string.Join("\n", Sections.All.Where(area.Defenders.ContainsKey).Select(s =>
                $"  {s}: " + string.Join(", ", area.Defenders[s].Select(u =>
                {
                    var def = content.Get<UnitDefinitionBase>(u.Unit);
                    var actions = def.Actions.Count == 0
                        ? "no actions"
                        : string.Join(", ", def.Actions.Select(a => $"{a.Name} {a.Effect} {a.Value:0.##}/{a.Cooldown:0.##}s"));
                    return $"{def.Name} x{u.Count} ({def.Hp:0} hp; {actions})";
                }))));
            _details.Text = $"{area.Name}: {w.State(areaId)}{(w.IsReachable(areaId) ? "" : " (no path yet)")}\n{area.Description}\n" +
                            $"Defenders:\n{defenders}\nRewards (once): " +
                            string.Join(", ", area.Rewards.Select(r => $"{StockIds.NameOf(content, r.Key)} {DevFormat.Exact(r.Value)}"));
        }
        else _details.Text = node.Home ? $"{node.Name}: home. Armies leave from here." : $"{node.Name}: a crossing on the path.";

        // Army builder rows, one per player unit.
        var units = content.All<UnitDefinitionBase>().Where(u => u.Side == "player").ToList();
        if (_rows.Count != units.Count)
        {
            foreach (var c in _army.GetChildren()) c.QueueFree();
            _rows.Clear();
            foreach (var h in new[] { "Unit", "In garrison", "Send", "Section" }) _army.AddChild(new Label { Text = h });
            foreach (var u in units)
            {
                _army.AddChild(new Label { Text = u.Name, TooltipText = u.Id, MouseFilter = MouseFilterEnum.Pass });
                _army.AddChild(new Label { Name = "have_" + u.Id });
                var count = new LineEdit { Text = "0", CustomMinimumSize = new Vector2(90, 0) };
                var section = new OptionButton();
                foreach (var s in Sections.All) section.AddItem(s);
                _army.AddChild(count);
                _army.AddChild(section);
                _rows[u.Id] = (count, section);
            }
        }
        foreach (var u in units)
            if (_army.GetNodeOrNull<Label>("have_" + u.Id) is { } have) have.Text = DevFormat.Exact(w.InGarrison(u.Id));

        _send.Disabled = node.Area is null || w.Battle is not null || w.State(node.Area) == AreaState.Cleared || !w.IsReachable(node.Area);
        _send.TooltipText = w.Battle is not null ? "One battle at a time (v0.1)." : "";
        _result.Text = w.LastResult is { } r ? ResultText(r) : "";
    }

    private void Send()
    {
        if (W is not { } w || w.Nodes.First(n => n.Id == _map.Selected).Area is not { } area) return;
        var plan = new ArmyPlan();
        foreach (var (unit, row) in _rows)
            if (DevFormat.TryParseCount(row.Count.Text, out var n) && n > 0) plan.Set(Sections.All[row.Section.Selected], unit, n);
        var res = WorldManager.Instance.Send(area, plan);
        if (res != SendResult.Ok) DevLog.Error($"Cannot send: {res}.");
    }

    public static string ResultText(BattleResult r)
    {
        var content = GameManager.Instance.Content;
        string Units(IReadOnlyDictionary<string, long> d) => string.Join(", ", d.Select(kv => $"{content.Get<UnitDefinitionBase>(kv.Key).Name} {DevFormat.Exact(kv.Value)}"));
        return $"Last battle at {content.Get<AreaDefinitionBase>(r.AreaId).Name}: {r.Outcome} after {r.Seconds:0.#}s. " +
               $"Sent {Units(r.Sent)}; back {Units(r.Survivors)} (lost {DevFormat.Exact(r.Lost)})." +
               (r.Rewards.Count > 0 ? " Rewards: " + string.Join(", ", r.Rewards.Select(kv => $"{StockIds.NameOf(content, kv.Key)} {DevFormat.Exact(kv.Value)}")) + "." : "");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree()) Refresh();
    }
}
