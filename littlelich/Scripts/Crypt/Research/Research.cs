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
    /// <summary>Its tome is not owned yet. Not drawn.</summary>
    Hidden,
    /// <summary>A required node is not researched yet. Drawn dimmed so the player can plan (reveal mode show_all).</summary>
    Locked,
    /// <summary>Can be started (an upgrade node with levels left counts as available too).</summary>
    Available,
    Researching,
    /// <summary>Researched (an upgrade node at its max level).</summary>
    Done,
}

public enum StartResult
{
    Ok,
    UnknownNode,
    NotAvailable,
    NoFreeBench,
}

/// <summary>
/// How much of a tree is shown. show_all (default): every node of an owned tome, locked ones dimmed, so the player
/// can plan ahead. available_only: nodes appear only when their requirements are done.
/// </summary>
public enum RevealMode { ShowAll, AvailableOnly }

/// <summary>One tome: its definition and nodes. One class per tome (design rule); unknown tomes use <see cref="GenericTome"/>.</summary>
public abstract class ResearchTome
{
    protected ResearchTome(TomeDefinitionBase def, ContentDatabase content)
    {
        Definition = def;
        Nodes = content.All<ResearchNodeDefinitionBase>().Where(n => n.Tome == def.Id).ToList();
    }

    public TomeDefinitionBase Definition { get; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public IReadOnlyList<ResearchNodeDefinitionBase> Nodes { get; }

    public static ResearchTome Create(TomeDefinitionBase def, ContentDatabase content) => def.Id switch
    {
        NecromancyTome.TomeId => new NecromancyTome(def, content),
        InfusionTome.TomeId => new InfusionTome(def, content),
        _ => new GenericTome(def, content),
    };
}

/// <summary>Necromancy: necromancers, summoning, the skeleton line, Soul Well, Undead Market, army. Owned at the start.</summary>
public sealed class NecromancyTome : ResearchTome
{
    public const string TomeId = "necromancy";
    public NecromancyTome(TomeDefinitionBase d, ContentDatabase c) : base(d, c) { }
}

/// <summary>Infusion: Infuser, Storage, Transmuter, spells, wisps, mana efficiency, benches, Insight.</summary>
public sealed class InfusionTome : ResearchTome
{
    public const string TomeId = "infusion";
    public InfusionTome(TomeDefinitionBase d, ContentDatabase c) : base(d, c) { }
}

/// <summary>A tome with no class of its own yet.</summary>
public sealed class GenericTome : ResearchTome
{
    public GenericTome(TomeDefinitionBase d, ContentDatabase c) : base(d, c) { }
}

/// <summary>A research bench: the node it works and that node's progress (staff live in the lich's pool).</summary>
public sealed class Bench
{
    public Bench(int index) => Index = index;
    public int Index { get; }
    /// <summary>Pool activity id for this bench's staff (owner "research").</summary>
    public string ActivityId => "bench_" + (Index + 1);
    public string? NodeId { get; internal set; }
    /// <summary>Work done on the node's next level, in micro-units.</summary>
    public long ProgressMicro { get; internal set; }
    public bool IsFree => NodeId is null;
}

/// <summary>A trigger or chance effect that is active, for the system that raises its event.</summary>
public sealed record ActiveResearchEffect(string NodeId, ResearchEffect Effect, int Level);

/// <summary>
/// Research (research revamp; tick step 5; save section "research"), owned by the CryptManager.
///
/// Nodes cost research work only. A bench works one node; necromancers on it add work every tick:
/// work per tick = (base + per necromancer x staffed) x level growth ^ (bench level - 1) x research efficiency.
/// A one-off node completes once and frees its bench (leftover work is lost); an upgrade node levels up and
/// keeps going on the same bench, leftover carried, until its max level. Cancelling keeps the node's progress.
///
/// Visibility: a tome shows once owned; all its nodes are drawn (locked ones dimmed) unless Reveal is available_only.
/// A node can be started once every node it requires has at least one level.
/// Effects: unlock opens content in <see cref="Unlocks"/>; modify adds lich stat modifiers (source "research:&lt;node&gt;",
/// key "stat@target" for targeted ones, per_level scaled by the node level); trigger and chance are listed in
/// <see cref="ActiveEffects"/>. Everything derived from levels is rebuilt on load.
/// Batched exactly: benches advance together, segment by segment, so Tick(n) equals n x Tick(1) even when a
/// completed node changes the work rate.
/// </summary>
public sealed class ResearchManager : ITickable, ISaveable
{
    public const string Owner = "research";
    public const string SourcePrefix = "research:";

    private readonly LichState _lich;
    private readonly ContentDatabase _content;
    private readonly Func<AwayRecap?> _recap;
    private readonly List<ResearchTome> _tomes;
    private readonly Dictionary<string, (ResearchTome Tome, ResearchNodeDefinitionBase Node)> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _levels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _partial = new(StringComparer.Ordinal);
    private readonly HashSet<string> _owned = new(StringComparer.Ordinal);
    private readonly List<Bench> _benches = new();
    private readonly ResearchBenchDefinition _bench;

    public ResearchManager(LichState lich, Stockpile stock, ContentDatabase content, Unlocks unlocks, Func<AwayRecap?>? recap = null)
    {
        _lich = lich;
        Stock = stock;
        _content = content;
        _recap = recap ?? (() => null);
        Unlocks = unlocks;
        _bench = content.TryGet<ResearchBenchDefinition>("research_bench", out var b) ? b : new ResearchBenchDefinition { Id = "research_bench", Name = "Research bench" };
        _tomes = content.All<TomeDefinitionBase>().Select(t => ResearchTome.Create(t, content)).ToList();
        foreach (var t in _tomes)
            foreach (var n in t.Nodes) _nodes[n.Id] = (t, n);

        // Everything a research effect unlocks starts locked; units also name their node (unlock.research).
        foreach (var n in _nodes.Values.Select(x => x.Node))
            foreach (var e in n.Effects.Where(e => e.Type == "unlock"))
                foreach (var (kind, id) in UnlockTargets(e)) unlocks.SetLockable(kind, id);
        foreach (var u in content.All<UnitDefinitionBase>().Where(u => u.Unlock?.Research is { Length: > 0 }))
            unlocks.SetLockable(Unlocks.Unit, u.Id);
        ResetToNewGame();
    }

    public string SaveKey => "research";

    public Unlocks Unlocks { get; }

    private Stockpile Stock { get; }

    public RevealMode Reveal { get; set; } = RevealMode.ShowAll;

    /// <summary>Every tome in content order (owned or not).</summary>
    public IReadOnlyList<ResearchTome> AllTomes => _tomes;

    /// <summary>Owned tomes only: what the tome list shows.</summary>
    public IReadOnlyList<ResearchTome> Tomes => _tomes.Where(t => _owned.Contains(t.Id)).ToList();

    public IReadOnlyList<Bench> Benches => _benches;
    public ResearchBenchDefinition BenchSettings => _bench;

    public event Action<string>? NodeAvailable;
    public event Action<string, int>? NodeStarted;
    /// <summary>(node id, new level).</summary>
    public event Action<string, int>? NodeCompleted;
    public event Action<string, int>? NodeCancelled;
    public event Action<string>? TomeFound;
    public event Action? BenchesChanged;
    public event Action? BenchLevelChanged;
    /// <summary>(skill id, xp) earned by completing a level; the boot code grants it to the skill.</summary>
    public event Action<string, Num>? SkillXpEarned;

    /// <summary>XP for the tome's skill per level completed = work x this (placeholder; the old data has no XP).</summary>
    public const double XpPerWork = 0.1;

    // ---- Nodes ----

    public ResearchNodeDefinitionBase? Node(string id) => _nodes.TryGetValue(id, out var x) ? x.Node : null;
    public ResearchTome? TomeOf(string nodeId) => _nodes.TryGetValue(nodeId, out var x) ? x.Tome : null;

    public int Level(string nodeId) => _levels.GetValueOrDefault(nodeId);
    public bool IsResearched(string nodeId) => Level(nodeId) > 0;

    /// <summary>Highest level a node can reach: max_level, plus building_and_node_level_cap for upgrade nodes.</summary>
    public int MaxLevel(string nodeId) =>
        Node(nodeId) is not { } n ? 0 : n.IsUpgrade ? n.MaxLevel + LevelCapBonus : n.MaxLevel;

    /// <summary>Extra levels for buildings, benches and upgrade nodes (Upgraded Infusions).</summary>
    public int LevelCapBonus => (int)Math.Floor(_lich.Stats.Get("building_and_node_level_cap").ToDouble());

    public bool IsOwned(string tomeId) => _owned.Contains(tomeId);

    public NodeState State(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var x) || !_owned.Contains(x.Tome.Id)) return NodeState.Hidden;
        if (Level(nodeId) >= MaxLevel(nodeId)) return NodeState.Done;
        if (_benches.Any(b => b.NodeId == nodeId)) return NodeState.Researching;
        return x.Node.Requires.All(IsResearched) ? NodeState.Available : NodeState.Locked;
    }

    /// <summary>Drawn in the tree: every node of an owned tome (show_all), or only unlocked ones (available_only).</summary>
    public bool IsVisible(string nodeId) => State(nodeId) switch
    {
        NodeState.Hidden => false,
        NodeState.Locked => Reveal == RevealMode.ShowAll,
        _ => true,
    };

    /// <summary>Required nodes that are not researched yet (what a locked node is waiting for).</summary>
    public IReadOnlyList<string> MissingRequirements(string nodeId) =>
        Node(nodeId) is { } n ? n.Requires.Where(r => !IsResearched(r)).ToList() : new List<string>();

    public int DoneCount(ResearchTome t) => t.Nodes.Count(n => State(n.Id) == NodeState.Done);

    /// <summary>Work for a node's next level.</summary>
    public double WorkNeeded(string nodeId) => Node(nodeId)?.WorkFor(Level(nodeId)) ?? 0;

    /// <summary>Work done toward the node's next level (on a bench or kept from a cancel).</summary>
    public double WorkDone(string nodeId) =>
        (_benches.FirstOrDefault(b => b.NodeId == nodeId)?.ProgressMicro ?? _partial.GetValueOrDefault(nodeId)) / (double)FixedPoint.Micro;

    // ---- Benches ----

    public int BenchCount => _bench.BaseBenches + (int)Math.Floor(_lich.Stats.Get("research_benches").ToDouble());

    public long Staffed(Bench b) => _lich.Pool.GetAssigned(Owner, b.ActivityId);

    /// <summary>Necromancers one bench holds: base, raised by capacity research (Necro Capacity, Dark Empowerment).</summary>
    public long Slots => ResearchStats.Room(_lich.Stats, StatTargets.ResearchBench, _bench.BaseSlotsPerBench);

    /// <summary>Research efficiency multiplier (Research Buddy, Necro Know-how).</summary>
    public double Efficiency =>
        _lich.Stats.GetFor("research_efficiency", StatTargets.ResearchBench, Num.One).ToDouble()
        * _lich.Stats.GetFor("necromancer_efficiency", StatTargets.ResearchBench, Num.One).ToDouble();

    public double WorkPerTick(Bench b) => WorkPerTickWith(Math.Min(Staffed(b), Slots), BenchLevel);

    /// <summary>The formula, for any staff count and bench level (the bench level card shows the next level).</summary>
    /// <remarks>An unstaffed bench does nothing on the tick (click it instead); base_work only adds while staffed.</remarks>
    public double WorkPerTickWith(long staffed, int benchLevel) => staffed <= 0 ? 0
        : (_bench.BaseWork + _bench.WorkPerNecromancer * staffed) * Math.Pow(_bench.LevelWorkGrowth, benchLevel - 1) * Efficiency;

    /// <summary>Work one click on a bench adds: click_power, raised by the bench level and research efficiency like necromancer work.</summary>
    public double ClickWork => _bench.ClickPower * Math.Pow(_bench.LevelWorkGrowth, BenchLevel - 1) * Efficiency;

    /// <summary>
    /// Research by hand: one click adds <see cref="ClickWork"/> to the node on this bench and completes any levels it
    /// pays for. False if the bench is empty or unknown.
    /// </summary>
    public bool Click(int bench)
    {
        if (bench < 0 || bench >= _benches.Count || _benches[bench].NodeId is null) return false;
        var b = _benches[bench];
        b.ProgressMicro = FixedPoint.Clamp((Int128)b.ProgressMicro + FixedPoint.ToMicro(ClickWork));
        Finish(b);   // a finished level raises NodeCompleted
        return true;
    }

    /// <summary>0..1 progress of the bench's node level.</summary>
    public double Progress(Bench b) =>
        b.NodeId is { } id && WorkNeeded(id) is > 0 and var need ? Math.Clamp(b.ProgressMicro / (need * FixedPoint.Micro), 0, 1) : 0;

    /// <summary>Ticks left on the bench's current level at the current rate (null if idle or no work).</summary>
    public double? TicksLeft(Bench b)
    {
        if (b.NodeId is not { } id) return null;
        var w = WorkPerTick(b);
        return w > 0 ? Math.Max(0, (WorkNeeded(id) * FixedPoint.Micro - b.ProgressMicro) / (w * FixedPoint.Micro)) : null;
    }

    /// <summary>Ticks a node's next level takes on a bench with this rate (the hover card's time).</summary>
    public double TicksFor(string nodeId, double workPerTick) =>
        workPerTick > 0 ? Math.Max(0, WorkNeeded(nodeId) - WorkDone(nodeId)) / workPerTick : double.PositiveInfinity;

    public long AddNecromancer(int bench, long count)
    {
        if (bench < 0 || bench >= _benches.Count) return 0;
        var b = _benches[bench];
        return _lich.TryAssign(Owner, b.ActivityId, Math.Min(count, Math.Max(0, Slots - Staffed(b))));
    }

    public long RemoveNecromancer(int bench, long count) =>
        bench < 0 || bench >= _benches.Count ? 0 : _lich.Unassign(Owner, _benches[bench].ActivityId, count);

    // ---- Bench level (one level for every bench; costs from research/bench.json tiers) ----

    public int BenchLevel { get; private set; } = 1;

    public int BenchMaxLevel => _bench.MaxLevel + LevelCapBonus;

    public bool IsBenchMaxLevel => BenchLevel >= BenchMaxLevel;

    /// <summary>What the next bench level costs (empty at max).</summary>
    public IReadOnlyList<ItemAmount> BenchUpgradeCosts => IsBenchMaxLevel ? Array.Empty<ItemAmount>() : _bench.CostOfLevel(BenchLevel + 1);

    public bool CanUpgradeBench(out string reason)
    {
        if (IsBenchMaxLevel) { reason = "Highest level reached"; return false; }
        var shortOf = BenchUpgradeCosts.FirstOrDefault(c => Stock.Get(c.Id) < c.Count);
        reason = shortOf is null ? "" : $"Not enough {StockIds.NameOf(_content, shortOf.Id).ToLowerInvariant()} yet";
        return shortOf is null;
    }

    /// <summary>Pays every cost and raises the bench level, or does nothing.</summary>
    public bool TryUpgradeBench()
    {
        if (!CanUpgradeBench(out _)) return false;
        foreach (var c in BenchUpgradeCosts) Stock.TryRemove(c.Id, c.Count);
        BenchLevel++;
        BenchLevelChanged?.Invoke();
        return true;
    }

    // ---- Start / cancel ----

    /// <summary>Starts a node on a bench (the first free one if not given). Progress kept from a cancel resumes.</summary>
    public StartResult Start(string nodeId, int? bench = null)
    {
        if (Node(nodeId) is null) return StartResult.UnknownNode;
        if (State(nodeId) != NodeState.Available) return StartResult.NotAvailable;
        var b = bench is int i ? (i >= 0 && i < _benches.Count && _benches[i].IsFree ? _benches[i] : null)
                               : _benches.FirstOrDefault(x => x.IsFree);
        if (b is null) return StartResult.NoFreeBench;
        b.NodeId = nodeId;
        b.ProgressMicro = _partial.GetValueOrDefault(nodeId);
        _partial.Remove(nodeId);
        NodeStarted?.Invoke(nodeId, b.Index);
        return StartResult.Ok;
    }

    /// <summary>Can a node be put on this bench (drag and drop)? Available nodes, or one researching on another bench.</summary>
    public bool CanPlace(string nodeId, int bench)
    {
        if (bench < 0 || bench >= _benches.Count || Node(nodeId) is null) return false;
        if (_benches[bench].NodeId == nodeId) return false;
        return State(nodeId) is NodeState.Available or NodeState.Researching;
    }

    /// <summary>
    /// Puts a node on a given bench (drag and drop): a node already on another bench moves with its progress, and
    /// whatever this bench was working on is taken off with its progress kept. Ok, or why not.
    /// </summary>
    public StartResult Place(string nodeId, int bench)
    {
        if (Node(nodeId) is null) return StartResult.UnknownNode;
        if (!CanPlace(nodeId, bench)) return StartResult.NotAvailable;
        if (_benches.FirstOrDefault(b => b.NodeId == nodeId) is { } from) Cancel(from.Index);   // progress kept
        if (!_benches[bench].IsFree) Cancel(bench);
        return Start(nodeId, bench);
    }

    /// <summary>Takes a bench's node off; its progress is kept for when it is started again. False if the bench is idle.</summary>
    public bool Cancel(int bench)
    {
        if (bench < 0 || bench >= _benches.Count || _benches[bench].NodeId is not { } id) return false;
        var b = _benches[bench];
        if (b.ProgressMicro > 0) _partial[id] = b.ProgressMicro;
        b.NodeId = null;
        b.ProgressMicro = 0;
        NodeCancelled?.Invoke(id, bench);
        return true;
    }

    // ---- Tomes ----

    /// <summary>Grants a tome (no-op if owned or unknown). Returns true if newly found.</summary>
    public bool GrantTome(string tomeId)
    {
        if (_owned.Contains(tomeId) || _tomes.All(t => t.Id != tomeId)) return false;
        var before = AvailableNodes().ToHashSet();
        _owned.Add(tomeId);
        TomeFound?.Invoke(tomeId);
        foreach (var id in AvailableNodes().Where(x => !before.Contains(x))) NodeAvailable?.Invoke(id);
        return true;
    }

    /// <summary>World hook: grants every tome found by clearing this area.</summary>
    public void OnAreaCleared(string areaId)
    {
        foreach (var t in _tomes.Where(t => t.Definition.FoundBy?.AreaCleared == areaId)) GrantTome(t.Id);
    }

    // ---- Debug ----

    /// <summary>Debug: research one level of a node now (no bench, requirements and tome not checked). False at max.</summary>
    public bool DebugComplete(string nodeId)
    {
        if (Node(nodeId) is null || Level(nodeId) >= MaxLevel(nodeId)) return false;
        if (TomeOf(nodeId) is { } t) _owned.Add(t.Id);
        foreach (var b in _benches.Where(b => b.NodeId == nodeId)) { b.NodeId = null; b.ProgressMicro = 0; }
        _partial.Remove(nodeId);
        Complete(nodeId);
        return true;
    }

    // ---- Tick ----

    public void Tick(long ticks)
    {
        var remaining = ticks;
        while (remaining > 0)
        {
            // The shortest time until any bench finishes a level; every bench advances by that much together.
            var active = _benches.Where(b => b.NodeId is not null).Select(b => (Bench: b, Work: FixedPoint.ToMicro(WorkPerTick(b)), Need: NeedMicro(b.NodeId!)))
                .Where(x => x.Work > 0).ToList();
            if (active.Count == 0) return;
            var seg = remaining;
            foreach (var (b, w, need) in active)
            {
                var left = (Int128)need - b.ProgressMicro;
                var t = left <= 0 ? 1 : (long)Int128.Min((left + w - 1) / w, long.MaxValue);
                seg = Math.Min(seg, Math.Max(1, t));
            }
            foreach (var (b, w, _) in active)
                b.ProgressMicro = FixedPoint.Clamp((Int128)b.ProgressMicro + (Int128)w * seg);
            remaining -= seg;
            foreach (var (b, _, _) in active) Finish(b);
        }
    }

    private long NeedMicro(string nodeId) => Math.Max(1, FixedPoint.ToMicro(WorkNeeded(nodeId)));

    /// <summary>Completes as many levels as the bench's progress pays for.</summary>
    private void Finish(Bench b)
    {
        while (b.NodeId is { } id && b.ProgressMicro >= NeedMicro(id))
        {
            var leftover = b.ProgressMicro - NeedMicro(id);
            Complete(id);
            if (Node(id)!.IsUpgrade && Level(id) < MaxLevel(id)) b.ProgressMicro = leftover;   // next level, same bench
            else { b.NodeId = null; b.ProgressMicro = 0; }
            _recap()?.AddGained("research", id, 1);
        }
    }

    private void Complete(string nodeId)
    {
        var (tome, node) = _nodes[nodeId];
        var before = AvailableNodes().ToHashSet();
        var work = node.WorkFor(Level(nodeId));
        _levels[nodeId] = Level(nodeId) + 1;
        Apply(node);
        if (tome.Definition.Skill.Length > 0) SkillXpEarned?.Invoke(tome.Definition.Skill, work * XpPerWork);
        NodeCompleted?.Invoke(nodeId, Level(nodeId));
        foreach (var id in AvailableNodes().Where(x => !before.Contains(x))) NodeAvailable?.Invoke(id);
    }

    private IEnumerable<string> AvailableNodes() => _nodes.Keys.Where(id => State(id) == NodeState.Available);

    // ---- Effects ----

    /// <summary>The (kind, id) pairs an unlock effect opens ("recipes" opens every transmutation in the group).</summary>
    private IEnumerable<(string Kind, string Id)> UnlockTargets(ResearchEffect e) => e.Kind switch
    {
        "recipes" => _content.All<TransmutationDefinitionBase>().Where(t => t.Group == e.Id).Select(t => (Unlocks.Transmutation, t.Id)),
        "unit" => new[] { (Unlocks.Unit, e.Id!) },
        "building" => new[] { (Unlocks.Building, e.Id!) },
        "spell" => new[] { (Unlocks.Spell, e.Id!) },
        "feature" => new[] { (Unlocks.Feature, e.Id!) },
        _ => Array.Empty<(string, string)>(),
    };

    /// <summary>The stat modifiers a node gives at a level.</summary>
    public static IEnumerable<Modifier> ModifiersFor(ResearchNodeDefinitionBase node, int level)
    {
        if (level <= 0) yield break;
        foreach (var e in node.Effects.Where(e => e.Type == "modify"))
        {
            var op = e.Op switch { "mul" => ModifierOp.Mul, "set" => ModifierOp.Set, _ => ModifierOp.Add };
            var value = !e.PerLevel ? e.Value : op switch
            {
                ModifierOp.Add => e.Value * level,
                ModifierOp.Mul => Math.Pow(e.Value, level),
                _ => e.Value,
            };
            yield return new Modifier(StatTargets.Key(LichStats.Alias(e.Stat!), e.Target), op, value, SourcePrefix + node.Id);
        }
    }

    /// <summary>Trigger and chance effects of researched nodes (for the systems that raise those events).</summary>
    public IEnumerable<ActiveResearchEffect> ActiveEffects(string type) =>
        _levels.Where(kv => kv.Value > 0 && Node(kv.Key) is not null)
            .SelectMany(kv => Node(kv.Key)!.Effects.Where(e => e.Type == type).Select(e => new ActiveResearchEffect(kv.Key, e, kv.Value)));

    private void Apply(ResearchNodeDefinitionBase node)
    {
        foreach (var e in node.Effects.Where(e => e.Type == "unlock"))
            foreach (var (kind, id) in UnlockTargets(e)) Unlocks.Unlock(kind, id);
        foreach (var u in _content.All<UnitDefinitionBase>().Where(u => u.Unlock?.Research == node.Id))
            Unlocks.Unlock(Unlocks.Unit, u.Id);
        SetModifiers(SourcePrefix + node.Id, ModifiersFor(node, Level(node.Id)).ToList());
        SyncBenches();
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
        if (_benches.Count >= BenchCount) return;
        while (_benches.Count < BenchCount) _benches.Add(new Bench(_benches.Count));
        BenchesChanged?.Invoke();
    }

    // ---- Save ----

    private void ResetToNewGame()
    {
        _levels.Clear();
        _partial.Clear();
        _owned.Clear();
        foreach (var t in _tomes.Where(t => t.Definition.StartsOwned)) _owned.Add(t.Id);
        Unlocks.Reset();
        _lich.Stats.RemoveWhere(m => m.Source.StartsWith(SourcePrefix, StringComparison.Ordinal));
        BenchLevel = 1;
        _benches.Clear();
        SyncBenches();
    }

    public SaveData Save() => new SaveData()
        .Set("tomes", _owned.OrderBy(x => x, StringComparer.Ordinal).ToList())
        .Set("levels", _levels.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value))
        .Set("partial", _partial.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value))
        .Set("benches", _benches.Select(b => new BenchDto { Node = b.NodeId, Progress = b.ProgressMicro }).ToList())
        .Set("bench_level", BenchLevel);

    /// <summary>
    /// Restores owned tomes, node levels, kept progress and benches, then rebuilds every derived effect from the
    /// definitions (unlocks, modifiers, bench count). Unknown nodes are dropped, so saves from the old research
    /// (Necromancy 1) start the new trees fresh. Levels are clamped to the current max.
    /// </summary>
    public void Load(SaveData data)
    {
        ResetToNewGame();
        if (data.Has("tomes"))
            foreach (var t in data.Get("tomes", new List<string>()))
                if (_tomes.Any(x => x.Id == t)) _owned.Add(t);
        foreach (var (id, level) in data.Get("levels", new Dictionary<string, int>()))
            if (Node(id) is not null && level > 0) _levels[id] = level;
        // Apply in a stable order, then clamp to max levels (which research itself can raise) and apply again.
        foreach (var id in _levels.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList()) Apply(Node(id)!);
        foreach (var id in _levels.Keys.ToList()) _levels[id] = Math.Min(_levels[id], MaxLevel(id));
        foreach (var id in _levels.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList()) Apply(Node(id)!);
        foreach (var (id, p) in data.Get("partial", new Dictionary<string, long>()))
            if (Node(id) is not null && p > 0 && Level(id) < MaxLevel(id)) _partial[id] = p;
        BenchLevel = Math.Clamp(data.Get("bench_level", 1), 1, BenchMaxLevel);
        SyncBenches();
        var saved = data.Get("benches", new List<BenchDto>());
        for (var i = 0; i < _benches.Count && i < saved.Count; i++)
        {
            var id = saved[i].Node;
            if (id is null || Node(id) is null || State(id) != NodeState.Available) continue;
            _benches[i].NodeId = id;
            _benches[i].ProgressMicro = Math.Max(0, saved[i].Progress);
        }
        // Benches hold fewer necromancers than the old research did: extra staff go back to the free pool.
        foreach (var b in _benches)
            if (Staffed(b) > Slots) _lich.Unassign(Owner, b.ActivityId, Staffed(b) - Slots);
        BenchesChanged?.Invoke();
        BenchLevelChanged?.Invoke();
    }

    public sealed class BenchDto
    {
        public string? Node { get; set; }
        public long Progress { get; set; }
    }
}
