# Phase 7: Research (ResearchManager, benches, UnlockManager)

Superseded design: see `design/research-revamp.md` for the full design, data model, effect catalog and open decisions. Data: `data/refined/research/*.json` (78 nodes, structured effects, generated layout). UI: `ui-reference/images/03-research.png`.

## Goal
Research works end to end: tomes, a node tree, benches staffed by necromancers, and effects that unlock and change the game.

## Depends on
Phases 2 to 5 (LichManager assignments and modifiers, TickManager, CryptManager). The Workshop and Summoning Circle read unlocks from this phase.

## v0.1 scope
- Tomes: **Necromancy (33 nodes) and Infusion (27 nodes)**. Crypt Magic and Spider Sorcery data exists but ships disabled.
- Unit research (unit level-ups on benches) is an open decision. Build the bench job type so it can be added.

## Classes
```csharp
public class UnlockManager : ISaveable {          // set of researched node ids and levels
    public bool IsUnlocked(string kind, string id);   // "unit","building","spell","feature","recipes"
    public int NodeLevel(string nodeId);
    public event Action<string> NodeResearched;
}
public class ResearchManager : ISaveable, ITickable {  // owned by CryptManager
    public IReadOnlyList<Tome> Tomes;                  // found tomes only
    public IReadOnlyList<Bench> Benches;
    public bool CanStart(string nodeId, out string reason);
    public bool Start(string nodeId, int benchIndex);
    public void Cancel(int benchIndex);
    public bool AssignNecromancers(int benchIndex, long n);   // via LichManager
    public override void Tick(long ticks);                     // batched
}
public class Bench { public string JobId; public double Progress; public long Necromancers; public int Level; }
```
- Effects are applied by an `EffectApplier` that reads the node's `effects` list: `unlock` goes to UnlockManager, `modify` registers a modifier (with `target` and `per_level`), `trigger` and `chance` register event handlers.
- Work per tick = `(1 + 0.5 x necromancers) x 1.1^(bench level - 1) x research efficiency` (placeholder, in JSON).
- A node is available when every id in `requires` is researched. Only available and researched nodes are shown (no locked nodes). Upgrade nodes cost `work x growth^level` per level up to `max_level`.
- Processed in tick step 5. Offline progress batched; completed nodes appear in the away recap.

## Tasks
- [ ] Load the four tome files into the content database with validation (ids, requires, no cycles, effect shapes).
- [ ] `UnlockManager` and its events; the Workshop and Summoning Circle read from it.
- [ ] `ResearchManager`: benches, start, cancel, assign necromancers, batched tick.
- [ ] `EffectApplier` for `unlock`, `modify`, `trigger`, `chance`, with modifier `target` filters in the LichManager.
- [ ] Bench level upgrades with costs from `buildings.json` (`research_bench`).
- [ ] Bench count from research (Research Assistance 1 and 2).
- [ ] Save/load: researched ids, levels, bench jobs, progress, bench levels.
- [ ] Research page (see `phases/phase-00-ui-spec.md` section 2 and image 03).
- [ ] Unit tests: requirement gating, upgrade level costs, batched ticks equal single ticks, effects apply and persist across save.

## Done when
- Researching Summoning, Soul Harvesting and Animate Bones in order unlocks the Summoning Circle units and the Skeleton.
- Researching Basic Infusion adds the Infuser to the Workshop list; nothing locked appears before that.
- Upgrade nodes can be researched repeatedly and their per-level effect scales.
- Offline time completes research correctly and survives save and reload.

## Out of scope
Crypt Magic and Spider Sorcery in v0.1, unit-level research (until decided), balancing the work numbers.
