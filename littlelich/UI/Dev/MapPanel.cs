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
/// Phase 8: the world map. The map, the selected node's area (state, defenders, rewards), and (phase 9) a formation
/// picker with the roster to send a formation to the selected area. Formations are edited on the Army tab.
/// </summary>
public partial class MapPanel : ScrollContainer, IDevPanel
{
    public string Title => "Map";
    public Control Root => this;

    private readonly MapView _map;
    private readonly Label _details;
    private readonly OptionButton _formation;
    private readonly Label _army;
    private readonly Button _send;
    private readonly Label _result;

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
        col.AddChild(new Label { Text = "Send a formation (edit formations on the Army tab)", ThemeTypeVariation = "HeaderSmall" });
        var buttons = new HFlowContainer();
        col.AddChild(buttons);
        _formation = new OptionButton { CustomMinimumSize = new Vector2(200, 0) };
        _formation.ItemSelected += _ => Refresh();
        buttons.AddChild(_formation);
        _send = new Button { Text = "Send to selected area" };
        _send.Pressed += Send;
        buttons.AddChild(_send);
        _army = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        col.AddChild(_army);
        _result = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        col.AddChild(_result);
    }

    public void Bind()
    {
        var wm = WorldManager.Instance;
        CryptManager.Instance.UnitRosterChanged += (_, _) => Refresh();
        CryptManager.Instance.FormationsChanged += Refresh;
        wm.AreaStateChanged += _ => Refresh();
        wm.BattleEnded += (_, _) => Refresh();
        GameManager.Instance.GameStarted += (_, _) => Refresh();
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

        // Formation picker (keeps the selection by id) and what it would send against the roster.
        var units = w.Units;
        var selectedId = SelectedFormation()?.Id;
        _formation.Clear();
        foreach (var f in units.Formations)
        {
            _formation.AddItem(f.Name);
            _formation.SetItemMetadata(_formation.ItemCount - 1, f.Id);
            if (f.Id == selectedId) _formation.Select(_formation.ItemCount - 1);
        }
        if (_formation.ItemCount > 0 && _formation.Selected < 0) _formation.Select(0);
        var formation = SelectedFormation();
        var plan = formation is null ? null : w.PlanFor(formation);
        var short_ = plan?.Totals().Where(t => units.Count(t.Key) < t.Value).Select(t => $"{t.Key} {DevFormat.Exact(units.Count(t.Key))}/{DevFormat.Exact(t.Value)}").ToList();
        _army.Text = (formation is null ? "No formations yet: make one on the Army tab.\n"
                         : "Sends: " + (plan!.Totals().Count == 0 ? "nothing (empty formation)" :
                             string.Join(", ", plan.Entries.Select(e => $"{units.Type(e.Unit)?.Name ?? e.Unit} x{DevFormat.Exact(e.Count)} ({e.Section})"))) +
                           (short_!.Count > 0 ? "\nNot enough in the roster: " + string.Join(", ", short_) : "") + "\n") +
                     "Roster: " + (units.Roster.Count(r => r.Value > 0) == 0 ? "empty" :
                         string.Join(", ", units.Roster.Where(r => r.Value > 0).Select(r => $"{units.Type(r.Key)?.Name ?? r.Key} {DevFormat.Exact(r.Value)}")));

        _send.Disabled = node.Area is null || formation is null || w.Battle is not null || w.State(node.Area) == AreaState.Cleared
                         || !w.IsReachable(node.Area) || plan!.Totals().Count == 0 || short_!.Count > 0;
        _send.TooltipText = w.Battle is not null ? "One battle at a time (v0.1)." : "";
        _result.Text = w.LastResult is { } r ? ResultText(r) : "";
    }

    private void Send()
    {
        if (W is not { } w || w.Nodes.First(n => n.Id == _map.Selected).Area is not { } area || SelectedFormation() is not { } f) return;
        var res = w.Send(area, f);
        if (res != SendResult.Ok) DevLog.Error($"Cannot send: {res}.");
    }

    private Crypt.Units.Formation? SelectedFormation() =>
        _formation.Selected >= 0 && W is { } w ? w.Units.GetFormation(_formation.GetItemMetadata(_formation.Selected).AsString()) : null;

    public static string ResultText(BattleResult r)
    {
        var content = GameManager.Instance.Content;
        var types = CryptManager.Instance.Units;
        string Units(IReadOnlyDictionary<string, long> d) => string.Join(", ", d.Select(kv => $"{types?.Type(kv.Key)?.Name ?? kv.Key} {DevFormat.Exact(kv.Value)}"));
        return $"Last battle at {content.Get<AreaDefinitionBase>(r.AreaId).Name}: {r.Outcome} after {r.Seconds:0.#}s. " +
               $"Sent {Units(r.Sent)}; back {Units(r.Survivors)} (lost {DevFormat.Exact(r.Lost)})." +
               (r.Rewards.Count > 0 ? " Rewards: " + string.Join(", ", r.Rewards.Select(kv => $"{StockIds.NameOf(content, kv.Key)} {DevFormat.Exact(kv.Value)}")) + "." : "");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree()) Refresh();
    }
}
