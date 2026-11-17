# Little Lich: Game Design Document

Nov 16, 2026 · @Matt

## Overview

Little Lich is an idle RPG where you train a freshly raised lich, build an undead army in a workshop, and march it across a map to claim the soul of a king who cheated death.

**Pitch.** A little lich is raised from the dead to do the will of Death himself. The king of this land has cheated death somehow. The lich that very king killed over 300 years ago must now raise an army of the undead and claim the king's soul.

**Genre.** Incremental, idle, RPG, automation. The skill-and-mastery structure follows Melvor Idle; the theme and the army layer are our own.

**Design pillars**

- **Automation is earned.** Everything starts by hand. Each step of automation is a reward for progress, and the player chooses what to automate first.
- **Death feeds growth.** Every fight returns corpses and parts, so losing units is part of the economy and never a dead end.
- **Choices that matter.** Where the army marches, which research branch opens next, and which units to build are decisions that automation cannot make for you.
- **Small lich, big ambition.** The player starts weak and a little comic, and ends as the most feared thing in the kingdom.

**Core loop**

1. The lich and its necromancers train skills, which yield materials.
2. The Workshop turns materials and mana into items, souls and units.
3. The Siege Host advances on the World Map and fights automatically.
4. Victories return remains, tomes and strongholds.
5. Tomes and territory open research, and research opens better skills, units and automation.
6. Back to step 1, with more of it running without you.

## Lich Skills

Lich Skills is where the player trains the lich by hand. There are ten skills, each levelling from 1 to 999, and summoned necromancers gather for the gathering skills in the background.

**Level curve.** Each level needs 10% more XP than the last, so the XP for level n is 100 × 1.1^(n−1).

| Level | XP needed for that level |
| --- | --- |
| 1 | 100 |
| 2 | 110 |
| 3 | 121 |
| 10 | about 236 |
| 50 | about 10,700 |
| 100 | about 1.25 million |
| 250 | about 2 trillion (2 × 10^12) |
| 500 | about 4.5 × 10^22 |
| 999 | about 2 × 10^43 |

A flat 10% per level is a steep curve. Past level 100 the XP per action must grow almost as fast as the requirement, or the late levels become unreachable. The usual fixes are mastery multipliers, research bonuses and necromancers, which all appear below. Another option is to ease the curve after a set level, which is an open question at the end.

**The ten skills**

| Skill | Type | What the lich does | Feeds into |
| --- | --- | --- | --- |
| Mining | Gathering | Digs ore, stone and gems | Smithing, Enchanting, Transmuter |
| Fishing | Gathering | Catches fish and drowned relics | Alchemy, Undead Market |
| Hunting | Gathering | Hunts beasts for hides, bone and meat | Smithing, Beast Magic, Summoning Circle |
| Herbalism | Gathering | Picks herbs and grave plants | Alchemy |
| Smithing | Crafting | Turns ore and bone into weapons and armour | Unit equipment, Undead Market |
| Alchemy | Crafting | Brews potions and preservatives | Infusion, unit buffs |
| Enchanting | Crafting | Binds magic into gear | Unit and lich equipment |
| Necromancy | Core | Raises and strengthens the undead | Unit types, army size, Summoning Circle |
| Magic | Core | Casts spells and grows the mana pool | Mana Well, spells, Crypt, Holy and Beast Magic |
| Health | Core | Toughens the lich | Lich HP in boss and dungeon encounters |

**What each skill unlocks.** Every skill unlocks new activities as it levels, such as better ore, rarer herbs and stronger recipes. Necromancy and Magic also gate the research trees that share their names, so they matter most.

**Summoned necromancers.** The Summoning Circle can raise necromancers as well as combat units. Necromancers are the main way gathering is automated. Each one is assigned to a gathering activity, such as a Mining seam, a hunting ground, a fishing spot or an herb patch, and gathers it on its own while the lich does something else. Crafting skills work the same way: a necromancer assigned to a recipe crafts it again and again on its own (see Skill pages under UI). More necromancers, and better ones, are the main way to level many skills at once. They can also be assigned to Workshop buildings instead (see Automation).

## Research

Every tome the player finds is its own research tree, so exploring the world map is what grows the player's knowledge. The player always starts with the Necromancy 1 tome.

**How it works.** Each tome is a tree of nodes. A node costs materials and time, and unlocks one thing: a unit, a recipe, a building upgrade, a skill activity, or an automation option. Research runs on research benches and keeps going while the player is away.

**Research benches.** The player starts with one bench, which researches one node at a time. Extra bench space can be unlocked, so several nodes, or several trees, research in parallel and research speeds up.

**The seven trees.** What each unlocks and where the tome is found are proposals.

| Tome | Source | Main unlocks |
| --- | --- | --- |
| Necromancy 1 | Start | Skeletons, Zombies, first workshop upgrades, basic automation queues |
| Infusion | The Villages (Ring 2) | Infused mana, Soul Well upgrades, Enchanting recipes |
| Beast Magic | The Villages (Ring 2) | Undead beasts, Hunting bonuses, fast scouting units |
| Necromancy 2 | The Market Towns (Ring 3) | Ghouls, Wraiths, bigger armies, minion workers |
| Crypt Magic | The Walled Cities (Ring 4) | Curses, rot and plague spells, siege damage against walls and gates |
| Holy Magic | The Abbey and Citadel (Ring 5) | Resistance to holy damage, ward-breaking, desecration of consecrated ground |
| Necromancy 3 | The Royal Capital (Ring 6) | Bone Golems, Wights, siege units, fully automated army orders |

The Rings are the stages of the World Map section below.

**Holy Magic is stolen, not learned.** A lich cannot cast holy spells, but studying the kingdom's own tomes lets the player counter them, which is why the tome sits deep inside enemy territory.

**Research and the rest of the game.** Necromancy and Magic skill levels gate how deep each tree can go, so levelling skills and finding tomes both matter. Spending materials on research competes with spending them on the army, which keeps the player choosing.

## Workshop

The Workshop is the automated part of the game: where the player summons, crafts and stocks everything the army needs. Once a building is built and set, it keeps running on its own, including while the player is away.

**The seven buildings**

| Building | What it does | Input and output | Upgrades |
| --- | --- | --- | --- |
| Mana Well | Generates mana | Nothing in, mana out | Faster generation |
| Summoning Circle | Summons units and necromancers | Mana, materials and items in, units out | More circles, new recipes from tomes |
| Infusion | Converts mana into infused mana | Mana in, infused mana out | Faster conversion, better yield |
| Soul Well | Converts infused mana into souls | Infused mana in, souls out | Faster conversion, better yield |
| Transmuter | Converts materials into items | Materials in, items out | New recipes, faster crafting |
| Undead Market | Sells items | Items in, coin out | Better prices |
| Magical Storage | Raises max resources and max inventory space | None | Higher storage tiers |

**Each building has its own page and mechanic.** Opening the Workshop shows a list of buildings on the left. Selecting one opens that building's page, built around the way it works, rather than a shared card. These mechanics are proposals.

| Building | Its mechanic |
| --- | --- |
| Mana Well | Level and necromancer slots. Each slot adds its own output, and the well level opens more slots. Surplus mana can overflow into the Infuser or a Summoning Circle. |
| Summoning Circle | Several circles, each running one recipe on repeat with a target stock. Recipes are picked from a list of units and necromancers. |
| Infuser | Converts mana into infused mana. The player chooses which element to infuse and splits the output between elements. |
| Soul Well | Turns infused mana into souls of different grades. Higher grades take longer and cost more. |
| Transmuter | A recipe list of materials into gear and ingredients, with ingredient quality carried through. |
| Undead Market | A sell list with price per item, a reserve the player keeps, and a rule to sell any surplus. |
| Storage | Per-resource caps and tier upgrades. |

**What the player controls.** The Workshop does not need clicking, but it still needs decisions. For each building the player chooses what it makes and sets stock targets, such as "keep building Skeletons until there are 200". The player also decides which building to upgrade next and what to spend coin on.

**Where materials come from.** Lich Skills gather and craft the raw materials. Those flow into the Workshop, which turns them into items and units. Higher-tier units cost souls, which makes the Soul Well the gate to late-game armies.

&#91;embedded content: Workshop resource flow · 7 buildings, 2 chains\]

Mana flows left to right into souls, items flow from materials to the market, and both feed the Summoning Circle. Mana also pays for every summon.

**Why storage matters.** Every resource has a cap, so a Workshop that runs for hours will fill up. Magical Storage raises those caps, which also sets how long the player can be away before production stalls.

## Unit Builder

The Unit Builder is where the player turns base units into specialised troops: choose a unit, give it a weapon and armour built in the Workshop, then place it in the formation as a stack.

**Base units.** Each unit has a role, a damage type, an attack speed and health. Which units are available depends on the research tree that unlocks them.

| Unit | Role | Strength | Weakness | Unlocked by |
| --- | --- | --- | --- | --- |
| Skeleton | Swarm | Cheap, quick to summon, huge numbers | Crossbows and holy damage | Necromancy 1 |
| Zombie | Tank | Slow and durable, regrows between fights | Slow attacks | Necromancy 1 |
| Wisp | Support | Flies, mends and shields allies | Very fragile | Infusion |
| Ghoul | Flanker | Fast, heals by feeding on the fallen | Low armour | Necromancy 2 |
| Wraith | Caster | Spirit damage ignores armour | Fragile, costs souls | Necromancy 2 |
| Golem | Siege breaker | Huge blunt damage to walls and gates | Slow, expensive | Necromancy 3 |

**Upgrading with gear.** Every unit type has a loadout with a weapon slot and an armour slot. Weapons and armour are crafted in the Workshop: materials from Mining, Hunting and Smithing go through the Transmuter, and Enchanting adds a trait to the finished piece. Gear tier depends on the materials used and the skill level.

**Enchantment traits.** Examples are Rot (damage over time), Fleet (faster attacks) and Ward (resists holy damage). Traits are what let a basic unit stay useful late in the game.

**How gear reaches the army.** The player picks one loadout per unit type. Each unit the Summoning Circle raises takes one weapon and one armour from storage, so gear production has to keep pace with summoning. Units already in the field can be re-equipped for a cost. This is a proposal, and it is listed in the open questions.

**Into the formation.** Finished units join a stack. The player assigns stacks to formation slots (see Combat), and the Summoning Circle keeps each stack topped up to the target count the player sets.

## Spell Book

The Spell Book is where the lich casts spells to help the battle raging in the background. Max Focus limits how many spells the player can have ready at once, and every spell has its own mana cost and cooldown.

**Focus.** Each equipped spell takes some Focus, with bigger spells taking more. The lich can only equip spells whose Focus costs fit within Max Focus. Max Focus grows with Magic skill levels, research and the lich's equipment (staff and grimoire). This is a proposal, and the exact rule is in the open questions.

**Mana and cooldown.** Every cast spends mana and then waits out its cooldown. Spells draw from the same mana pool the Summoning Circle uses, so the player trades casting against summoning troops. This tension is deliberate: a spell-heavy lich spends less on the army.

**Example spells.** Each spell comes from the research tree named in the last column.

| Spell | What it does | Best used when | Source |
| --- | --- | --- | --- |
| Raise Dead | Raises remains on the field into the nearest stacks | Many remains are lying about | Necromancy 1 |
| Bone Spear | One heavy hit on a single enemy stack | Killing priests or knights | Necromancy 1 |
| Bone Ward | Shields a row against damage | Facing crossbows | Necromancy 2 |
| Mend Flesh | Heals the most wounded stack | A stack drops low | Infusion |
| Soul Drain | Steals health and returns mana | Mana is running short | Infusion |
| Pack Howl | Speeds up the whole formation | Opening a big assault | Beast Magic |
| Rot Gate | Weakens walls and gate | The wall or gate layer | Crypt Magic |
| Plague Cloud | Damage over time on a whole row | Big garrisons | Crypt Magic |
| Unhallow | Breaks wards and consecrated ground | Priests and holy wards | Holy Magic |

**A book that keeps growing.** Research nodes add modifiers to each spell, such as wider area, lower cost, shorter cooldown or longer duration. Combined with the base spells, this gives hundreds of variants and the room to grow toward thousands.

**Casting.** Spells can be cast by hand during a siege, or on rules once the player researches that automation (see Automation).

## World Map

The World Map is a siege front that advances by itself. A standing army pushes toward the capital one stronghold at a time, and the kingdom visibly reacts as it gets closer. The player shapes the result through what they build, research and prioritise, and never has to command a battle. Everything here is a proposal for you to react to.

**The Siege Host.** Units from the Summoning Circle join one standing army, the Siege Host, up to a cap that research and Necromancy raise. Reinforcements flow to the front automatically as the Workshop produces them.

**The Rings.** The kingdom is six rings around the capital. Each ring has several strongholds, and the player can take them in any order, so the map branches and has real choices. A ring's final stronghold, its Ring Keep, is guarded by a champion and holds that ring's tome. Champions are fought in the encounter format described under Bosses and Dungeons.

| Ring | Name | Strongholds | Kingdom's alarm | Reward |
| --- | --- | --- | --- | --- |
| 1 | The Borderlands | Farms, hamlets | Unaware: peasants and a few militia | Second research bench, first storage |
| 2 | The Villages | Villages, hunting lodges, shrines | Alerted: militia drilled, hunters out | Infusion and Beast Magic tomes |
| 3 | The Market Towns | Walled towns, trade roads | Mustering: town guard, archers, mercenaries | Necromancy 2 tome |
| 4 | The Walled Cities | Fortress cities, royal catacombs | War footing: knights, siege defences | Crypt Magic tome |
| 5 | The Abbey and Citadel | Monasteries, holy fortresses | Holy crusade: priests, paladins, consecrated ground | Holy Magic tome |
| 6 | The Royal Capital | The castle and the king's keep | Last stand: royal guard, champions, the king | Necromancy 3 tome, the king's soul |

**How a siege plays out.** Each stronghold is a stack of layers the Host works through in order. Every layer has its own health bar, so the player can watch the siege advance.

| Layer | What it is | Weak to | Hard to hurt with |
| --- | --- | --- | --- |
| Outer defenders | Soldiers in front of the walls | Large numbers | Weak units alone |
| Walls | Stone or timber walls | Blunt damage, rot and curses | Piercing damage |
| Gate | The weak point of the wall | Blunt damage, curses | Piercing damage |
| Garrison | Defenders inside | A mixed army | Whatever counters your main unit |
| Wards (late rings) | Holy protection | Holy Magic and ward-breaking | All undead damage until researched |

**Combat.** Combat is automatic and runs in ticks, like Melvor. Each tick the Host damages the current layer, using each unit's damage type against that layer's weaknesses, while the defenders hit back and kill units. Fallen units, and defenders, leave remains. A share of those remains is recovered into storage, ready for the Transmuter and Summoning Circle, so losses feed the economy.

**Failing forward.** If the Host's strength falls below its retreat threshold, it pulls back and waits for reinforcements. Damage done to the layers fades slowly instead of vanishing, so a failed attempt still counts as progress.

**Making the threat visible.** The player should feel the kingdom grow afraid without doing anything. Two things drive this: the alarm stage of the current ring, and Dread, a meter that rises with every stronghold taken and with the size of the Host.

- **The land changes.** Captured ground darkens and gains undead banners. Empty villages, refugees on the roads and lit beacon fires appear on the map behind and ahead of the front.
- **The enemy changes.** Each ring's alarm stage swaps the defenders for tougher, more specialised ones, so the player's army has to keep adapting.
- **Dread weakens the frightened.** In rings the Host has already broken, defenders lose morale and are easier to beat, so the early game gets quicker as the player grows.
- **Dread draws a response.** A high Dread level makes the kingdom strike back.

**The kingdom fights back.** At intervals the king sends a relief army to retake the most recently captured stronghold, with a visible countdown. It fights the garrison automatically.

- If the garrison wins, the player recovers extra remains.
- If it loses, the stronghold is lost and the Host must retake it, but its siege damage and rewards already earned stay.
- Nothing the player owns is wiped, so a raid is exciting without being punishing, even after a long time away.

**What the player decides.** The siege runs itself, but these choices still shape it, and none needs constant attention:

- **Aggression.** How ready the Host must be before it attacks (cautious, steady or reckless).
- **Target order.** Which stronghold in a ring to attack next, or let the Host pick the nearest.
- **Army mix.** The ratio of unit types the Summoning Circle builds, such as mostly Skeletons with a few Zombies.
- **Counter-research.** Crypt Magic for walls and gates, Holy Magic for wards, Beast Magic for scouts.
- **Scouting.** Scouts reveal each stronghold's layers and weaknesses. Without them the player only sees a rough strength range.

**Offline.** The Host keeps sieging while the player is away, up to an offline cap that research raises.

**The final siege.** The king cheated death, so the Royal Capital is a different kind of fight. The king's wards must be broken with Holy Magic before his soul can be reached, and the champions and royal guard defend the keep. Claiming the soul ends the main story.

## Combat

Combat is a long campaign along many paths to the king, fought by an eight-slot formation in which every unit has a job. The player fights through each path, improving the army as time goes on. Bosses and dungeons use a smaller single-summon format, described in the next section.

**Many paths lead to the king.** Each ring of the World Map has several routes, and each route is a chain of strongholds. Routes meet at the ring's keep and finally at the capital. A route has its own mix of defenders, walls and rewards, so the player picks routes that suit the army, such as a river road full of archers or a pilgrim's way full of priests. Later research allows more than one Host, so several routes can be pushed at once.

**The formation: 2 front, 3 mid, 3 back.** A siege is fought by eight stacks. This replaces the earlier 3×3 idea, and the battle mockup still shows nine slots until it is updated.

| Row | Slots | Job | Typical units |
| --- | --- | --- | --- |
| Front | 2 | Soak hits and break walls and gates | Zombie, Golem |
| Mid | 3 | Deal the damage and dive the back row | Skeleton, Ghoul |
| Back | 3 | Support, heal and cast | Wisp, Wraith |

**Targeting.** Enemies hit the front row first. Ranged defenders, Wisps and Wraiths can reach the back row. Area spells hit a whole row.

**Strategies.** Each strategy is a formation plus a spell choice, and each has an obvious counter.

| Strategy | Front / Mid / Back | Spells | Good against | Weak to |
| --- | --- | --- | --- | --- |
| Wall breaker | Golem, Zombie / Skeleton ×2, Ghoul / Wisp ×2, Wraith | Rot Gate, Mend Flesh | Walls, gates, towers | Knights and fast raiders |
| Skeleton tide | Zombie ×2 / Skeleton ×3 / Wisp ×2, Wraith | Raise Dead, Pack Howl | Militia, spearmen, outer defenders | Crossbows and priests |
| Ghost line | Zombie, Golem / Ghoul ×2, Skeleton / Wraith ×3 | Bone Spear, Soul Drain | Armoured knights and guards | Priests |
| Anti-holy | Zombie ×2 / Ghoul ×3 / Wisp ×2, Wraith | Unhallow, Bone Ward | Priests, paladins, wards | Mass crossbows |
| Plague grind | Zombie ×2 / Skeleton ×2, Ghoul / Wisp ×3 | Plague Cloud, Mend Flesh | Big garrisons, long sieges | Fast breakouts |

**Plan by ring.** Scouting reveals each stronghold's defenders, so the player can match a strategy to the target.

| Ring | Main defenders | Suggested plan |
| --- | --- | --- |
| 1. Borderlands | Peasants, a few militia | Skeleton tide, and farm remains |
| 2. Villages | Militia, hunters | Skeleton tide with Ghouls to catch hunters |
| 3. Market Towns | Town guard, archers, mercenaries | Bone Ward, Ghoul flankers, Zombies to soak; Wall breaker for walled towns |
| 4. Walled Cities | Knights, siege defences | Wall breaker or Plague grind; Ghost line against knights |
| 5. Abbey and Citadel | Priests, paladins | Anti-holy, with Unhallow |
| 6. Royal Capital | Royal guard, champions, the king | Ghost line plus Anti-holy, and ward-breaking for the king |

**Learning from a loss.** After each siege a short report names the stack that did the most damage, what killed the most units and what was lost. The player changes one thing at a time and sends the Host in again.

## Bosses and Dungeons

Bosses and dungeons use a Melvor-style encounter: the lich summons single units one at a time and slings spells at a boss on the far right. Big map battles still use the army formation described under Combat.

```
Lich (HP)    [Unit 1]  [Unit 2]  [Unit 3]    ........    Boss
```

**Where it applies.** Route wardens, Ring Keep champions and the King are fought this way, and so are dungeons when they are added later. Ordinary strongholds on the World Map keep the army format.

**Summoning.** Each unit has a recipe of items from the inventory, and a summon time shown as a cast bar in its slot. The examples below are illustrations.

| Unit | Example ingredients | Role |
| --- | --- | --- |
| Skeleton Warrior | Bones, a sword | Reliable melee damage |
| Zombie Brute | Meat, armour | Tank that draws attacks |
| Wisp | Infused mana | Mends and shields the lich |
| Ghoul | Meat, a dagger | Fast attacker that heals itself |
| Golem | Ore bars, stone | Slow, heavy hitter |

**Items and losses.** A unit holds its ingredients while it fights. When the encounter ends, a surviving unit returns its items to the inventory. A unit that dies loses its items for good, so good play pays and a strong sword is a real risk.

**Ingredient quality.** Better ingredients make a stronger unit. An iron sword gives a better Skeleton Warrior than a bronze one, so Mining, Smithing and Enchanting feed straight into boss fights.

**Slots.** There are three summon slots in front of the lich. A slot fills when its unit finishes summoning and empties when the unit dies. An auto-summon setting refills empty slots from a priority list while the ingredients last.

**The lich.** The lich has an HP bar, and enemies can target it directly. Units can draw attacks as bodyguards. If the lich falls, the encounter fails and the lich reforms at its phylactery, but the items held by units that died are gone. The lich's HP comes from the Health skill and its gear.

**Mana and spells.** Mana is not a separate bar. It is the stock held in the inventory from the Mana Well, and spells spend from it. The Spell Book works as described earlier: Max Focus limits how many spells are equipped, and each spell has a cost and a cooldown.

**How bosses work**

1. **Milestones, not walls.** Beating a boss unlocks something real, such as a tome, a research tree or a new route.
2. **One signature mechanic.** Each boss has one mechanic with a known answer, shown by a cast bar before it lands. Three phases begin at health thresholds, each with a banner.
3. **Mechanics hit the slots or the lich.** A boss might kill the front unit, silence the lich or target the lich, and units and spells are the answer.
4. **Fail forward.** Damage on the boss fades slowly after a failed attempt, so there is no hard reset.
5. **Prepare, then commit.** Scouting shows the mechanic and a recommended plan before the fight. A toggle lets advanced players auto-attack bosses.
6. **Echoes.** After the first kill, a weaker echo of the boss returns on a cooldown for farming.
7. **Three beats.** Build-up (scouting and preparation), a moment (a phase change), and a payoff (an unlock and a recap).

**Example bosses.** These are illustrations for you to react to.

| Ring | Boss | Mechanic | Answer |
| --- | --- | --- | --- |
| 1 | Reeve Captain | A horn calls militia every minute | Kill the horn-bearer first with Bone Spear |
| 2 | Huntmaster | Traps stun the front unit | Spring them with a cheap unit first |
| 3 | Warden of the Market | Crossbow volley on a timer | Bone Ward before each volley |
| 4 | Lord Marshal | A charge hits the front unit hard | A Zombie Brute to take it, then Plague Cloud |
| 5 | High Abbot | A holy ward blocks spells | Unhallow to break the ward |
| 6 | The King | A soul tether brings him back | Break the ward, then the duel phase |

**The King's duel.** In one phase of the final fight, the units are pinned down and the lich faces the King alone, with its equipped spells running on their rules. The King killed the lich 300 years ago, so this fight is personal.

**Dungeons (later).** A dungeon is a chain of encounters in a fixed order, with a boss at the end. What the player brings in, including inventory, equipped spells and slot priorities, stays fixed once inside, as in Melvor, and the reward comes on completion.

## Automation

Automation keeps the army alive, supplied and fighting while the player is away, because a single siege can take hours or days. Each layer of automation is earned through research, so the player always has something new to unlock.

**Four loops keep the Host going.**

1. **Reinforce.** The Summoning Circle tops each stack up to its target count. Necromancers working in the Workshop make this faster, so the army stays fresh.
2. **Resupply.** Smithing, the Transmuter and recovered remains keep gear and materials flowing to the Summoning Circle.
3. **Cast.** The lich casts spells on the player's rules, within Max Focus, to help the battle.
4. **Command.** The Host retreats, regroups, picks its next target and returns to the fight without being told.

**Necromancers at work.** A summoned necromancer can be assigned to a skill or to a Workshop building. Assigned to the Summoning Circle, Transmuter or another building, they raise its speed and output, and they are the main way the Workshop scales up.

**Automation tiers**

| Tier | Unlocked by | What it does |
| --- | --- | --- |
| 1. Manual | Start | The player sets the formation and casts spells by hand |
| 2. Top-up | Early research | Stacks refill to their target counts automatically |
| 3. Spell rules | Mid research | If/then spell triggers, within Max Focus |
| 4. Necromancer crews | Mid research | Necromancers staff Workshop buildings and speed them up |
| 5. Autopilot | Late research | The Host picks targets, changes presets after a defeat, and farms cleared strongholds |

**Spell rule examples.** "If the gate is above 50%, cast Rot Gate." "If any stack falls below 30% health, cast Mend Flesh." "If there are more than 100 remains on the field, cast Raise Dead."

**Encounter automation.** In bosses and dungeons, auto-summon refills empty slots from a priority list while the ingredients last, spell rules cast within Max Focus, and an encounter can repeat on its own until the inventory runs low.

**Pace.** Early sieges should take minutes so the player learns the loop quickly. Later sieges take hours, and the final assault on the capital can take days. These are design targets, not final numbers.

**Staying safe.** The Host retreats below a strength threshold the player sets, so a bad fight does not wipe the army. Nothing the player owns is lost to a defeat.

**Coming back.** After time away, a recap shows strongholds taken, raids repelled, remains collected and tomes found. Notifications flag the moments that matter, such as a relief army on the way, a ring cleared or a new tome.

## UI

Little Lich's UI follows the layout of the Melvor Idle 2 screenshot: a skill sidebar on the left, a category list beside the content, a top bar for global pages, and a bottom bar showing live activity. We borrow the layout and its conventions, and Little Lich gets its own art, icons and colours.

**What the screenshot shows**

- **Top bar.** Global pages sit as icon buttons (storage with its count, the shop with the coin total, quests, completion, unlocks, mastery, statistics and a guide). The current page title is in the centre and the account menu on the right.
- **Left sidebar.** Every skill is listed with its level and a thin XP bar, in groups that can collapse.
- **Category list.** A column of large buttons picks a category, with the selected one highlighted.
- **Filters row.** Jump-to shortcuts, "Hold to Buy", "Show purchased" and "Show locked" toggles, and a buy quantity of 1, 10, 100 or Max.
- **Card grid.** Each item is a card with its limit, name, what it provides, its costs and requirements, and a Hold to Buy button. Requirements the player meets show in green, and cost chips show how much is owned over how much is needed.
- **Bottom bar.** The current activity, level progress with time left, XP per hour, income per hour and an away timer.
- **Toasts.** Small pop-ups report gains, such as the amount gained and how many are in storage.

&#91;embedded content: screen layout: top bar, Crypt tabs, skills sidebar, page and resource bar\]

The Crypt tabs pick a section and the skills sidebar stays put, so the whole game is one screen with no loading. The highlighted siege strip in the top bar keeps the background battle visible from every page.

**Left sidebar: skills only.** Every skill is a button, listed with its level shown as current/999 and a thin XP bar, in three groups: Lich (Health, Necromancy, Magic), Gathering (Mining, Fishing, Hunting, Herbalism) and Crafting (Smithing, Alchemy, Enchanting). The selected skill is highlighted. Gathering and Crafting skills carry a marker to show they open a skill page. The three Lich skills level passively and have no page. Clicking a skill swaps the content area. Nothing else lives in the sidebar.

**Where each feature lives**

| Feature | Where | Layout |
| --- | --- | --- |
| Lich skills | Left sidebar, one page per skill | Activity cards with level requirements and mastery |
| Workshop | Crypt tab | A building list on the left, the selected building's own page beside it |
| Research | Crypt tab | Tomes as the category list, nodes as cards, research benches along the top |
| Unit Builder | Crypt tab | Unit cards with recipes, ingredient tiers and loadouts |
| Spell Book | Crypt tab | A Focus bar on top, equipped slots, then spell cards |
| Lich | Crypt tab | Health, gear and the lich's stats |
| World Map, Settings | Top bar | The map of routes with a Host panel, and game settings |
| Bosses and Dungeons | Reached from the map | The encounter screen: lich, three slots, boss |

**One card for everything.** The screenshot's card is the template for research nodes, unit recipes, market items and Workshop upgrades. Every card has a title, a "Provides" section, "Costs and Requirements" with owned-over-needed chips, and one action button, with a hold-to-confirm action for purchases so nothing is bought by accident.

**The Crypt and the resource bar.** The Crypt is the row of tabs under the top bar (Workshop, Research, Unit Builder, Spell Book, Lich). The resource bar sits at the bottom of every page and shows each resource as stock over cap with its rate per tick: coin, mana, infused mana, souls and the raw materials. The top bar holds the tick bar on the left, then the siege strip with the Dread meter, so the background battle is visible from every page. The exact resource list is still to be decided.

**Show locked content.** The "Show locked" toggle is worth keeping. In an incremental game, seeing what is coming next is a major motivator.

**Away recap.** A pop-up on return lists what happened while the player was away: strongholds taken, raids repelled, items gathered and tomes found.

**Colour language.** Green means met or good, red means unmet or damage, and purple means mana and spells. Panels are dark stone and parchment, with bone-coloured text and a necrotic green accent.

### Workshop screen

The Workshop tab shows a list of buildings on the left, each with a small status dot (green when running, yellow when it needs input). Selecting a building opens its own page, built around how that building works. The mockups are in the Design canvas "Little Lich: Battle Screen UI", as the boards "Workshop: Mana Well" and "Workshop: Summoning Circle".

**Mana Well page.** A header with the well's level, necromancers assigned (for example 3 of 11) and mana per second, with a level-up button. Below it, a grid of channeling slots: filled slots show a necromancer and their output, empty slots wait for one, and later slots show the level that unlocks them. A second panel shows the mana stock against its cap, a choice of where overflow goes (Infuser, Summoning Circle or discard), and the well's level milestones.

**Summoning Circle page.** A panel of circles, each with its recipe, a progress bar, time left, a target stock and a state (running, idle or locked). A second panel lists recipes for units and necromancers, with owned-over-needed costs, and lets the player assign one to a circle. Recipes found in tomes appear locked until the tome is found.

**Other buildings.** The Infuser, Soul Well, Transmuter, Undead Market and Storage each get their own page in the same style, following the mechanics in the Workshop section. They are not drawn yet.

### Research screen

Research has three parts, as in the current build, with a much larger tree. The mockup is the board "Research" in the same canvas.

- **Tome list (left).** Every tome is listed. Found tomes show their progress (for example 9 of 24 researched). Tomes not yet found are dimmed and show which ring holds them. Holy Magic is marked as stolen.
- **Node tree (centre).** A large web of round nodes that grows outward from the first node, in the style of the Path of Exile 2 passive tree. Nodes come in three sizes: small, notable and keystone. Researched paths glow green, the node being researched glows purple, and locked paths are dotted and dim. Hovering a node shows its effect, cost and research time, and clicking starts it on a free bench. The tree can be panned and zoomed, so a tome can grow well past the screen.
- **Benches (right).** Each bench shows the node it is researching, a progress bar with time left, and the necromancers assigned to it, with minus and plus buttons. More necromancers make the bench faster. A locked bench shows what unlocks it. A bench level panel below raises research speed for coin and mana, with an upgrade button.

How deep a tome goes is limited by the player's Necromancy and Magic levels, as described in the Research section.

### Unit Builder screen

The Unit Builder tab shows a list of base units on the left and the selected unit's page beside it. The mockup is the board "Unit Builder" in the same canvas.

- **Unit list (left).** The six base units, with units still to be researched dimmed. A yellow dot marks a unit whose gear is running short.
- **Unit page (centre).** A header with the unit's role and a one-line description, then four stat boxes (health, damage, attack speed, armour), each showing the base value and the value with the current loadout. Below are the weapon and armour slots, each showing the equipped item's tier and enchantment trait. A tabbed list of crafted gear follows, with tier, bonus, trait, stock and an Equip button. Locked gear shows what is still to come.
- **Stack panel (right).** The stack's current size against its target count, the formation slot it fills, the Summoning Circle that raises it, and a stepper for the target.
- **Gear supply.** Meters for weapon and armour stock, with a warning when one will run out soon. Each summoned unit takes one weapon and one armour, so the Summoning Circle pauses when either runs out.
- **Re-equip.** A button that swaps the loadout on units already in the field, for a mana cost per unit.

This follows the proposal of one loadout per unit type. Per-stack loadouts are an open question.

### Spell Book screen

The Spell Book tab keeps the three-panel layout of the current build: spells on the left, an Insight meter in the middle and the channelled spells on the right. The mockup is the board "Spell Book" in the same canvas. The screen calls the limit Insight, as the current build does. Earlier in this doc it is called Max Focus, and the name still needs settling.

- **Spells (left).** A grid of spell tiles, each showing its Insight and mana cost. Learned spells are lit. Spells not yet found are dimmed and show the tome that holds them. A search box and filter chips (Attack, Support, Siege) keep the grid usable as the book grows toward thousands of spells.
- **Modifiers (below the grid).** The selected spell shows its base stats next to its modified stats, how many variants are unlocked, and a row of modifier chips such as wider area, lower cost and shorter cooldown. Each chip is on, available to buy or locked. Research nodes add the modifiers.
- **Insight (middle).** Free, Used and Max, with a note that Max grows with Magic level and gear.
- **Channelled (right).** Each equipped spell shows its mana cost, its effect, its Insight cost, a cooldown bar with a Ready or time-left state, and a button to remove it. Each also has an Auto or Manual tag and, for Auto, the rule it casts on, such as "when a stack is under 40%". Free Insight is shown at the bottom as a prompt to channel another spell.

What the plus and minus buttons on the current build's spell tiles do is still to be confirmed, and the mockup does not use them.

### Lich screen

The Lich tab keeps the three panels of the current build and adds the Health skill. The mockup is the board "Lich" in the same canvas.

- **Throne (left).** The lich sits on a throne surrounded by seven equipment slots: Hood, Amulet, Ring, Robe, Staff, Grimoire and Boots. Each slot shows the equipped item's rarity. Under the throne is the lich's health bar, with the Health skill level out of 999 and health regeneration per second.
- **Items (centre).** Tabs for Items and Loadouts, an Auto Equip button, filters for item type and rarity, and a grid of items with stack counts. Hovering an item shows its bonuses and what it replaces. A Sell bar at the bottom sells selected items for coin.
- **Stats and bonuses (right).** Max Health with a breakdown of what comes from the Health skill and what comes from gear, and Max Insight. Below is the list of equipment bonuses, colour-coded: health in red and green, mana and spell speed in blue, summon speed in gold.

Gear can add Max Health, health regeneration and Max Insight alongside the bonuses in the current build.

### World Map screen

The Map button in the top bar opens the World Map in the content area. The mockup is the board "World Map" in the same canvas.

- **The map.** The six rings are drawn as concentric circles around the Royal Capital. Each ring holds its strongholds as nodes, joined by routes that branch inward. Nodes are colour-coded as taken, besieged, available or locked. Larger nodes are Ring Keeps, which hold a tome and are guarded by a champion. Any available stronghold can be taken in any order.
- **Siege in progress.** A banner shows the active siege, its wall progress and the relief army countdown, with a Watch battle button and a Recall button.
- **Stronghold panel.** Selecting a stronghold shows its siege layers with a strength note for each, its rewards and its alarm state. A Host card below shows unit count and strength against the defences, with a Send the Host button and a link to edit formation and aggression.
- **Ring Keeps.** Selecting a Keep offers a champion fight in place of a siege, using the encounter format.

One siege runs at a time in the mockup. Whether the Host can hold several is an open question.

### Getting into battle

There is one battle surface, with three ways in. It opens over the content area, so the skills sidebar and resource bar stay visible and there is no loading.

1. **The siege strip in the top bar.** It is on every page, and clicking it opens the live siege.
2. **The World Map.** Watch battle on the siege banner opens it, and Send the Host opens it as the siege starts.
3. **Champion fights and, later, dungeons.** Challenge champion on the map opens the prep screen, then the fight.

### Boss prep screen

Challenging a champion opens a prep screen before the fight. The mockup is the board "Boss prep".

- **Boss card (left).** The boss's health, damage and speed, and a list of what the player has learned from earlier attempts, such as shield timing and weaknesses. Patterns not yet seen stay hidden, which makes a first loss useful. It also shows attempts and the best result.
- **Encounter loadout (centre).** Three summon slots in priority order. Each shows the unit, its gear, its cost per summon, its summon time, an auto-summon toggle and a Change button. Below are the channelled spells with their Insight use and Auto or Manual tags, and a row of saved loadouts per boss.
- **Supply check and rules (right).** Meters for the stock the loadout needs, with a warning about what will run out first and a reminder that units that die lose their gear. Rules cover re-summon on death, retry after a loss and swap rules such as changing a unit when the boss shields. Begin fight starts the encounter, and an auto-farm toggle repeats it.

### Boss encounter screen

The fight itself. The mockup is the board "Boss encounter".

- **Battlefield.** The lich on the left with its health bar, three summon positions in front of it, and the boss on the right with a large health bar. A telegraph shows the boss's next attack, its timing and its target. Floating numbers show damage and healing.
- **Slot panels.** Each shows its unit, a summon progress bar and a Summon now button.
- **Spells and log.** Spell buttons with cooldown bars, where manual spells are cast by clicking and auto spells cast on their rule. A battle log and a Retreat button sit alongside.
- **During the fight.** Everything runs automatically, but the player can summon by hand, cast a manual spell or swap a unit at any time.

### Ticks

The game runs in ticks, currently 2 seconds at startup. The left of the top bar shows a tick bar: a progress bar that fills over each tick, with the current tick length beside it. It replaces the game title. Resource rates in the resource bar are shown per tick. Tick length is meant to be something the player can speed up later, which is an open question.

### Necromancers in the top bar

Necromancers are the main automation resource, so the top bar always shows them as three abbreviated numbers: free, assigned and max (for example Free 2.4B, Assigned 97.6B, Max 100B), with a thin bar showing how much of the max is assigned. The max can reach 100 billion, so there are no per-necromancer icons anywhere. Free necromancers are idle and can be assigned anywhere. Assigned ones are working in the Workshop, on a gathering item or on a crafting recipe. The maximum comes from the Summoning Circle and research. Every page that assigns necromancers shows the same pool and an Assign by selector (1, 1k, 1M or all free), so a stepper adds or removes that many at a time.

### Skill pages

Gathering and Crafting skills each open a page from the sidebar. All the mockups are boards in the same canvas.

**Gathering skills.** Mining, Hunting, Fishing and Herbalism share one gathering view. The board "Skill page: Mining" is the example.

- **Header.** The skill's level, XP bar, XP per tick, the next unlock, and a progress bar for what the lich is gathering by hand.
- **Gathering items.** A grid of cards, one per item. Each shows its level requirement, what it gives, XP and time in ticks, stock in storage and mastery. A card has a Gather by hand button and a necromancer stepper that adds or removes the chosen step. Each necromancer assigned to an item gathers it on its own, and the card shows the rate. Locked items show what unlocks them.
- **Necromancer pool.** A panel showing how many necromancers are free out of the total, since some are working in the Workshop or on other skills.
- **Automation.** Rules such as auto-assigning idle necromancers to an item, switching to another item when storage is full, stopping at a stock target and carrying on while the player is away.
- **Tool upgrades.** A tool per skill (a pickaxe for Mining) with its next tier, effect and cost, and an auto-upgrade rule that keeps a reserve of coin so it cannot drain the player.

The items for each gathering skill are proposals. Mining gives stone, iron ore, silver ore, grave iron and soul crystal.

**Crafting skills.** Smithing, Alchemy and Enchanting each show a list of items the player can craft. The boards "Skill page: Smithing", "Skill page: Alchemy" and "Skill page: Enchanting" show them.

- **Smithing** crafts weapons and armour, with tabs for each. Gear feeds the Unit Builder and the Lich page.
- **Alchemy** crafts potions and bombs, used in boss fights and sieges.
- **Enchanting** crafts runes, and a panel lets the player attach a rune to a weapon or piece of armour. A new rune replaces the old one.

Each recipe row shows its ingredients as owned-over-needed chips, its time and XP, how many are in storage, the number of necromancers assigned with a stepper, a stock target and a Craft button for crafting by hand. Locked recipes show what unlocks them.

**Automating crafting.** Crafting is automated with necromancers, like gathering. Each necromancer assigned to a recipe crafts it again and again on its own, and the page shows how many necromancers are free. A recipe can have a stock target, and its necromancers stop when it is reached. Rules pause a recipe when an ingredient runs low, move idle necromancers to the next recipe and keep crafting while the player is away. Enchanting adds a rule to attach a rune automatically to new pieces of gear. An anvil or cauldron upgrade raises speed and can be bought automatically.

Smithing and Enchanting here replace the earlier idea of gear only passing through the Transmuter. How these skills relate to the Transmuter is an open question.

**Screens still to design.** The siege-mode battle screen, the Hunting, Fishing and Herbalism pages (they reuse the gathering view with their own items), the remaining building pages and the away recap. The old battle and formation mockups also need redrawing for the lich-plus-summons format.

## Architecture

The game is built in Godot with C#. The four managers below are the starting point. Everything else on this page is a proposal until it is agreed.

&#91;embedded content: manager architecture: GameManager, TickManager, LichManager, CryptManager and the UI\]

The GameManager starts everything. The TickManager drives the Lich and Crypt managers on each tick. The UI sits on top and only reads state.

**Principles**

- **Simulation and UI are separate.** Game state lives in plain C# classes. UI scenes read that state and call manager methods, and never change state themselves. This keeps the game testable and lets offline progress run without the UI.
- **Managers are autoload singletons.** Each manager is a C# script on a node that Godot loads at startup, so any scene can reach it.
- **Everything advances on the tick.** Apart from direct player commands, game state only changes inside a tick, in a fixed order.
- **Signals tell the UI what changed.** Managers raise events when state changes, and the UI updates from them rather than polling.
- **Numbers need a big-number type.** Skill XP at level 999 is around 10^43 and necromancers can reach 100 billion, which is past what a 64-bit integer holds. Whether to use double or a custom big-number type is an open question.

**The four managers**

| Manager | Responsibility | Owns |
| --- | --- | --- |
| GameManager | Starts the game, looks for saves, loads or starts a new game, writes saves, and works out how much time passed while the player was away. | The save files, the order managers start in |
| TickManager | Runs a tick on a timer, checks what is automated and processes it, then reports that the tick finished so the UI can update. | The tick length, the tick counter, the processing order |
| LichManager | Manages the lich: skills and levels, equipment, Health, Insight and every buff and debuff. Holds the lich's save data. | Skills, gear, stat modifiers, necromancer pool and assignments, lich save data |
| CryptManager | Manages everything in the Crypt: Workshop buildings, research, Unit Builder, storage and the necromancer pool. | Buildings, research, resources and storage, Unit Builder loadouts |

**GameManager**

- Runs first when the game starts and creates the other managers in a fixed order.
- Looks in the save folder, shows the saves it finds and loads the one the player picks, or starts a new game.
- Writes saves: on a timer, when the player quits and on request. It asks each manager for its save data and writes one file.
- On load, works out how long the player was away, turns that into a number of ticks and hands it to the TickManager.

**TickManager**

- Counts time and runs a tick when the tick length has passed. The tick is 2 seconds at startup, and it can be sped up or paused.
- On each tick it works out what is automated (necromancer gathering and crafting, Workshop buildings, research benches, the siege) and processes it in the order below.
- Raises an event at the start and end of each tick. The tick bar in the top bar is driven by the time left in the current tick.
- Runs many ticks at once for offline progress.

**LichManager**

- Holds the lich's skills, levels and XP, and tracks where every necromancer is assigned.
- Holds equipment and Health, and the spells equipped within Insight.
- Holds a list of stat modifiers. Each modifier has a source, a stat, whether it adds or multiplies, a value and an optional end time. Equipment, research, potions, spells and enemy effects all add modifiers, and the lich's stats are worked out from the list. Buffs and debuffs are modifiers that expire.
- Writes and reads the lich's save data.

**CryptManager**

- Holds every resource with its stock and cap, and gives other systems a way to add or spend them.
- Runs the Workshop buildings in flow order, so mana made by the Mana Well is available to the Infuser in the same tick.
- Runs the research benches and unlocks nodes when they finish.
- Raises new necromancers through the Summoning Circle and adds them to the pool, which the LichManager holds.
- Holds the Unit Builder's loadouts and stacks.
- Writes and reads the Crypt's save data.

**One class per feature**

Every skill, building, research tome and the spell book has its own class that manages all of its state and behaviour. The managers own these classes and do not contain their logic.

- **LichManager** owns one class per skill: MiningManager, FishingManager, HuntingManager, HerbalismManager, SmithingManager, AlchemyManager, EnchantingManager, and the Health, Necromancy and Magic skills. It also owns the SpellBook class.
- **CryptManager** owns one class per Workshop building: ManaWell, SummoningCircle, Infuser, SoulWell, Transmuter, UndeadMarket and Storage. It also owns one ResearchTome class per tome and the Unit Builder.
- **Base classes keep them consistent.** Skills share a SkillBase, gathering skills share a GatheringSkill, crafting skills share a CraftingSkill, and buildings share a BuildingBase. A new skill or building is a small class on top of one of these.
- **One interface for ticking and saving.** Every feature class can be ticked and saved, so the managers treat them all the same way.

A sketch of the shape. Names are proposals:

```csharp
public interface ITickable { void Tick(long ticks); }
public interface ISaveable { SaveData Save(); void Load(SaveData data); }

public abstract class SkillBase : ITickable, ISaveable
{
    public string Id;
    public int Level;
    public abstract void Tick(long ticks);
}

public abstract class GatheringSkill : SkillBase
{
    // activity id to the number of necromancers working on it
    protected Dictionary<string, long> assigned;
    public bool AddNecromancer(string activityId, long count);
    public void RemoveNecromancer(string activityId, long count);
}

public class MiningManager : GatheringSkill { }
```

**Content is data**

Items, units, resources, research and recipes are defined in JSON files, not in code.

- **Abstract classes for the definitions.** An item definition is a base class with an id, a name and an icon. Weapons, armour, runes, potions, bombs and materials are subclasses that add their own fields. Units, research nodes and resources work the same way. New content is a new JSON entry, and new behaviour is a new subclass.
- **JSON files.** Resources, items, units, gathering activities, crafting recipes, spells and one file per research tome all live in a data folder. Every entry has a stable text id, such as iron\_ore.
- **Loaded once.** The GameManager loads all definitions at startup, before the managers start, into one read-only content database that every feature class reads.
- **Saves hold ids and counts.** A save stores ids, levels and quantities, not the definitions. Balance changes and new content do not break old saves.

**Automating gathering, step by step**

The example is pressing + on Iron Ore on the Mining page. Crafting works the same way through its own class.

1. The player presses + on Iron Ore. The page calls MiningManager.AddNecromancer for iron\_ore with the amount from the Assign by selector.
2. MiningManager asks the LichManager for that many free necromancers. The LichManager checks how many are free, records that they are assigned to Mining and iron\_ore, and updates the free and assigned counts. If fewer are free than asked for, it assigns the free ones. It raises an event so the top bar and the page update.
3. MiningManager records the new count on its iron\_ore activity.
4. On every tick, the TickManager reads the assignments from the LichManager and calls the matching feature class for each one.
5. MiningManager works out what its necromancers produced: the count times the rate for that activity. It adds the iron ore to storage through the CryptManager, keeping within the cap, and adds the skill XP. It raises events for the UI.
6. Pressing the minus button reverses step 2: the LichManager frees the necromancers and MiningManager lowers its count.

Offline progress uses the same maths with a number of ticks, so assigned necromancers keep producing while the player is away.

**Tick order**

1. TickManager advances the tick counter and raises the start event.
2. LichManager expires finished buffs and debuffs, applies health regeneration and processes cooldowns.
3. CryptManager runs the Workshop buildings in flow order: Mana Well, Infuser, Soul Well, Transmuter, Summoning Circle, Undead Market.
4. TickManager reads the necromancer assignments from the LichManager and calls the matching feature class for each one. The class works out what its necromancers produced and adds it to storage through the CryptManager.
5. CryptManager advances the research benches.
6. The siege or encounter, if one is running, is processed. This system does not have an owner yet.
7. TickManager raises the end event, the UI updates and the GameManager checks whether it is time to autosave.

**Saving**

- Each manager implements the same small interface: one method that returns its save data and one that loads it.
- Save data is plain C# classes, written as JSON. The file has a version number and the time it was saved, so old saves can be upgraded.
- The GameManager writes to a temporary file and then renames it, so a crash cannot leave half a save. It keeps a backup of the previous save.
- Saves go in Godot's user folder. The game supports several save slots.

**Offline progress.** When a save loads, the GameManager compares the saved time with the current time. The TickManager then processes that many ticks. For long absences it uses batched maths for each system and does not loop one tick at a time. The result is collected into data for the away recap screen.

**Systems that still need an owner**

| System | Proposed owner | Note |
| --- | --- | --- |
| World Map, sieges and the Host | A fifth manager (WorldManager) | Strongholds, rings, siege layers, relief armies and Dread |
| Boss and dungeon encounters | WorldManager, or an EncounterManager | Uses the lich, three summon slots and the boss |
| Spell Book and Insight | LichManager | Spells equipped, cooldowns and casting rules |
| Gathering and crafting skills | LichManager | One class per skill, such as MiningManager, owned by the LichManager. Items go to storage through the CryptManager |
| Tomes and the research tree | CryptManager | Tomes found on the map unlock trees |
| Settings, audio, screen navigation | GameManager or a UIManager | Not part of the simulation |

**Open questions for the architecture**

- How does the Summoning Circle add new necromancers to the LichManager's pool: by calling it directly or by raising an event?
- How are feature classes registered so the TickManager can find them: in code or from a list in JSON?
- Do sieges and encounters belong to one WorldManager or to two managers?
- Which number type holds values past 64 bits?
- Can the player change tick length, and what happens to offline progress if they do?
- Should the TickManager call managers directly in a fixed order, or should each manager register itself with a priority?

## Open questions and next steps

**Decided for v0.1**, then the questions still open.

- **Summoning:** necromancers are summoned in the Summoning Circle in the Workshop and join the free pool, ready to be assigned to automation.
- **XP curve:** a flat 10% more per level to 999, no easing.
- **Training:** the lich can train several skills at once, depending on how the player spreads necromancers across automation.
- **Mastery:** none for now, maybe later.
- **Coins:** buy upgrades for the Workshop, research and similar.
- **World map:** no rings for now. The map is a set of paths. v0.1 has two areas: the Graveyard (a small undead army belonging to another lich, with the graveyard gates as its walls) and the Woods (where the wild people live).
- **Battles:** they should feel like a battle. Each army section has up to 3 actions, each on its own cooldown. For example, a front-middle swordsman attacks every 2 seconds and braces every 10 seconds (the next attack against it does 10% less damage). HP is shown as bars.
- **Gear:** every summon uses up its weapon, armour and items.
- **Prestige:** late game, not part of v0.1.

* [ ] Formation size: eight slots (2 front, 3 mid, 3 back) as now planned, or the earlier nine?
* [ ] Gear economy: does every unit use up a weapon and armour when summoned, or is gear a one-time upgrade per unit type?
* [ ] Focus rule: is Max Focus a budget that spells spend, or a simple cap on how many spells can be equipped?
* [ ] Can more than one Host fight on different paths at the same time, and when does that unlock?

- [ ] Do summoned units also cost mana, or only items?
- [ ] Should the three summon slots grow later, for example to five, and what unlocks them?
- [ ] Can the player restock inside a dungeon, or is the inventory fixed once inside?
- [ ] Should Health also give HP regeneration, or only maximum HP?

**Suggested next steps**

1. Decide the open questions above.
2. Build a vertical slice: Mining, one Workshop chain (Mana Well to Summoning Circle), Skeletons, and one Graveyard siege.
3. Add research benches and the Necromancy 1 tree.
4. Add the alarm stages, Dread and the first relief army once the siege feels good.
