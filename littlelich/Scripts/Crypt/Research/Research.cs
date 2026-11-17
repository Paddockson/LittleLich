using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Ticks;

namespace LittleLich.Crypt.Research;

public enum NodeState
{
    /// <summary>Requirements not done, or the tome skill level is too low.</summary>
    Locked,
    Available,
    Researching,
    Done,
}

public enum StartResult
{
    Ok,
    UnknownNode,
    NotAvailable,
    NoFreeBench,
    CannotAfford,
}

/// <summary>One tome: its nodes and which are done. One class per tome (design rule); v0.1 has Necromancy 1.</summary>
public abstract class ResearchTome
{
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);

    protected ResearchTome(string id, ContentDatabase content)
    {
        Definition = content.TryGet<TomeDefinitionBase>(id, out var d)
            ? d : throw new InvalidOperationException($"Content has no tome '{id}'.");
        Nodes = content.All<ResearchNodeDefinition>().Where(n => n.Tome == id).ToList();
    }

    public TomeDefinitionBase Definition { get; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public IReadOnlyList<ResearchNodeDefinition> Nodes { get; }
    public IReadOnlyCollection<string> Done => _done;
    public int DoneCount => _done.Count;

    public bool IsDone(string nodeId) => _done.Contains(nodeId);
    public bool RequirementsDone(ResearchNodeDefinition n) => n.Requires.All(_done.Contains);

    internal bool MarkDone(string nodeId) => _done.Add(nodeId);
    internal void ClearDone() => _done.Clear();
}

/// <summary>The Necromancy 1 tome (the player starts with it).</summary>
public sealed class Necromancy1Tome : ResearchTome
{
    public const string TomeId = "necromancy_1";
    public Necromancy1Tome(ContentDatabase content) : base(TomeId, content) { }
}

/// <summary>A research bench: the node it is researching and its progress (staff live in the lich's pool).</summary>
public sealed class Bench
{
    public Bench(int index) => Index = index;
    public int Index { get; }
    /// <summary>Pool activity id for this bench's staff (owner "research").</summary>
    public string ActivityId => "bench_" + (Index + 1);
    public string? NodeId { get; internal set; }
    /// <summary>Work done so far, in micro-units.</summary>
    public long ProgressMicro { get; internal set; }
}

/// <summary>
/// Research (tick step 5; save section "research"), owned by the CryptManager. Holds the tomes and the benches.
///
/// A node is available when every node it requires is done and the tome's skill is at the node's skill level.
/// Starting spends the cost up front and puts the node on a free bench; staffed necromancers do the work
/// (bench work per tick = base + staffed x per necromancer, limited by the bench's slots); at research_ticks of work
/// the node completes, its effects apply and the bench frees up (no queue). Cancelling refunds the cost (capped by storage).
///
/// Effects: stat_mod adds a permanent lich modifier (source "research:&lt;node&gt;"); unlock_* opens content in
/// <see cref="Unlocks"/>; building_level raises a building's max level; unlock_bench adds a bench.
/// Everything derived from completed nodes is rebuilt on load, so content changes apply to old saves.
/// Batched with fixed-point maths: Tick(n) completes exactly what n x Tick(1) would.
/// </summary>
public sealed class ResearchManager : ITickable, ISaveable
{
    public const string Owner = "research";
    public const string SourcePrefix = "research:";

    private readonly LichState _lich;
    private readonly Stockpile _stock;
    private readonly ContentDatabase _content;
    private readonly Func<AwayRecap?> _recap;
    private readonly List<ResearchTome> _tomes;
    private readonly Dictionary<string, (ResearchTome Tome, ResearchNodeDefinition Node)> _nodes = new(StringComparer.Ordinal);
    private readonly List<Bench> _benches = new();
    private readonly ResearchBenchDefinition _bench;

    public ResearchManager(LichState lich, Stockpile stock, ContentDatabase content, Unlocks unlocks, Func<AwayRecap?>? recap = null)
    {
        _lich = lich;
        _stock = stock;
        _content = content;
        _recap = recap ?? (() => null);
        Unlocks = unlocks;
        _bench = content.TryGet<ResearchBenchDefinition>("research_bench", out var b) ? b : new ResearchBenchDefinition { Id = "research_bench", Name = "Research bench" };
        _tomes = new List<ResearchTome> { new Necromancy1Tome(content) };
        foreach (var t in _tomes)
            foreach (var n in t.Nodes) _nodes[n.Id] = (t, n);

        // Everything a research effect unlocks starts locked.
        foreach (var n in _nodes.Values.Select(x => x.Node))
            foreach (var e in n.Effects)
                if (KindOf(e) is { } kind) unlocks.SetLockable(kind, e.Id!);
        SyncBenches();
    }

    public string SaveKey => "research";

    public Unlocks Unlocks { get; }

    /// <summary>Tome skill level lookup (wired to the skills at boot; level 1 until then).</summary>
    public Func<string, int> SkillLevel { get; set; } = _ => 1;

    /// <summary>Tomes in list order.</summary>
    public IReadOnlyList<ResearchTome> Tomes => _tomes;
    public IReadOnlyList<Bench> Benches => _benches;
    public ResearchBenchDefinition BenchSettings => _bench;

    public event Action<string>? NodeAvailable;
    public event Action<string, int>? NodeStarted;
    public event Action<string>? NodeCompleted;
    public event Action<string, int>? NodeCancelled;
    public event Action? BenchesChanged;
    /// <summary>(skill id, xp) earned by completing a node; the boot code grants it to the skill.</summary>
    public event Action<string, Num>? SkillXpEarned;

    public ResearchNodeDefinition? Node(string id) => _nodes.TryGetValue(id, out var x) ? x.Node : null;
    public ResearchTome? TomeOf(string nodeId) => _nodes.TryGetValue(nodeId, out var x) ? x.Tome : null;

    public NodeState State(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var x)) return NodeState.Locked;
        if (x.Tome.IsDone(nodeId)) return NodeState.Done;
        if (_benches.Any(b => b.NodeId == nodeId)) return NodeState.Researching;
        return WhyLocked(nodeId) is null ? NodeState.Available : NodeState.Locked;
    }

    /// <summary>Why a node cannot be started yet, or null if it can (ignoring cost and benches).</summary>
    public string? WhyLocked(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var x)) return "unknown node";
        var missing = x.Node.Requires.Where(r => !x.Tome.IsDone(r)).ToList();
        if (missing.Count > 0) return "needs " + string.Join(", ", missing.Select(r => Node(r)?.Name ?? r));
        var skill = x.Tome.Definition.Skill;
        if (x.Node.SkillLevel > 0 && skill.Length > 0 && SkillLevel(skill) < x.Node.SkillLevel)
            return $"needs {skill} level {x.Node.SkillLevel}";
        return null;
    }

    public bool CanAfford(ResearchNodeDefinition n) => n.Cost.All(c => _stock.Get(c.Key) >= c.Value);

    // ---- Benches ----

    public int BenchCount => _bench.BaseBenches + _extraBenches;
    private int _extraBenches;

    public long Staffed(Bench b) => _lich.Pool.GetAssigned(Owner, b.ActivityId);
    public long Slots => _bench.SlotsPerBench;

    public double WorkPerTick(Bench b) => _bench.BaseWork + Math.Min(Staffed(b), Slots) * _bench.WorkPerNecromancer;

    /// <summary>0..1 progress of the bench's node.</summary>
    public double Progress(Bench b) =>
        b.NodeId is { } id && Node(id) is { } n ? Math.Clamp(b.ProgressMicro / (double)(n.ResearchTicks * FixedPoint.Micro), 0, 1) : 0;

    /// <summary>Ticks left at the current work rate (null if no work).</summary>
    public double? TicksLeft(Bench b)
    {
        if (b.NodeId is not { } id || Node(id) is not { } n) return null;
        var w = WorkPerTick(b);
        return w > 0 ? Math.Max(0, (n.ResearchTicks * FixedPoint.Micro - b.ProgressMicro) / (w * FixedPoint.Micro)) : null;
    }

    public long AddNecromancer(int bench, long count)
    {
        var b = _benches[bench];
        return _lich.TryAssign(Owner, b.ActivityId, Math.Min(count, Math.Max(0, Slots - Staffed(b))));
    }

    public long RemoveNecromancer(int bench, long count) => _lich.Unassign(Owner, _benches[bench].ActivityId, count);

    // ---- Start / cancel ----

    /// <summary>Starts a node on a bench (the first free one if not given): checks it is available and affordable, then pays.</summary>
    public StartResult Start(string nodeId, int? bench = null)
    {
        if (Node(nodeId) is not { } n) return StartResult.UnknownNode;
        if (State(nodeId) != NodeState.Available) return StartResult.NotAvailable;
        var b = bench is int i ? (i >= 0 && i < _benches.Count && _benches[i].NodeId is null ? _benches[i] : null)
                               : _benches.FirstOrDefault(x => x.NodeId is null);
        if (b is null) return StartResult.NoFreeBench;
        if (!CanAfford(n)) return StartResult.CannotAfford;
        foreach (var (id, count) in n.Cost) _stock.TryRemove(id, count);
        b.NodeId = nodeId;
        b.ProgressMicro = 0;
        NodeStarted?.Invoke(nodeId, b.Index);
        return StartResult.Ok;
    }

    /// <summary>Stops a bench's node and refunds its cost (resources are capped by storage). False if the bench is idle.</summary>
    public bool Cancel(int bench)
    {
        if (bench < 0 || bench >= _benches.Count || _benches[bench].NodeId is not { } id) return false;
        var b = _benches[bench];
        b.NodeId = null;
        b.ProgressMicro = 0;
        if (Node(id) is { } n)
            foreach (var (cost, count) in n.Cost) _stock.Add(cost, count);
        NodeCancelled?.Invoke(id, bench);
        return true;
    }

    /// <summary>Debug: complete a node now, without cost or bench (requirements are not checked).</summary>
    public bool DebugComplete(string nodeId)
    {
        if (Node(nodeId) is null || State(nodeId) == NodeState.Done) return false;
        foreach (var b in _benches.Where(b => b.NodeId == nodeId)) { b.NodeId = null; b.ProgressMicro = 0; }
        Complete(nodeId);
        return true;
    }

    // ---- Tick ----

    public void Tick(long ticks)
    {
        if (ticks <= 0) return;
        foreach (var b in _benches.ToList())
        {
            if (b.NodeId is not { } id || Node(id) is not { } n) continue;
            var work = FixedPoint.ToMicro(WorkPerTick(b));
            var need = (Int128)n.ResearchTicks * FixedPoint.Micro;
            var progress = (Int128)b.ProgressMicro + (Int128)work * ticks;
            if (progress >= need)
            {
                b.NodeId = null;
                b.ProgressMicro = 0;   // leftover work is lost: the bench sits idle until the next node is started
                _recap()?.AddGained("research", id, 1);
                Complete(id);
            }
            else b.ProgressMicro = (long)progress;
        }
    }

    private void Complete(string nodeId)
    {
        var (tome, node) = _nodes[nodeId];
        var before = AvailableNodes().ToHashSet();
        tome.MarkDone(nodeId);
        Apply(node);
        if (node.Xp > 0 && tome.Definition.Skill.Length > 0) SkillXpEarned?.Invoke(tome.Definition.Skill, node.Xp);
        NodeCompleted?.Invoke(nodeId);
        foreach (var id in AvailableNodes().Where(x => !before.Contains(x))) NodeAvailable?.Invoke(id);
    }

    private IEnumerable<string> AvailableNodes() => _nodes.Keys.Where(id => State(id) == NodeState.Available);

    // ---- Effects ----

    private static string? UnlockKind(ResearchEffect e) => e.Type switch
    {
        "unlock_unit" => Unlocks.Unit,
        "unlock_activity" => Unlocks.Activity,
        _ => null,  // unlock_recipe kind depends on content; see below
    };

    private string? KindOf(ResearchEffect e) =>
        e.Type == "unlock_recipe" ? ResearchNodeDefinitionBase.RecipeKind(_content, e.Id!) : UnlockKind(e);

    private void Apply(ResearchNodeDefinition node)
    {
        var mods = new List<Modifier>();
        foreach (var e in node.Effects)
        {
            switch (e.Type)
            {
                case "unlock_unit" or "unlock_recipe" or "unlock_activity":
                    if (KindOf(e) is { } kind) Unlocks.Unlock(kind, e.Id!);
                    break;
                case "building_level":
                    Unlocks.AddBuildingLevels(e.Building!, e.Levels);
                    break;
                case "unlock_bench":
                    _extraBenches++;
                    SyncBenches();
                    break;
                case "stat_mod":
                    mods.Add(new Modifier(e.Stat!, e.Op == "mul" ? ModifierOp.Mul : ModifierOp.Add, e.Value, SourcePrefix + node.Id));
                    break;
            }
        }
        SetModifiers(SourcePrefix + node.Id, mods);
    }

    /// <summary>Replace one source's modifiers, adding before removing so maxima never dip.</summary>
    private void SetModifiers(string source, List<Modifier> desired)
    {
        var stats = _lich.Stats;
        var old = stats.Modifiers.Where(m => m.Source == source).ToList();
        if (old.SequenceEqual(desired)) return;
        foreach (var m in desired) stats.Add(m);
        foreach (var m in old) stats.RemoveWhere(x => ReferenceEquals(x, m));
    }

    private void SyncBenches()
    {
        if (_benches.Count == BenchCount) return;
        while (_benches.Count < BenchCount) _benches.Add(new Bench(_benches.Count));
        BenchesChanged?.Invoke();
    }

    // ---- Save ----

    public SaveData Save() => new SaveData()
        .Set("done", _tomes.SelectMany(t => t.Done).OrderBy(x => x, StringComparer.Ordinal).ToList())
        .Set("benches", _benches.Select(b => new BenchDto { Node = b.NodeId, Progress = b.ProgressMicro }).ToList());

    /// <summary>
    /// Restores done nodes and bench progress, then rebuilds every derived effect from the definitions
    /// (unlocks, building levels, benches, research modifiers; stale research modifiers are removed).
    /// Unknown nodes are dropped; a bench whose node is unknown or already done is cleared.
    /// </summary>
    public void Load(SaveData data)
    {
        foreach (var t in _tomes) t.ClearDone();
        Unlocks.Reset();
        _extraBenches = 0;
        foreach (var id in data.Get("done", new List<string>()))
            if (_nodes.TryGetValue(id, out var x)) x.Tome.MarkDone(id);

        foreach (var t in _tomes)
            foreach (var n in t.Nodes.Where(n => t.IsDone(n.Id)))
                Apply(n);
        var doneSources = _tomes.SelectMany(t => t.Done).Select(id => SourcePrefix + id).ToHashSet();
        _lich.Stats.RemoveWhere(m => m.Source.StartsWith(SourcePrefix, StringComparison.Ordinal) && !doneSources.Contains(m.Source));
        _benches.Clear();
        SyncBenches();

        var saved = data.Get("benches", new List<BenchDto>());
        for (var i = 0; i < _benches.Count && i < saved.Count; i++)
        {
            var id = saved[i].Node;
            if (id is null || Node(id) is null || TomeOf(id)!.IsDone(id) || _benches.Take(i).Any(b => b.NodeId == id)) continue;
            _benches[i].NodeId = id;
            _benches[i].ProgressMicro = Math.Max(0, saved[i].Progress);
        }
        BenchesChanged?.Invoke();
    }

    public sealed class BenchDto
    {
        public string? Node { get; set; }
        public long Progress { get; set; }
    }
}
