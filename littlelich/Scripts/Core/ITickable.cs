namespace LittleLich.Core;

/// <summary>
/// Anything that advances on the game tick. Registered with the TickManager under a
/// <see cref="Ticks.TickGroup"/> and an order when its manager creates it; unregistered on dispose.
///
/// Batching contract: Tick(n) must give the same result as n calls of Tick(1), so offline progress
/// (up to 43,200 ticks) is one call, not a loop. Do the maths in closed form:
///   gathered = assigned x rate x modifiers x n, clamped to the storage cap;
///   timers   = compare an absolute tick number against an end tick (see LichState buffs);
///   crafting = completions = min(n x speed, what the inputs allow), consuming inputs once.
/// Rates are always per tick. Report offline gains to <c>TickScheduler.ActiveRecap</c> when it is not null.
/// </summary>
public interface ITickable
{
    void Tick(long ticks);
}
