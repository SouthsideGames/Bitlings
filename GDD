# Bitlings — Project Guide

Bitlings is a released Unity (URP) creature-collection + idle/management game for mobile.
Players collect ~180 "Bitling" monsters, assign them to jobs, battle, and compete in arenas.

## Layout

- `Assets/Scripts/` — all gameplay code (~420 C# files), organized **by feature**, not by layer.
- `Assets/Scenes/Main.unity` — the game runs from a **single scene**. Systems are wired here.
- `Assets/Resources/` — runtime-loaded config `.asset` files (`GameBalanceConfig`, `MonsterLibrary`,
  `LevelCostCurve`, `HealingConfig`, `IdleBattleConfig`, etc.).
- `Assets/CSV/` — source-of-truth content tables (`Bitling Monsters.csv`, `Bitlings Achievements.csv`,
  `World Events.csv`, `Recycle Recipe.csv`). These are imported into ScriptableObjects via Editor tooling.
- `MonsterDesignRules.md` — the balance bible: stat budgets by rarity, type personality profiles,
  evolution/spawn/job/fatigue/regen rules. Consult before adding or rebalancing monsters.

## Architecture conventions

- **Manager singletons** are `MonoBehaviour`s living in the Main scene, exposed as `public static X I;`
  (or `public static X I { get; private set; }`). Access via `JobManager.I`, `AudioManager.I`, etc.
  Always null-check `.I` — managers may not be active during menu-first boot.
- **`SaveManager`** is a static (non-MonoBehaviour) facade. `SaveManager.Data` is the live
  `PlayerManager` (a `[Serializable]` POCO, **not** a MonoBehaviour). `SaveManager.NowUnix()` is the
  canonical time source — use it, not `DateTime.Now`, for anything persisted.
- **Big managers are split into partial classes** by concern, e.g.
  `BattleManager.cs` / `.TurnLoop.cs` / `.Statuses.cs` / `.Ending.cs` / `.UI.cs`.
  Keep new battle logic in the matching partial.
- **Logging:** use `DevLog.Log(...)` for informational logs — it is `[Conditional]`-compiled out of
  release builds. Reserve raw `Debug.LogWarning` / `Debug.LogError` for genuine problems that should
  surface in release. Avoid plain `Debug.Log(...)` (it ships in release).

## Save system (handle with care — this is a live game)

Saving is robust and you should preserve its guarantees:
- Atomic writes via a `.tmp` file with crash recovery on load (`LoadOrCreate`).
- A `.bak` backup is kept and used as a fallback when the primary file fails to load.
- `SaveValidator.ValidateAndRepair` runs on every load; `SaveMigrationManager` versions old saves.
- Cloud sync via UGS (`CloudSaveSync`) for arena data.
- `PlayerManager.EnsureTransientSets()` rebuilds runtime `HashSet`/`Dictionary` mirrors from the
  serialized `*List` fields after load. If you add a `[NonSerialized]` set, mirror it the same way.

When adding persisted fields to `PlayerManager` / `SaveData`, default them safely (old saves won't
have them) and add a migration step if existing saves need transformation.

## Time / offline progression

Offline catch-up exists in several systems (`JobManager.ResolveOfflineIfAny`,
`IdleBattleManager`, `HealthRegenSystem`, `EnergyRegenSystem`, `ExchangeManager`). The established
pattern: clamp elapsed to `Mathf.Max(0, now - last)` (guards clock rollback), and bound the result
(storage caps for jobs, `maxOfflineHours` for idle battle). Follow this when adding time-based gains.

## Content workflow

To add monsters/achievements/events: edit the relevant `Assets/CSV/*.csv`, then run the Editor
import tooling (see `Assets/Editor/`) to regenerate the ScriptableObjects. Follow `MonsterDesignRules.md`
for stat budgets and naming.
