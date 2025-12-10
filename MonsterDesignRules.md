# Bitlings – Monster Design Rules

**Version 1.0 — Updated for Fatigue & Regeneration Systems**

This document outlines the standardized rules used to create all Bitlings, including stats, naming, rarity behavior, work mechanics, and thematic flavor. These rules ensure consistency across the entire roster and allow future monsters to be added without breaking balance.

---

# 1. MonsterDataSO Structure Assumptions

Each monster uses the following fields from `MonsterDataSO`:

```csharp
// Identity
public string id;
public string displayName;
public MonsterType type;
public Sprite icon;
public Sprite backIcon;
public Sprite shinyIcon;
public Sprite shinyBackIcon;

// Starter
public bool canBeStarter;
public int starterWeight;

// Stats
public int baseHP;
public int baseAttack;
public int baseDefense;
public int baseSpeed;

// Evolution
public int evolutionStage;
public int evolutionLevel;
public MonsterDataSO evolutionForm;

// Encounters
public float spawnWeight;

// Collection & Jobs
public float jobSkill;
public Rarity rarity;

// Fatigue
public float fatigueRatePerHour;
public float fatigueCooldownHours;

// Regeneration
public float hpRegenPerHour;

// Description
public string description;

// Boss-Only
public int bossWeight;
Boss monsters do not use spawnWeight and instead rely on bossWeight.

2. ID & Naming Conventions
Standard Monsters
IDs follow the format: M-001, M-002, …

Names are based on visual silhouette, mood, and flavor.

Avoid repeated suffixes (e.g., “-ling”, “-mon”, “-flame”).

Names must be:

Unique

Easy to pronounce

Thematically appropriate

Boss Monsters
IDs follow the format: M-B01, M-B02, …

Names should have a mythic-tier style (e.g., Nyxiron, Chitigon, Pyrranth).

Should feel ancient, powerful, or divine.

3. Rarity → Stat Budget
Each rarity tier has a fixed total stat budget:

Rarity	Total Stats
Common	200
Uncommon	220
Rare	240
Epic	260
Legendary	280
Mythic	300
Boss	320

These points are distributed using Type Personality Profiles.

4. Stat Distribution by Type
(Personality Profiles)

Stat percentages applied to the total stat budget:

Type	HP	Atk	Def	Spd	Notes
Fire	20%	35%	20%	25%	Aggressive striker
Water	30%	25%	30%	15%	Defensive tank
Grass	30%	25%	25%	20%	Balanced & resilient
Electric	20%	30%	20%	30%	Fast attacker
Ice	25%	25%	30%	20%	High defense
Rock	35%	20%	30%	15%	Slow, tanky
Ground	30%	25%	30%	15%	Sturdy bruiser
Bug	25%	35%	20%	20%	Aggressive swarm type
Sky	25%	30%	20%	25%	Agile aerial fighter
Oracle	20%	20%	20%	40%	Prediction-based speedster
Clash	25%	35%	20%	20%	Physical brawler
Corrupt	30%	25%	20%	25%	Twisted, unstable
Wyrm	30%	30%	20%	20%	Burly dragon
Specter	20%	30%	20%	30%	Fast spectral striker
Umbral	25%	30%	20%	25%	Shadow predator
Alloy	25%	25%	35%	15%	Mechanical tank

Minor adjustments are made when the sprite clearly indicates a different emphasis.

5. Evolution Rules
Stage Logic
Stage 1 = Base form

Stage 2 = First evolution

Stage 3 = Final evolution

Evolution Levels
Two-stage lines: evolve at Level 18–22

Three-stage lines: evolve at Level 16 → 32

Spawn Weights
Stage 1 forms: normal spawn weights

Evolutions: typically spawnWeight = 0

Only obtainable via evolution unless specified otherwise

6. Spawn Weight Rules
Rarity	Typical Spawn Weight
Common	6–8
Uncommon	4–6
Rare	2–3
Epic	1–2
Legendary	0.5–1
Mythic	0.25–0.5
Boss	0 (uses bossWeight instead)

7. Job Skill Rules
jobSkill ranges 0.5–3.0, determining work-site performance.

By Rarity:
Rarity	Typical Range
Common	0.9–1.1
Uncommon	1.1–1.3
Rare	1.2–1.5
Epic	1.4–1.7
Legendary	1.6–2.0
Mythic	2.0–2.5
Boss	2.6–2.9

Job Alignment
Fire → Forge, Volcano

Grass → Grove

Water → Harbor, Cryo Lab

Electric → Power Plant

Rock / Ground → Mine, Quarry

Sky → Observatory

Specter / Umbral / Oracle → Sanctum, Shadow Market

Alloy → Workshop, Power Plant

8. Fatigue System Rules
Fatigue Rate per Hour
(0.00 – 0.20)
Controls how quickly a monster accumulates fatigue while working.

Type Defaults:
High (0.05–0.07):
Fire, Electric, Clash, Wyrm, Bosses

Medium (0.03–0.04):
Grass, Water, Bug, Rock, Ground, Alloy, Sky

Low (0.02–0.03):
Oracle, Specter, Umbral

Very Low:
Special lore-based exceptions

Fatigue Cooldown Hours
Time required to fully recover from 100% fatigue.

Rarity	Cooldown Hours
Common	7–8
Uncommon	6–7
Rare	5–6
Epic	4–5
Legendary	3–4
Mythic	2–3
Boss	10–14

9. HP Regeneration Rules
Type Base Regen per Hour
Type	hpRegen/hr
Water	8–10
Grass	7–9
Oracle	6–9
Clash	6–8
Wyrm	6–8
Ground	5–7
Fire	5–6
Electric	5–7
Rock	4–6
Alloy	4–6
Bug	4–6
Ice	4–5
Umbral	4–6
Specter	3–4
Corrupt	2–4

Rarity Bonus
Rarity	Bonus
Common	+0
Uncommon	+0.5
Rare	+1
Epic	+2
Legendary	+3
Mythic	+4
Boss	+5–7

Final Formula:
ini
Copy code
hpRegenPerHour = TypeBase + RarityBonus
10. Boss-Specific Rules
Bosses follow these additional guidelines:

Total stat budget = 320

Always Stage 1, no evolution

spawnWeight = 0

bossWeight = 1 (or custom)

jobSkill = 2.6–2.9

fatigueRatePerHour = 0.06–0.10

fatigueCooldownHours = 10–14

hpRegenPerHour = 10–12+

Flavor text must clearly define the monster as a world-level threat

11. Flavor Text Rules
Flavor text should be:

1–2 sentences

Written in a Pokédex-style tone

Include elements of:

Habitat

Behavior

Rumor or myth

Emotional tone

Avoid real-world references

Example:
“Volcranox’s fiery core burns hotter than a collapsing star. It roams volcanic tunnels, leaving rivers of molten stone in its wake.”
```
