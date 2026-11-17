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
- [x] `SkillBase` with XP curve (10% per level) and level-up event.
- [x] `GatheringSkill` with Add/RemoveNecromancer.
- [x] `CraftingSkill` with recipes.
- [x] Seven automatable skills and three level-only skills.
- [x] Activity/recipe loading from JSON through the content database.
- [x] Batched `Tick(long ticks)` maths.
- [x] Events for UI: XP changed, level up, activity assigned, capped.
- [x] Save/load of level, XP and per-activity counts.

## Done when
- Pressing + on Iron Ore in a test scene moves necromancers from free to assigned and iron ore rises each tick.
- Level up raises the unlocked activity list.
- 100 live ticks equals one batched `Tick(100)`.

## Out of scope
Skill-specific upgrades, mastery (none in v0.1), skill UI polish.

## Status: complete (2026-11-17)

### Decisions
- **Pulled forward (user decision):** a bare **Storage** (phase 5) and a bare stack-only **Inventory** (phase 6), so skills have somewhere to put what they make.
  - `Scripts/Crypt/Storage.cs`: resources only. Stock, cap = the resource's `base_cap`, and the net rate per tick of the last tick batch.
  - `Scripts/Crypt/Inventory.cs`: items only. Counts with no space limit, no equipping.
  - `Stockpile` routes an id to whichever one holds it.
  - A minimal `CryptManager` autoload (between LichManager and TickManager) owns them. Save sections are `storage` and `inventory`.
  - Placeholder `item` content with a `kind` (weapon, armour, potion, bomb, rune, material). Item ids may not clash with resource ids, because recipes name stock by id alone.
- **Content types:**
  - `gathering_activity` (`ActivityDefinitionBase` family): skill, required_level, rate (units per tick per necromancer), xp per unit, output.
  - `recipe` (`RecipeDefinitionBase` family): skill, required_level, inputs `[{id,count}]`, output `{id,count}`, ticks per craft per necromancer, xp per craft.
  - Both validate skill ids against `SkillIds` and check that inputs and outputs exist. Sample content is in `Data/activities.json`, `recipes.json`, `items.json`, and new resources in `resources.json`, all `"placeholder": true`.
- **XP curve:** XP from level L to L+1 = 10 x 1.1^(L-1). The base of 10 is a placeholder; level 999 needs about 1.8e43 total XP. Level comes from a precomputed threshold table with binary search. The level is derived from XP on load; the saved level is only for reading.
- **Classes:** `SkillBase` (ITickable, ISaveable, save key `skill_<id>`, events `XpChanged`, `LeveledUp`), `LevelOnlySkill`, `GatheringSkill`, `CraftingSkill`, and one class per skill: `MiningManager`, `FishingManager`, `HuntingManager`, `HerbalismManager`, `SmithingManager`, `AlchemyManager`, `EnchantingManager`, `HealthSkill`, `NecromancySkill`, `MagicSkill`. `SkillFactory.CreateAll` creates them, and `SkillContext` hands them the lich, stockpile, content and active recap (plain C#, unit tested).
- **Necromancer counts live only in the LichManager pool** (owner = skill id). Skills ask the pool instead of keeping a copy (the spec's step 3), so the two cannot drift; the pool already saves them.
  - `AddNecromancer`/`RemoveNecromancer` return how many actually moved (`long`, not `bool`), like `TryAssign`.
  - Locked activities and recipes return 0. Ids that belong to another skill throw.
- **Boot:**
  - **Order:** after content loads, GameManager calls `CryptManager.Initialize(content)` and `LichManager.InitializeSkills(content, stockpile)`, then registers every saveable. The lich section loads first, so skills see the restored assignments.
  - **Tick registration:** gathering skills register in `TickGroup.Skills` at `TickOrder.Gathering`, crafting skills at `TickOrder.Crafting`. Level-only skills are not registered.
- **Exact batched maths:** production uses `Int128` fixed point (1 unit = 1,000,000 micro-units), so `Tick(n)` equals n x `Tick(1)` exactly, including carried fractions, the cap, and inputs running out.
  - **Gathering:** units = carry + assigned x rate x speed x ticks.
  - **Caps:** a full output stores what fits, then stops (`IsCapped`, necromancers stay assigned). XP is only given for units actually stored.
  - **Crafting:** crafts = (carry + assigned x ticks x speed) / recipe ticks, limited by inputs and output room. When limited, progress resets and the status shows why (`MissingInput` + the input id, or `OutputFull`). Recipes in one skill that compete for an input are served in recipe order within a batch.
- **Speed modifiers:** each gathering and crafting skill has a lich stat `<skill>_speed` (base 1) that multiplies output or speed, so buffs, gear and research can use the phase 2 modifier system.
- **Health skill:** +10 max health per level above 1 (placeholder), as a modifier with source `skill:health`. The new modifier is added before the old one is removed, so current health is never clamped by a temporary dip. Necromancy and Magic are level-only for now.
- **Offline:** skills add gained resources/items/XP and capped ids to `TickScheduler.ActiveRecap`.
- **Godot signals on LichManager:** `SkillXpChanged`, `SkillLeveledUp`, `SkillActivityAssigned`, `ActivityCappedChanged`, `RecipeStatusChanged`. `LichManager.AssignVia/UnassignVia` route through the owning skill (so locks apply), falling back to the pool for non-skill owners.
- **Open question not settled:** Smithing/Enchanting vs the Transmuter. As the spec says, the Transmuter stays resource conversion only in v0.1 (phase 5).

### Verified
- `dotnet test tests/LittleLich.Tests`: 119 tests (25 new). The test project now also compiles `Scripts/Skills` and `Scripts/Crypt`. Covered:
  - **Done when:** pressing + on Iron Ore is refused while locked; at level 10 it moves 10 from free to assigned and iron ore rises 3, 6, 9, 12, 15 over five ticks. Levelling up widens the unlocked list (copper, then coal at 5, then iron at 10). Live ticks equal one batched tick for 7, 100 and 3,500 ticks in a world with gathering, crafting, a cap being hit and inputs running out.
  - **Also:** XP curve boundaries; caps stopping and resuming; fractional carry; speed modifiers; missing input and output-full statuses; crafted items going to the inventory; locked and unknown ids; offline recaps; the Health bonus without a health dip; a save/reload round trip continuing identically; new game resets; Storage/Inventory rules; content validation.
- **Godot 4.4.1 at 1280x720, via `--dev-cmd`, on a new game:**
  - Gathering: 100 necromancers; copper assigned; iron refused while locked; `level mining 10`; iron assigned; 20 ticks. Ore rose with live rates.
  - Smithing: 4 on copper bars, net copper +1/tick.
  - Reload run: assignments, levels, stock and tick count all restored. Swords went to the inventory, and Iron Bar showed "PAUSED: missing Coal" with a Skill log entry.

### Follow-ups (not in this phase)
- Gathering/crafting by hand ("Gather by hand" / "Craft" buttons from the design doc) and automation rules (stock targets, switching when full).
- Balance: XP base, rates, recipe times and Health HP per level are placeholders.
- Magic level raising Max Insight (with the spell book), Necromancy effects (units, army size).
- Phase 5: Workshop buildings, Magical Storage cap upgrades (`Storage.Cap` currently returns `base_cap`), and whether Storage should also cover items.
- Phase 6: inventory space, equipping, item subclasses; replace the minimal Inventory tab.
