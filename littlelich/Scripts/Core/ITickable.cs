namespace LittleLich.Core;

/// <summary>
/// Anything that advances on the game tick.
/// Contract: Tick(n) must give the same result as n calls of Tick(1), so offline progress
/// can be processed in one batched call.
/// </summary>
public interface ITickable
{
    void Tick(long ticks);
}
