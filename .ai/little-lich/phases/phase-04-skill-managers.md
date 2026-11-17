# Phase 4: Skill managers

## Goal
All ten skills exist as classes with levels and XP, and the gathering and crafting skills can be automated with necromancers.

## Depends on
Phases 1 to 3.

## Skills
Health, Necromancy, Magic, Mining, Fishing, Hunting, Herbalism, Smithing, Alchemy, Enchanting. Level 1 to 999, each 10% more XP per level, no easing.

## Class layout
```csharp
public abstract class SkillBase : ITickable, ISaveable {
    public string Id; public int Level; public Num Xp;
    public abstract void Tick(long ticks);
    protected void AddXp(Num amount);          // handles level ups
}
public abstract class GatheringSkill : SkillBase {
    protected Dictionary<string, long> assigned;   // activityId -> necromancers
    public bool AddNecromancer(string activityId, long count);
    public void RemoveNecromancer(string activityId, long count);
}
public abstract class CraftingSkill : SkillBase { /* recipe list, queue-less, automated per recipe */ }

public class MiningManager : GatheringSkill { }
// FishingManager, HuntingManager, HerbalismManager : GatheringSkill
// SmithingManager, AlchemyManager, EnchantingManager : CraftingSkill
// HealthSkill, NecromancySkill, MagicSkill : SkillBase (level only, no automation yet)
```

## Automation flow (press + on Iron Ore)
1. UI: `MiningManager.AddNecromancer("iron_ore", 1)`.
2. MiningManager asks `LichManager.TryAssign("mining", "iron_ore", count)`. It gets back how many were actually assigned (limited by free).
3. MiningManager stores the count on its activity.
4. Each tick, the TickManager asks the LichManager for assignments and calls `MiningManager.Tick(ticks)`.
5. MiningManager works out `count x rate x modifiers x ticks`, adds ore through `CryptManager.Storage.Add(...)` within the cap, then adds XP.
6. Minus reverses via `LichManager.Unassign`.

## Gathering
- Activity definitions from JSON: id, name, required level, rate per tick, XP per unit, output item id.
- Locked activities show the required level.
- If storage is full, production stops for that item (necromancers stay assigned), and the UI shows it capped.

## Crafting
- Recipe definitions from JSON: id, required level, inputs, output, ticks or rate, XP.
- Recipe runs while necromancers are assigned and inputs are in storage.
- Missing inputs pause that recipe and show why.
- Smithing = weapons and armour, Alchemy = potions and bombs, Enchanting = runes that attach to weapons and armour.
- Open question: Smithing/Enchanting vs the Transmuter overlap. For v0.1 keep Transmuter limited to resource conversion.

## v0.1 content
Mining only has real content: copper/iron/coal style ores (placeholders). Other gathering skills get one activity each so the screens work. Crafting: a few recipes each.

## Tasks
- [ ] `SkillBase` with XP curve (10% per level) and level-up event.
- [ ] `GatheringSkill` with Add/RemoveNecromancer.
- [ ] `CraftingSkill` with recipes.
- [ ] Seven automatable skills and three level-only skills.
- [ ] Activity/recipe loading from JSON through the content database.
- [ ] Batched `Tick(long ticks)` maths.
- [ ] Events for UI: XP changed, level up, activity assigned, capped.
- [ ] Save/load of level, XP and per-activity counts.

## Done when
- Pressing + on Iron Ore in a test scene moves necromancers from free to assigned and iron ore rises each tick.
- Level up raises the unlocked activity list.
- 100 live ticks equals one batched `Tick(100)`.

## Out of scope
Skill-specific upgrades, mastery (none in v0.1), skill UI polish.
