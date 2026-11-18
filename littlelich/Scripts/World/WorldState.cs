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
    /// <summary>No slot of the formation has a figure standing yet.</summary>
    EmptyArmy,
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
    /// <summary>Figures that fell (sent includes the lich's replacements during the fight).</summary>
    public long Lost => Sent.Sum(kv => kv.Value) - Survivors.Sum(kv => kv.Value);
}

/// <summary>
/// The world map (save section "world"): map nodes and paths, area states and the one active battle
/// (v0.1: one at a time). Battles run on their own clock via <see cref="Advance"/>.
///
/// The army is the Unit Builder's 3 by 3 formation (adjustment 03): each filled slot fights as one squad of up to
/// five figures in its column's section (Back, Mid, Front). The lich keeps summoning during the fight
/// (<see cref="LichSummoner"/>), and each new figure joins its squad's battle group. Losses are written back to the
/// squads as the battle runs, so survivors are simply the figures still standing. The formation is locked while a
/// battle runs.
///
/// Save: cleared areas and the area being fought. A battle is not saved mid-fight: on load it restarts from the
/// beginning with the squads as they were saved.
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
        units.ArmyLocked = () => Battle is not null;
        units.FiguresSummoned += (slot, n) => Battle?.Reinforce(slot, n);
        Nodes = content.All<MapNodeDefinition>().ToList();
        Areas = content.All<AreaDefinition>().ToList();
    }

    public string SaveKey => "world";

    public IReadOnlyList<MapNodeDefinition> Nodes { get; }
    public IReadOnlyList<AreaDefinition> Areas { get; }

    public BattleController? Battle { get; private set; }
    public string? BattleArea { get; private set; }
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

    // ---- The army (the Unit Builder's formation) ----

    public UnitBuilder Units => _units;

    /// <summary>Figures standing across every squad.</summary>
    public long ArmyFigures => Enumerable.Range(0, ArmyFormation.SlotCount).Sum(i => _units.GetSlot(i) is null ? 0 : _units.Figures(i));

    /// <summary>Total HP of the squads as they stand (figures x unit HP with gear).</summary>
    public double ArmyHp => Enumerable.Range(0, ArmyFormation.SlotCount)
        .Sum(i => _units.GetSlot(i) is { } u ? _units.Figures(i) * _units.BattleUnit(u).Hp : 0);

    /// <summary>The formation as battle entries: one squad per filled slot, refillable to five.</summary>
    private List<BattleEntry> ArmyEntries() => Enumerable.Range(0, ArmyFormation.SlotCount)
        .Where(i => _units.GetSlot(i) is not null)
        .Select(i => new BattleEntry(ArmyFormation.SectionOf(i), _units.BattleUnit(_units.GetSlot(i)!), _units.Figures(i), ArmyFormation.SquadSize, i))
        .ToList();

    // ---- Battles ----

    /// <summary>Sends the formation's squads, as they stand, at an area.</summary>
    public SendResult Send(string areaId)
    {
        if (Area(areaId) is not { } area) return SendResult.UnknownArea;
        if (Battle is not null) return SendResult.BattleRunning;
        if (_cleared.Contains(areaId)) return SendResult.AlreadyCleared;
        if (!IsReachable(areaId)) return SendResult.NotReachable;
        if (ArmyFigures <= 0) return SendResult.EmptyArmy;
        Start(area);
        return SendResult.Ok;
    }

    private void Start(AreaDefinition area)
    {
        var defenders = area.Defenders.SelectMany(s => s.Value.Select(u => new BattleEntry(s.Key, BattleUnit.From(Unit(u.Unit)), u.Count)));
        Battle = new BattleController(ArmyEntries(), defenders);
        BattleArea = area.Id;
        BattleStarted?.Invoke(area.Id);
        AreaStateChanged?.Invoke(area.Id);
    }

    /// <summary>Runs the active battle on its own clock (game seconds; nothing happens while paused). Resolves it when it ends.</summary>
    public void Advance(double seconds)
    {
        if (Battle is not { } b) return;
        b.Advance(seconds);
        SyncFigures(b);
        if (b.IsOver) Resolve();
    }

    /// <summary>Pull the army back now; the squads keep the figures still standing.</summary>
    public bool Retreat()
    {
        if (Battle is not { } b) return false;
        b.Retreat();
        Resolve();
        return true;
    }

    /// <summary>Battle losses go back to the formation's squads.</summary>
    private void SyncFigures(BattleController b)
    {
        foreach (var g in b.GroupsOf(BattleSide.Attacker))
            if (g.Slot >= 0) _units.SyncFigures(g.Slot, g.Alive);
    }

    private void Resolve()
    {
        var b = Battle!;
        var area = Area(BattleArea!)!;
        SyncFigures(b);
        var rewards = new Dictionary<string, long>();
        if (b.Outcome == BattleOutcome.Victory)
        {
            _cleared.Add(area.Id);
            foreach (var (id, n) in area.Rewards) rewards[id] = _stock.Add(id, n);
        }
        LastResult = new BattleResult(area.Id, b.Outcome!.Value, b.NowSeconds, b.Fielded(BattleSide.Attacker), b.Survivors(BattleSide.Attacker), rewards);
        Battle = null;
        BattleArea = null;
        AreaStateChanged?.Invoke(area.Id);
        BattleEnded?.Invoke(LastResult);
    }

    private UnitDefinitionBase Unit(string id) => _content.Get<UnitDefinitionBase>(id);

    // ---- Save ----

    public SaveData Save() => new SaveData()
        .Set("cleared", _cleared.OrderBy(x => x, StringComparer.Ordinal).ToList())
        .Set("battle", BattleArea is null ? null : new BattleDto { Area = BattleArea });

    /// <summary>
    /// Restores cleared areas; a battle that was running restarts from the beginning with the saved squads.
    /// Loads after the army. Older saves' "garrison" and battle "army" (the roster) are ignored.
    /// </summary>
    public void Load(SaveData data)
    {
        _cleared.Clear();
        Battle = null;
        BattleArea = null;
        foreach (var id in data.Get("cleared", new List<string>()))
            if (Area(id) is not null) _cleared.Add(id);
        if (data.Get<BattleDto?>("battle", null) is { Area: { } areaId } && Area(areaId) is { } area && !_cleared.Contains(areaId) && ArmyFigures > 0)
            Start(area);
        foreach (var a in Areas) AreaStateChanged?.Invoke(a.Id);
    }

    public sealed class BattleDto
    {
        public string? Area { get; set; }
    }
}
