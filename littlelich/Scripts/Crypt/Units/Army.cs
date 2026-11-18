using System;
using System.Collections.Generic;
using System.Linq;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Ticks;

namespace LittleLich.Crypt.Units;

/// <summary>
/// The army formation (adjustment 03, save section "army"): a 3 by 3 grid of slots, each holding one unit id (a
/// player base id or a custom unit type id) or nothing. Index = row * 3 + col; col 0 is Back, 2 is Front (the enemy
/// is on the right). Each filled slot is a squad of up to <see cref="SquadSize"/> figures in battle; the lich keeps
/// the squads topped up (<see cref="LichSummoner"/>). The <see cref="UnitBuilder"/> owns it and enforces the rules;
/// this class only holds and saves the state.
/// </summary>
public sealed class ArmyFormation : ISaveable
{
    public const int Columns = 3;
    public const int Rows = 3;
    public const int SlotCount = Columns * Rows;
    public const int BackColumn = 0;
    public const int MidColumn = 1;
    public const int FrontColumn = 2;
    /// <summary>Figures per squad in battle.</summary>
    public const long SquadSize = 5;

    /// <summary>Unit id per slot, or null. Index = row * 3 + col.</summary>
    public string?[] Slots { get; } = new string?[SlotCount];

    /// <summary>Figures standing in each slot's squad (0 to <see cref="SquadSize"/>). A newly placed slot starts at 0.</summary>
    public long[] Figures { get; } = new long[SlotCount];

    /// <summary>The lich's progress toward the next summon, in micro-seconds of summoning (exact, for batching).</summary>
    internal long ProgressMicro { get; set; }

    /// <summary>Set by the Unit Builder: whether a saved unit id still exists (unknown ids are dropped on load).</summary>
    internal Func<string, bool> Exists { get; set; } = _ => true;

    public string SaveKey => "army";

    public static int Index(int row, int col) => row * Columns + col;
    public static int Row(int index) => index / Columns;
    public static int Column(int index) => index % Columns;
    public static bool IsSlot(int index) => index >= 0 && index < SlotCount;

    /// <summary>The battle section a slot fights in: col 0 back, 1 middle, 2 front.</summary>
    public static string SectionOf(int index) => Column(index) switch
    {
        FrontColumn => Sections.Front,
        MidColumn => Sections.Middle,
        _ => Sections.Back,
    };

    public long Missing(int index) => Slots[index] is null ? 0 : Math.Max(0, SquadSize - Figures[index]);

    public SaveData Save() => new SaveData()
        .Set("slots", Enumerable.Range(0, SlotCount).Select(i => new SlotDto { Unit = Slots[i], Figures = Slots[i] is null ? 0 : Figures[i] }).ToList())
        .Set("progress", ProgressMicro);

    public void Load(SaveData data)
    {
        Array.Clear(Slots);
        Array.Clear(Figures);
        var saved = data.Get("slots", new List<SlotDto>());
        for (var i = 0; i < SlotCount && i < saved.Count; i++)
        {
            if (saved[i]?.Unit is not { } u || !Exists(u)) continue;
            Slots[i] = u;
            Figures[i] = Math.Clamp(saved[i].Figures, 0, SquadSize);
        }
        ProgressMicro = Math.Max(0, data.Get("progress", 0L));
        Loaded?.Invoke();
    }

    /// <summary>Set by the Unit Builder: raise its events after a load.</summary>
    internal Action? Loaded { get; set; }

    public sealed class SlotDto
    {
        public string? Unit { get; set; }
        public long Figures { get; set; }
    }
}

/// <summary>One battle action as the Details box shows it.</summary>
public sealed class UnitAction
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public double CooldownSeconds { get; init; }
}

/// <summary>Read-only view of a unit for the Army page's Details box (adjustment 03).</summary>
public sealed class UnitDetails
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Melee, Ranged or Magic.</summary>
    public string Type { get; init; } = "";
    public bool IsCustom { get; init; }
    /// <summary>Custom units only: "Skeleton + copper sword + bone armour".</summary>
    public string? BuiltFrom { get; init; }
    public double Health { get; init; }
    public double Damage { get; init; }
    /// <summary>Seconds between attacks (the first damage action's cooldown).</summary>
    public double AttackSpeed { get; init; }
    public IReadOnlyList<UnitAction> Actions { get; init; } = Array.Empty<UnitAction>();
    /// <summary>What one summon spends: "sword, 2 bones".</summary>
    public string SummonCostText { get; init; } = "";
    /// <summary>How many the Crypt can still supply from its stock.</summary>
    public long Stock { get; init; }
}

/// <summary>
/// The lich keeps the army alive (adjustment 03): whenever a squad is below <see cref="ArmyFormation.SquadSize"/>,
/// the lich summons a replacement, one every <see cref="BaseSecondsPerSummon"/> seconds at base speed (stat
/// <see cref="SpeedStat"/>, a multiplier research and skills can raise). Runs on the tick, at home, during battle and
/// offline.
///
/// Each summon pays one unit's cost (body, weapon, armour, items) from the Crypt stock; a unit the stock cannot pay
/// for is skipped. The slot missing the most figures goes first; on a tie the more forward column (Front, Mid, Back),
/// then the top row. When nothing can be summoned the progress waits, full, for one summon.
/// Batched: Tick(n) equals n x Tick(1) while the stock does not change in between.
/// </summary>
public sealed class LichSummoner : ITickable
{
    /// <summary>Lich stat: summoning speed multiplier (1 = one figure every 4 seconds).</summary>
    public const string SpeedStat = "lich_summon_speed";
    public const double BaseSecondsPerSummon = 4;
    /// <summary>Figures one summon adds. Open question in the design: one figure or a whole squad. Change it here.</summary>
    public const long FiguresPerSummon = 1;

    private readonly LichState _lich;
    private readonly UnitBuilder _units;
    private readonly Func<AwayRecap?> _recap;

    public LichSummoner(LichState lich, UnitBuilder units, Func<AwayRecap?>? recap = null)
    {
        _lich = lich;
        _units = units;
        _recap = recap ?? (() => null);
    }

    /// <summary>Summoning speed multiplier (base 1).</summary>
    public double Speed => Math.Max(0, _lich.Stats.GetFor(SpeedStat, "army", Num.One).ToDouble());

    /// <summary>Seconds per summon at the current speed (infinity at speed 0).</summary>
    public double SecondsPerSummon => Speed > 0 ? BaseSecondsPerSummon / Speed : double.PositiveInfinity;

    /// <summary>0..1 progress toward the next summon.</summary>
    public double Fraction => Math.Clamp(_units.Army.ProgressMicro / (double)FixedPoint.ToMicro(BaseSecondsPerSummon), 0, 1);

    public void Tick(long ticks)
    {
        if (ticks <= 0) return;
        var army = _units.Army;
        var period = FixedPoint.ToMicro(BaseSecondsPerSummon);
        var progress = (Int128)army.ProgressMicro + (Int128)FixedPoint.ToMicro(OfflineTime.BaseTickSeconds * Speed) * ticks;
        while (progress >= period)
        {
            var slot = PickSlot(army, id => _units.Affordable(id).Count >= 1);
            if (slot < 0) break;
            var unit = army.Slots[slot]!;
            var made = _units.SummonIntoSlot(slot, FiguresPerSummon);
            if (made <= 0) break;
            _recap()?.AddGained("units", unit, made);
            progress -= period;
        }
        army.ProgressMicro = (long)(progress >= period ? period : progress);
    }

    /// <summary>
    /// The slot the next summon goes to, or -1: a filled slot below squad size whose unit the stock can pay for,
    /// missing the most figures; ties go Front, Mid, Back, then top row first.
    /// </summary>
    public static int PickSlot(ArmyFormation army, Func<string, bool> canSupply)
    {
        var best = -1;
        for (var i = 0; i < ArmyFormation.SlotCount; i++)
        {
            if (army.Slots[i] is not { } unit || army.Missing(i) <= 0 || !canSupply(unit)) continue;
            if (best < 0 || Better(army, i, best)) best = i;
        }
        return best;
    }

    private static bool Better(ArmyFormation army, int a, int b)
    {
        var ma = army.Missing(a);
        var mb = army.Missing(b);
        if (ma != mb) return ma > mb;
        var ca = ArmyFormation.Column(a);
        var cb = ArmyFormation.Column(b);
        if (ca != cb) return ca > cb;
        return ArmyFormation.Row(a) < ArmyFormation.Row(b);
    }
}
