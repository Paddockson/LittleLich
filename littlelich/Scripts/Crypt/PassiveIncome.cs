using System;
using LittleLich.Core;
using LittleLich.Core.Lich;
using LittleLich.Core.Ticks;

namespace LittleLich.Crypt;

/// <summary>
/// Income research grants with no building (research revamp): soul_harvest_rate souls per tick (Soul Harvesting sets
/// it to 2). Goes into Storage and stops at the cap. Batched exactly (fractions carry; the carry is not saved).
/// </summary>
public sealed class PassiveIncome : ITickable
{
    public const string SoulRateStat = "soul_harvest_rate";
    public const string Souls = "souls";

    private readonly LichState _lich;
    private readonly Stockpile _stock;
    private readonly Func<AwayRecap?> _recap;
    private long _carry;

    public PassiveIncome(LichState lich, Stockpile stock, Func<AwayRecap?>? recap = null)
    {
        _lich = lich;
        _stock = stock;
        _recap = recap ?? (() => null);
    }

    /// <summary>Souls per tick right now.</summary>
    public double SoulsPerTick => Math.Max(0, _lich.Stats.Get(SoulRateStat).ToDouble());

    public void Tick(long ticks)
    {
        var rate = FixedPoint.ToMicro(SoulsPerTick);
        if (ticks <= 0 || rate <= 0) return;
        var (ops, carry, limited) = FixedPoint.Run(_carry, rate, ticks, FixedPoint.Micro, _stock.Room(Souls));
        _carry = carry;
        if (ops > 0)
        {
            _stock.Add(Souls, ops);
            _recap()?.AddGained("resource", Souls, ops);
        }
        if (limited) _recap()?.MarkCapped(Souls);
    }
}
