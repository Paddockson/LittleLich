# Phase 3: TickManager

## Goal
A tick loop that processes the game in a fixed order, drives the tick bar, and can run many ticks at once for offline progress.

## Depends on
Phases 1 and 2.

## Responsibilities
- Run a tick every 2 seconds at startup. Speed-up and pause are possible later (open question).
- Raise `TickStarted` and `TickEnded` events.
- Read necromancer assignments from the LichManager and call the feature classes.
- Process things in a fixed order.
- Expose progress for the tick bar (`0.7s / 2.0s`).
- Trigger autosave every N ticks.
- Run batched ticks for offline progress.

## Tick order
1. Increment tick counter, raise `TickStarted`.
2. LichManager: buffs expire, regeneration, cooldown counters.
3. CryptManager buildings in flow order: Mana Well, Infuser, Soul Well, Transmuter, Summoning Circle, Undead Market.
4. Read assignments from LichManager and call the matching feature classes (skills first, then crafting).
5. Research benches.
6. Siege / encounter step (owner decided in phase 8).
7. Raise `TickEnded`, tell the UI, check autosave.

## Registration
Feature classes implement `ITickable` and register with the TickManager with a priority and group:
```csharp
TickManager.Register(ITickable feature, TickGroup group, int order);
public enum TickGroup { Lich, Buildings, Skills, Research, World }
```
Open question from the doc: how registration works for classes created later. Decision here: classes register themselves when their manager creates them, and unregister on dispose.

## Rates
All rates are per tick. A gathering result per tick = assigned necromancers x rate x modifiers. The feature class owns this maths; the TickManager only calls it.

## Offline progress
- GameManager hands over `elapsedSeconds`.
- `offlineTicks = floor(elapsedSeconds / tickLength)`, capped (set a max, for example 24 h).
- Do not loop one tick at a time for very large counts. Each feature class implements `Tick(long ticks)` with batched maths, so one call handles many ticks.
- Collect an `AwayRecap` (resources gained, XP gained, anything that capped out) for the recap screen later.
- Open question: changing tick length with speed-up must not break offline maths; always convert using the base tick length.

## Tasks
- [ ] `TickManager` autoload with timer, pause, and tick length.
- [ ] Register/unregister API with groups and order.
- [ ] Fixed order processing and events.
- [ ] Tick bar progress signal for the UI.
- [ ] Batched `Tick(long ticks)` contract documented and tested.
- [ ] Offline tick calculation with cap.
- [ ] `AwayRecap` collector.
- [ ] Autosave call into GameManager.

## Done when
- A dummy feature that adds 1 per tick shows the same total after 100 live ticks and after 100 offline ticks.
- Pausing stops the bar and the processing.
- Order is provably fixed (a test registers three features and checks call order).

## Out of scope
Real feature logic, the away recap screen.
