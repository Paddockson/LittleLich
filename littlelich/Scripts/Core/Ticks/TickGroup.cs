namespace LittleLich.Core.Ticks;

/// <summary>
/// Tick processing groups, run in this order every tick (see the tick order in the design doc):
///   Lich      2. buffs expire, cooldowns (no regeneration in v0.1)
///   Buildings 3. Workshop buildings in flow order (order: Mana Well, Infuser, Soul Well, Transmuter, Summoning Circle, Undead Market)
///   Skills    4. skill and crafting classes, each reading its own necromancer counts from the LichManager (gathering first, then crafting)
///   Research  5. research benches
///   World     6. siege / encounter step
/// Steps 1 (counter, TickStarted) and 7 (TickEnded, UI, autosave) belong to the scheduler itself.
/// </summary>
public enum TickGroup
{
    Lich = 0,
    Buildings = 1,
    Skills = 2,
    Research = 3,
    World = 4,
}

/// <summary>Suggested <c>order</c> values within a group, so features slot in without renumbering.</summary>
public static class TickOrder
{
    // Buildings, in flow order.
    public const int ManaWell = 100;
    public const int Infuser = 200;
    public const int SoulWell = 300;
    public const int Transmuter = 400;
    public const int SummoningCircle = 500;
    public const int UndeadMarket = 600;

    // Skills: gathering before crafting so crafted inputs gathered this tick are available.
    public const int Gathering = 100;
    public const int Crafting = 200;
}
