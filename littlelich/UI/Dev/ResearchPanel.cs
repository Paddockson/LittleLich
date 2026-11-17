using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Crypt.Research;
using LittleLich.Managers;

namespace LittleLich.UI.Dev;

/// <summary>
/// Phase 7: research. Top: tomes (a list, not tabs) with progress. Middle: the selected tome's node tree (full width).
/// Bottom: the selected node's details (state, why locked, cost owned/needed, work, effects, Start) beside the benches
/// (node, progress, time left, staff, Cancel). Stacked this way so the tree stays usable at 1280x720.
/// </summary>
public partial class ResearchPanel : VBoxContainer, IDevPanel
{
    public string Title => "Research";
    public Control Root => this;

    private readonly ItemList _tomes;
    private readonly ResearchTreeView _tree;
    private readonly Label _details;
    private readonly Button _start;
    private readonly VBoxContainer _benches;
    private readonly List<Action> _benchUpdaters = new();
    private int _tomeIndex;

    private static ResearchManager? R => CryptManager.Instance.Research;

    public ResearchPanel()
    {
        _tomes = new ItemList { MaxColumns = 0, AutoHeight = true, SameColumnWidth = false };
        _tomes.ItemSelected += i => { _tomeIndex = (int)i; ShowTome(); };
        AddChild(_tomes);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 160) };
        AddChild(scroll);
        _tree = new ResearchTreeView();
        _tree.NodeSelected += _ => RefreshDetails();
        scroll.AddChild(_tree);
        var bottom = new HBoxContainer { CustomMinimumSize = new Vector2(0, 170) };
        AddChild(bottom);
        var centre = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bottom.AddChild(centre);
        var detailScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        centre.AddChild(detailScroll);
        _details = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        detailScroll.AddChild(_details);
        _start = new Button { Text = "Start on a free bench", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _start.Pressed += () =>
        {
            if (R is not { } r || _tree.Selected is not { } id) return;
            var res = r.Start(id);
            if (res != StartResult.Ok) DevLog.Error($"Cannot start {r.Node(id)?.Name}: {Explain(res)}.");
        };
        centre.AddChild(_start);

        var right = new ScrollContainer { CustomMinimumSize = new Vector2(300, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        bottom.AddChild(right);
        _benches = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddChild(_benches);
    }

    public void Bind()
    {
        var c = CryptManager.Instance;
        c.ResearchNodeStarted += (_, _) => Refresh();
        c.ResearchNodeCompleted += _ => Refresh();
        c.ResearchNodeCancelled += (_, _) => Refresh();
        c.ResearchBenchesChanged += () => { BuildBenches(); Refresh(); };
        TickManager.Instance.TickEnded += (_, _) => { if (IsVisibleInTree()) { foreach (var u in _benchUpdaters) u(); RefreshDetails(); } };
        LichManager.Instance.NecromancerPoolChanged += (_, _, _) => { if (IsVisibleInTree() && !TickManager.Instance.Scheduler.IsRunning) foreach (var u in _benchUpdaters) u(); };
        c.StockChanged += (_, _) => { if (IsVisibleInTree() && !TickManager.Instance.Scheduler.IsRunning) RefreshDetails(); };
        GameManager.Instance.GameStarted += (_, _) => { BuildBenches(); Refresh(); };
    }

    public void Refresh()
    {
        if (!IsVisibleInTree() || R is not { } r) return;
        if (_tomes.ItemCount != r.Tomes.Count)
        {
            _tomes.Clear();
            foreach (var _ in r.Tomes) _tomes.AddItem("");
            _tomes.Select(_tomeIndex);
            ShowTome();
        }
        for (var i = 0; i < r.Tomes.Count; i++)
            _tomes.SetItemText(i, $"{r.Tomes[i].Name}  {r.Tomes[i].DoneCount}/{r.Tomes[i].Nodes.Count}");
        if (_benchUpdaters.Count == 0) BuildBenches();
        foreach (var u in _benchUpdaters) u();
        _tree.QueueRedraw();
        RefreshDetails();
    }

    private void ShowTome()
    {
        if (R is not { } r || _tomeIndex >= r.Tomes.Count) return;
        var tome = r.Tomes[_tomeIndex];
        _tree.Show(r, tome);
        if (_tree.Selected is null || r.Node(_tree.Selected) is null)
            _tree.Select(tome.Nodes.FirstOrDefault(n => r.State(n.Id) == NodeState.Available)?.Id ?? tome.Nodes[0].Id);
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        if (R is not { } r || _tree.Selected is not { } id || r.Node(id) is not { } n) return;
        var content = GameManager.Instance.Content;
        var stock = CryptManager.Instance.Stockpile;
        var state = r.State(id);
        var cost = n.Cost.Count == 0 ? "free"
            : string.Join(", ", n.Cost.Select(c => $"{StockIds.NameOf(content, c.Key)} {DevFormat.Exact(stock.Get(c.Key))}/{DevFormat.Exact(c.Value)}"));
        var tomeSkill = r.TomeOf(id)?.Definition.Skill;
        _details.Text =
            $"{n.Name}  ({n.Size})   {state}{(state == NodeState.Locked ? ": " + r.WhyLocked(id) : "")}\n" +
            (n.Description.Length > 0 ? n.Description + "\n" : "") +
            $"Cost (paid at start): {cost}.   Work: {DevFormat.Exact(n.ResearchTicks)} (1 per staffed necromancer per tick)." +
            (n.Xp > 0 && tomeSkill is { Length: > 0 } ? $"   +{n.Xp:0} {tomeSkill} XP." : "") +
            (n.SkillLevel > 0 ? $"   Needs {tomeSkill} level {n.SkillLevel}." : "") + "\n" +
            "Effects: " + string.Join("; ", n.Effects.Select(x => x.ToString())
                .Concat(content.All<UnitDefinitionBase>().Where(u => u.Unlock?.Research == id).Select(u => $"Unlocks unit {u.Name}"))
                .DefaultIfEmpty("none"));
        _start.Disabled = state != NodeState.Available || !r.CanAfford(n) || r.Benches.All(b => b.NodeId is not null);
        _start.TooltipText = state != NodeState.Available ? "Not available."
            : !r.CanAfford(n) ? "Not enough to pay the cost."
            : r.Benches.All(b => b.NodeId is not null) ? "No free bench." : "";
        _tree.QueueRedraw();
    }

    private void BuildBenches()
    {
        foreach (var c in _benches.GetChildren()) c.QueueFree();
        _benchUpdaters.Clear();
        if (R is not { } r) return;
        _benches.AddChild(new Label { Text = "Benches", ThemeTypeVariation = "HeaderSmall" });
        foreach (var bench in r.Benches)
        {
            var b = bench;
            var title = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            var bar = new ProgressBar { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) };
            var info = new Label();
            var staff = new AssignmentRow("Staff", () => r.Staffed(b), n => r.AddNecromancer(b.Index, n), n => r.RemoveNecromancer(b.Index, n), compact: true);
            var cancel = new Button { Text = "Cancel", TooltipText = "Stop and refund the cost." };
            cancel.Pressed += () => r.Cancel(b.Index);
            staff.AddChild(cancel);
            foreach (var c in new Control[] { title, bar, info, staff, new HSeparator() }) _benches.AddChild(c);
            _benchUpdaters.Add(() =>
            {
                var node = b.NodeId is { } id ? r.Node(id) : null;
                title.Text = $"Bench {b.Index + 1}: {node?.Name ?? "idle"}";
                bar.Value = r.Progress(b);
                var left = r.TicksLeft(b);
                info.Text = node is null ? $"{DevFormat.Exact(r.Staffed(b))}/{DevFormat.Exact(r.Slots)} staff"
                    : $"{r.Progress(b):P0}, {(left is double t ? $"{Math.Ceiling(t).ToString(CultureInfo.InvariantCulture)} ticks left" : "no staff: no progress")}, " +
                      $"{DevFormat.Exact(r.Staffed(b))}/{DevFormat.Exact(r.Slots)} staff";
                cancel.Disabled = node is null;
                staff.Refresh();
            });
        }
    }

    private static string Explain(StartResult r) => r switch
    {
        StartResult.NotAvailable => "requirements not met",
        StartResult.NoFreeBench => "no free bench",
        StartResult.CannotAfford => "not enough to pay the cost",
        _ => r.ToString(),
    };

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree()) Refresh();
    }
}
