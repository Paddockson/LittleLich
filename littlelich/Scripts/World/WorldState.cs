using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Crypt;
using LittleLich.Crypt.Units;

namespace LittleLich.World;

public enum AreaState { NotStarted, InProgress, Cleared }

public enum SendResult
{
    Ok,
    UnknownArea,
    /// <summary>v0.1: one battle at a time.</summary>
    BattleRunning,
    AlreadyCleared,
    /// <summary>No path from home through cleared ground.</summary>
    NotReachable,
    EmptyArmy,
    /// <summary>The roster does not have that many of a unit type.</summary>
    NotEnoughUnits,
    UnknownUnit,
}

/// <summary>An army to send: section -> unit type id -> count.</summary>
public sealed class ArmyPlan
{
    public Dictionary<string, Dictionary<string, long>> Sections { get; set; } = new();

    public ArmyPlan Set(string section, string unit, long count)
    {
        if (!Sections.TryGetValue(section, out var s)) Sections[section] = s = new Dictionary<string, long>();
        s[unit] = count;
        return this;
    }

    public IEnumerable<(string Section, string Unit, long Count)> Entries =>
        Sections.SelectMany(s => s.Value.Where(u => u.Value > 0).Select(u => (s.Key, u.Key, u.Value)));

    public Dictionary<string, long> Totals() =>
        Entries.GroupBy(e => e.Unit).ToDictionary(g => g.Key, g => g.Sum(e => e.Count));
}

/// <summary>How a battle went, for the log and the UI.</summary>
public sealed record BattleResult(
    string AreaId,
    BattleOutcome Outcome,
    double Seconds,
    IReadOnlyDictionary<string, long> Sent,
    IReadOnlyDictionary<string, long> Survivors,
    IReadOnlyDictionary<string, long> Rewards)
{
    public long Lost => Sent.Sum(kv => kv.Value) - Survivors.Sum(kv => kv.Value);
}

/// <summary>
/// The world map (save section "world"): map nodes and paths, area states and the one active battle
/// (v0.1: one at a time, no restocking). Battles run on their own clock via <see cref="Advance"/>.
///
/// Armies come from the Unit Builder's roster (phase 9): sending takes units of each type out of the roster, and
/// survivors return to it when the battle ends. Each type fights with its gear (<see cref="UnitBuilder.BattleUnit"/>).
/// A formation turns into an army with <see cref="PlanFor"/>.
///
/// Save: cleared areas and the army out fighting. A battle is not saved mid-fight: on load it
/// restarts from the beginning with the same army.
/// </summary>
public sealed class WorldState : ISaveable
{
    private readonly ContentDatabase _content;
    private readonly Stockpile _stock;
    private readonly HashSet<string> _cleared = new(StringComparer.Ordinal);
    private readonly UnitBuilder _units;

    public WorldState(ContentDatabase content, Stockpile stock, UnitBuilder units)
    {
        _content = content;
        _stock = stock;
        _units = units;
        units.InBattle = InBattle;
        Nodes = content.All<MapNodeDefinition>().ToList();
        Areas = content.All<AreaDefinition>().ToList();
    }

    public string SaveKey => "world";

    public IReadOnlyList<MapNodeDefinition> Nodes { get; }
    public IReadOnlyList<AreaDefinition> Areas { get; }

    public BattleController? Battle { get; private set; }
    public string? BattleArea { get; private set; }
    public ArmyPlan? BattlePlan { get; private set; }
    public BattleResult? LastResult { get; private set; }

    public event Action<string>? BattleStarted;
    public event Action<BattleResult>? BattleEnded;
    public event Action<string>? AreaStateChanged;

    public AreaDefinition? Area(string id) => Areas.FirstOrDefault(a => a.Id == id);

    public AreaState State(string areaId) =>
        _cleared.Contains(areaId) ? AreaState.Cleared : BattleArea == areaId ? AreaState.InProgress : AreaState.NotStarted;

    /// <summary>
    /// An area is reachable when a path leads to its node from a home node, passing only through nodes that hold no
    /// area or a cleared area.
    /// </summary>
    public bool IsReachable(string areaId)
    {
        var target = Nodes.FirstOrDefault(n => n.Area == areaId);
        if (target is null) return false;
        var byId = Nodes.ToDictionary(n => n.Id);
        var links = Nodes.ToDictionary(n => n.Id, n => new HashSet<string>(n.Links));
        foreach (var n in Nodes)
            foreach (var l in n.Links)
                if (links.TryGetValue(l, out var back)) back.Add(n.Id);   // paths go both ways

        var seen = new HashSet<string>();
        var queue = new Queue<string>(Nodes.Where(n => n.Home).Select(n => n.Id));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id)) continue;
            if (id == target.Id) return true;
            var passable = byId[id].Area is not { } a || _cleared.Contains(a) || byId[id].Home;
            if (!passable) continue;
            foreach (var next in links[id]) queue.Enqueue(next);
        }
        return false;
    }

    // ---- Roster (the Unit Builder's) ----

    public UnitBuilder Units => _units;

    /// <summary>Units of a type in the roster (not counting any out fighting).</summary>
    public long InRoster(string typeId) => _units.Count(typeId);

    /// <summary>
    /// Debug: add units without summoning. Accepts a unit type id, or a player base id (uses its gear-less type).
    /// </summary>
    public void AddUnits(string typeOrBaseId, long count)
    {
        var type = _units.Type(typeOrBaseId) ?? _units.EnsureBare(typeOrBaseId);
        _units.AddToRoster(type.Id, count);
    }

    /// <summary>A formation as an army: each filled slot sends its count of its unit type from its section.</summary>
    public ArmyPlan PlanFor(Formation formation)
    {
        var plan = new ArmyPlan();
        foreach (var s in formation.Slots.Where(s => s.UnitType is not null && s.Count > 0))
        {
            var current = plan.Sections.TryGetValue(s.Section, out var sec) ? sec.GetValueOrDefault(s.UnitType!) : 0;
            plan.Set(s.Section, s.UnitType!, current + s.Count);
        }
        return plan;
    }

    /// <summary>True while units of this type are out fighting (the Unit Builder will not delete the type).</summary>
    public bool InBattle(string typeId) => BattlePlan?.Totals().ContainsKey(typeId) ?? false;

    // ---- Battles ----

    public SendResult Send(string areaId, Formation formation) => Send(areaId, PlanFor(formation));

    public SendResult Send(string areaId, ArmyPlan plan)
    {
        if (Area(areaId) is not { } area) return SendResult.UnknownArea;
        if (Battle is not null) return SendResult.BattleRunning;
        if (_cleared.Contains(areaId)) return SendResult.AlreadyCleared;
        if (!IsReachable(areaId)) return SendResult.NotReachable;
        var totals = plan.Totals();
        if (totals.Count == 0) return SendResult.EmptyArmy;
        if (totals.Keys.Any(t => _units.Type(t) is null)) return SendResult.UnknownUnit;
        if (plan.Entries.Any(e => !Content.Definitions.Sections.All.Contains(e.Section))) return SendResult.UnknownUnit;
        if (totals.Any(t => InRoster(t.Key) < t.Value)) return SendResult.NotEnoughUnits;

        foreach (var (unit, n) in totals) _units.TakeFromRoster(unit, n);
        Start(area, plan);
        return SendResult.Ok;
    }

    private void Start(AreaDefinition area, ArmyPlan plan)
    {
        var attackers = plan.Entries.Select(e => (e.Section, _units.BattleUnit(e.Unit), e.Count));
        var defenders = area.Defenders.SelectMany(s => s.Value.Select(u => (s.Key, Unit(u.Unit), u.Count)));
        Battle = new BattleController(attackers, defenders);
        BattleArea = area.Id;
        BattlePlan = plan;
        BattleStarted?.Invoke(area.Id);
        AreaStateChanged?.Invoke(area.Id);
    }

    /// <summary>Runs the active battle on its own clock (game seconds; nothing happens while paused). Resolves it when it ends.</summary>
    public void Advance(double seconds)
    {
        if (Battle is not { } b) return;
        b.Advance(seconds);
        if (b.IsOver) Resolve();
    }

    /// <summary>Pull the army back now; survivors return to the roster.</summary>
    public bool Retreat()
    {
        if (Battle is not { } b) return false;
        b.Retreat();
        Resolve();
        return true;
    }

    private void Resolve()
    {
        var b = Battle!;
        var area = Area(BattleArea!)!;
        var survivors = b.Survivors(BattleSide.Attacker);
        foreach (var (unit, n) in survivors.Where(s => s.Value > 0)) _units.AddToRoster(unit, n);

        var rewards = new Dictionary<string, long>();
        if (b.Outcome == BattleOutcome.Victory)
        {
            _cleared.Add(area.Id);
            foreach (var (id, n) in area.Rewards) rewards[id] = _stock.Add(id, n);
        }
        LastResult = new BattleResult(area.Id, b.Outcome!.Value, b.NowSeconds, BattlePlan!.Totals(), survivors, rewards);
        Battle = null;
        BattleArea = null;
        BattlePlan = null;
        AreaStateChanged?.Invoke(area.Id);
        BattleEnded?.Invoke(LastResult);
    }

    private UnitDefinitionBase Unit(string id) => _content.Get<UnitDefinitionBase>(id);

    // ---- Save ----

    public SaveData Save() => new SaveData()
        .Set("cleared", _cleared.OrderBy(x => x, StringComparer.Ordinal).ToList())
        .Set("battle", BattleArea is null ? null : new BattleDto { Area = BattleArea, Army = BattlePlan!.Sections });

    /// <summary>
    /// Restores cleared areas; an army that was out fighting restarts its battle from the beginning.
    /// Loads after the Unit Builder. A phase 8 save's "garrison" (unit base ids) moves into the roster as gear-less types.
    /// </summary>
    public void Load(SaveData data)
    {
        _cleared.Clear();
        Battle = null;
        BattleArea = null;
        BattlePlan = null;
        foreach (var id in data.Get("cleared", new List<string>()))
            if (Area(id) is not null) _cleared.Add(id);
        foreach (var (id, n) in data.Get("garrison", new Dictionary<string, long>()))
            if (n > 0 && _content.TryGet<UnitDefinitionBase>(id, out var u) && u.Side == "player") AddUnits(id, n);

        if (data.Get<BattleDto?>("battle", null) is { Area: { } areaId } dto && Area(areaId) is { } area && !_cleared.Contains(areaId))
        {
            var plan = new ArmyPlan();
            foreach (var (section, units) in dto.Army ?? new())
                foreach (var (unit, n) in units)
                {
                    // Phase 8 saves used base ids: give them their gear-less type.
                    var type = _units.Type(unit) ?? (_content.TryGet<UnitDefinitionBase>(unit, out var b) && b.Side == "player" ? _units.EnsureBare(unit) : null);
                    if (n > 0 && type is not null && Content.Definitions.Sections.All.Contains(section)) plan.Set(section, type.Id, n);
                }
            if (plan.Totals().Count > 0) Start(area, plan);
        }
        foreach (var a in Areas) AreaStateChanged?.Invoke(a.Id);
    }

    public sealed class BattleDto
    {
        public string? Area { get; set; }
        public Dictionary<string, Dictionary<string, long>>? Army { get; set; }
    }
}
