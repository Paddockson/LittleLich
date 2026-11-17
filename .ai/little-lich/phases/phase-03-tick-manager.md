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
- [x] `TickManager` autoload with timer, pause, and tick length.
- [x] Register/unregister API with groups and order.
- [x] Fixed order processing and events.
- [x] Tick bar progress signal for the UI.
- [x] Batched `Tick(long ticks)` contract documented and tested.
- [x] Offline tick calculation with cap.
- [x] `AwayRecap` collector.
- [x] Autosave call into GameManager.

## Done when
- A dummy feature that adds 1 per tick shows the same total after 100 live ticks and after 100 offline ticks.
- Pausing stops the bar and the processing.
- Order is provably fixed (a test registers three features and checks call order).

## Out of scope
Real feature logic, the away recap screen.

## Status: complete (2026-11-17)

### Decisions
- **Plain C# core, thin Godot layer** (as in phases 1–2). `Scripts/Core/Ticks/`: `TickScheduler` (registration, fixed order, events, offline runs), `TickClock` (real time to ticks, tick bar, pause, speed), `AwayRecap`, `TickGroup` + `TickOrder`. The `TickManager` autoload (`Scripts/Managers/TickManager.cs`, third autoload, `Instance` set in its constructor) drives them from `_Process` and turns events into signals: `TickStarted(tick, count)`, `TickEnded(tick, count)`, `TickProgress(elapsed, tickSeconds)` (every frame), `PausedChanged`, `SpeedChanged`, `OfflineProcessed(ticks, seconds)`, `TickError`. There is also a C# `OfflineRecap(AwayRecap)` event and `LastRecap`.
- **Timer:** the tick timer is a real-time accumulator in `_Process`, not a Godot `Timer`, so the bar has exact progress and late frames don't lose time. If several ticks are due in one frame (a hitch or speed-up), they run as one batch.
- **Order:** groups `Lich → Buildings → Skills → Research → World`, then `order` within a group, then registration order. `TickOrder` constants give flow order for buildings (Mana Well 100 … Undead Market 600) and gathering (100) before crafting (200). Steps 1 and 7 (counter + `TickStarted`, `TickEnded`) belong to the scheduler.
- **Registration (open question settled as the spec suggested):** a feature registers when its manager creates it and unregisters on dispose. `LichManager` registers its `LichState` (group Lich) in `_Ready` and unregisters in `_ExitTree`. Registering the same object twice throws. Changes made during a tick take effect immediately: a feature unregistered mid-tick is not called again, and one registered mid-tick starts next tick.
- **Step 4 (assignments):** skill classes register in the Skills group and each reads its own necromancer counts from the LichManager when called. The pool stays the single source of truth; the TickManager does not hand counts around.
- **Batching contract** is documented on `ITickable` and `TickScheduler`. A batch of n calls each feature once with n, in group order (not tick by tick interleaved), so features must use closed-form maths and must not depend on another feature's same-tick output beyond the group order.
- **Not re-entrant:** calling `RunTicks` from inside a tick throws. In the Godot layer, any exception from a feature is logged, raised as `TickError`, and **pauses ticking** so the state can be inspected.
- **Offline:** on boot (in the TickManager's `_Ready`, after every manager has registered) and on every `GameStarted` (load / new game), the TickManager takes `TakePendingOfflineSeconds()` once and calls `TickScheduler.RunOffline`. Time is always converted with `OfflineTime.BaseTickSeconds` (2 s) and capped at **24 h = 43,200 ticks** (`OfflineTime.MaxOfflineSeconds/MaxOfflineTicks`). Ticks over the cap are reported in the recap (`TicksLostToCap`). If nothing was capped, the part-tick left over is carried into the tick bar.
- **AwayRecap:** `ElapsedSeconds`, `Ticks`, `TicksLostToCap`, `AddGained(kind, id, amount)`, `MarkCapped(id)`, `Describe()`. While an offline run is processed it is available as `TickScheduler.ActiveRecap` (null for live ticks), and features add to it. For now the recap is printed and logged in the dev UI; the recap screen is out of scope.
- **Pause and speed (speed-up is still an open design question):** both are runtime-only and not saved. Speed is a dev time scale (0.1x–100x) on the clock: it fills the bar faster but never changes the tick length or offline maths.
- **Autosave:** after every batch the TickManager calls `GameManager.OnTicksProcessed(n)`, which advances `GameMeta.TotalTicks` and saves every 30 ticks (`TickManager.AutosaveEveryTicks`, 60 s). An offline batch counts fully, so a long time away saves straight after it is processed. `TickScheduler.TickCount` is synced from `TotalTicks` whenever a game starts.
- Removed the phase-0 stopgap `GameManager.DebugRunTicks`. The dev `ticks` command now uses `TickManager.RunTicks`.

### Verified
- `dotnet test tests/LittleLich.Tests`: 94 tests (16 new), covering:
  - **Done when:** a dummy +1-per-tick feature gives 100 after 100 live ticks fed through the clock at 60 fps and 100 after 200 s offline (one batched call); pausing stops both the bar and the ticks; eight features registered out of order run in a fixed order.
  - Also: event order, re-entrancy, register/unregister (including mid-tick), the 24 h cap with lost ticks reported, partial and negative time, the recap only during offline runs, lich buffs expiring the same live and offline, speed not touching offline, and autosave every 30 ticks with total ticks surviving a reload.
- Godot 4.4.1 at 1280x720:
  - Boot processed 138 ticks of real time away.
  - `mod max_insight add 5 3; ticks 2; ticks 1` expired the buff on schedule.
  - `offline 8h` ran 14,400 ticks; `offline 3d` ran 43,200 and reported the 86,400 lost to the cap; autosave fired.
  - `speed 5; pause` froze the bar.
  - A separate run at `speed 100` ticked live in batches.

### Follow-ups (not in this phase)
- Away recap screen (out of scope; `AwayRecap` is ready for it).
- Decide whether speed-up becomes a player feature (open question); if so, decide whether it is saved.
- Phase 4: skill classes register in `TickGroup.Skills` with `TickOrder.Gathering` / `TickOrder.Crafting` and report offline gains to `ActiveRecap`.
