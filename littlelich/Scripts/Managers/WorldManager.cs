using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content;
using LittleLich.Crypt;
using LittleLich.World;

namespace LittleLich.Managers;

/// <summary>
/// Autoload singleton (after CryptManager, before TickManager). Thin Godot adapter over <see cref="WorldState"/>
/// (the map, area states, the garrison stub and the one active battle). Owner decision for tick step 6: the
/// WorldManager owns the map and creates a <see cref="BattleController"/> per fight; the battle runs on its own clock,
/// fed by <see cref="TickManager.GameSecondsElapsed"/> (so it pauses with the game and follows the speed).
/// </summary>
public partial class WorldManager : Node
{
    public static WorldManager Instance { get; private set; } = null!;

    [Signal] public delegate void BattleStartedEventHandler(string areaId);
    /// <summary>(area, outcome as int: Victory, Defeat, Retreat).</summary>
    [Signal] public delegate void BattleEndedEventHandler(string areaId, int outcome);
    /// <summary>An action fired (side 0 attacker / 1 defender, unit id, action id, amount).</summary>
    [Signal] public delegate void BattleActionFiredEventHandler(int side, string unitId, string actionId, double amount);
    [Signal] public delegate void AreaStateChangedEventHandler(string areaId);

    public WorldState? State { get; private set; }

    /// <summary>C# event with every action detail (signals cannot carry the record).</summary>
    public event Action<BattleAction>? ActionFired;
    /// <summary>C# event with the full result.</summary>
    public event Action<BattleResult>? Resolved;

    public WorldManager() => Instance = this;

    /// <summary>Boot step 3: creates the world for the loaded content. Returns its saveable.</summary>
    public WorldState Initialize(ContentDatabase content, Stockpile stock, Crypt.Units.UnitBuilder units)
    {
        if (State is not null) throw new InvalidOperationException("WorldManager is already initialised.");
        State = new WorldState(content, stock, units);
        State.BattleStarted += area =>
        {
            State.Battle!.ActionFired += a =>
            {
                ActionFired?.Invoke(a);
                EmitSignal(SignalName.BattleActionFired, (int)a.Source.Side, a.Source.Unit.Id, a.Action.Id, a.Amount);
            };
            EmitSignal(SignalName.BattleStarted, area);
        };
        State.BattleEnded += r =>
        {
            Resolved?.Invoke(r);
            EmitSignal(SignalName.BattleEnded, r.AreaId, (int)r.Outcome);
        };
        State.AreaStateChanged += id => EmitSignal(SignalName.AreaStateChanged, id);
        return State;
    }

    public override void _Ready()
    {
        TickManager.Instance.GameSecondsElapsed += s => State?.Advance(s);
    }

    // ---- Commands ----

    /// <summary>Sends the army formation's squads, as they stand, at an area.</summary>
    public SendResult Send(string areaId) => State?.Send(areaId) ?? SendResult.UnknownArea;
    public bool Retreat() => State?.Retreat() ?? false;
}
