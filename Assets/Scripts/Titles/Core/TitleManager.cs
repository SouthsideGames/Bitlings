using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Central Titles runtime. Designed to be resilient across refactors:
/// - Indexes TitleSO assets (preload list + Resources scan)
/// - Resolves equipped titles from SaveManager.Data (reflection-safe)
/// - Routes core mechanics via core ScriptableObjects (by type name + reflection)
/// - Maintains per-battle state for stack/turn/battle-start style titles (best-effort)
/// </summary>
public sealed class TitleManager : MonoBehaviour
{
    public static TitleManager I { get; private set; }

    [Header("Library / Lookup")]
    [Tooltip("If non-empty, these TitleSO are indexed first. Manager also scans Resources for all TitleSO assets on Awake.")]
    [SerializeField] private List<TitleSO> preloadTitles = new List<TitleSO>();

    [Header("Debug")]
    [SerializeField] private bool debugEffectiveness = false;

    // id -> TitleSO
    private readonly Dictionary<string, TitleSO> _idToTitle = new Dictionary<string, TitleSO>();

    private string _activeBattleMonsterId;

    // ─────────────────────────────────────────────────────────────────────
    // Per-battle state (TurnBooster / EventStacks / BattleStart)
    // These are maintained best-effort; exact behavior depends on your core SO fields.
    // ─────────────────────────────────────────────────────────────────────
    private readonly Dictionary<string, int> _turnStacks = new();
    private readonly Dictionary<string, int> _eventStacks = new();
    private readonly Dictionary<string, int> _eventMax = new();
    private readonly Dictionary<string, int> _eventDecayPerTurn = new();
    private readonly Dictionary<string, int> _flatStartUntilTurn = new();
    private readonly Dictionary<string, int> _flatStartAmountAtk = new();
    private readonly Dictionary<string, float> _shieldRemaining = new();
    private int _turnIndex;

    // ─────────────────────────────────────────────────────────────────────
    // Active Title UI state (Status Bar / Info Button)
    // ─────────────────────────────────────────────────────────────────────
    public struct ActiveTitleUIState
    {
        public string titleId;
        public string displayName;
        public Sprite icon;
        public int stacks;
        public bool isActive;
    }

    // Adapter expects fields: cannotBeCrit, percentReduce, flatReduce
    public struct TitleDamageFilter
    {
        public bool cannotBeCrit;
        public float percentReduce;   // 0.20 = 20% less damage after DEF
        public int flatReduce;        // subtract after % reduce
    }

    // ─────────────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;
        BuildIndex();
    }

    private void BuildIndex()
    {
        _idToTitle.Clear();

        // 1) Preload list (inspector)
        if (preloadTitles != null)
        {
            for (int i = 0; i < preloadTitles.Count; i++)
            {
                var t = preloadTitles[i];
                if (!t || string.IsNullOrEmpty(t.titleId)) continue;
                if (!_idToTitle.ContainsKey(t.titleId))
                    _idToTitle.Add(t.titleId, t);
            }
        }

        // 2) Resources scan
        var all = Resources.LoadAll<TitleSO>("");
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (!t || string.IsNullOrEmpty(t.titleId)) continue;
            if (!_idToTitle.ContainsKey(t.titleId))
                _idToTitle.Add(t.titleId, t);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Battle lifecycle hooks (called via TitlesAdapter)
    // ─────────────────────────────────────────────────────────────────────
    public void OnBattleStart(string activeMonsterId, MonsterDataSO wild, int wildLevel)
    {
        _activeBattleMonsterId = activeMonsterId ?? "";
        _turnIndex = 0;

        _turnStacks.Clear();
        _eventStacks.Clear();
        _eventMax.Clear();
        _eventDecayPerTurn.Clear();
        _flatStartUntilTurn.Clear();
        _flatStartAmountAtk.Clear();
        _shieldRemaining.Clear();

        // Best-effort: initialize any BattleStart effects from equipped titles.
        // This supports your "Battle Start Flat" and "Battle Start Shield" cores if they expose expected fields.
        var titles = GetTitlesForMonster(activeMonsterId);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            // Battle Start Flat (commonly boosts ATK for first N turns)
            if (HasCoreType(t, "BattleStartFlat"))
            {
                int amt = ReadIntFromCore(t, new[] { "amountAtk", "atkFlat", "flatAttack", "amount" }, fallback: 0);
                int untilTurn = ReadIntFromCore(t, new[] { "untilTurn", "turns", "durationTurns", "duration" }, fallback: 0);

                if (amt != 0 && untilTurn > 0)
                {
                    _flatStartAmountAtk[t.titleId] = amt;
                    _flatStartUntilTurn[t.titleId] = untilTurn;
                }
            }

            // Battle Start Shield (commonly grants a shield pool)
            if (HasCoreType(t, "BattleStartShield"))
            {
                float shield = ReadFloatFromCore(t, new[] { "shield", "shieldAmount", "amount", "value" }, fallback: 0f);
                if (shield > 0f)
                    _shieldRemaining[t.titleId] = shield;
            }

            // Turn Booster (commonly stacks each turn up to a cap)
            if (HasCoreType(t, "TurnBooster"))
            {
                int max = ReadIntFromCore(t, new[] { "maxStacks", "max", "cap" }, fallback: 0);
                if (max > 0) _turnStacks[t.titleId] = 0;
            }

            // Event Stacks (commonly stacks on events like crit / hit / etc.)
            if (HasCoreType(t, "EventStacks"))
            {
                int max = ReadIntFromCore(t, new[] { "maxStacks", "max", "cap" }, fallback: 0);
                int decay = ReadIntFromCore(t, new[] { "decayPerTurn", "decay", "decayStacksPerTurn" }, fallback: 0);

                if (max > 0) _eventMax[t.titleId] = max;
                if (decay > 0) _eventDecayPerTurn[t.titleId] = decay;

                _eventStacks[t.titleId] = 0;
            }
        }
    }

    public void OnBattleEnd(string activeMonsterId, bool victory, MonsterDataSO wild, int wildLevel)
    {
        _activeBattleMonsterId = "";
        _turnIndex = 0;

        _turnStacks.Clear();
        _eventStacks.Clear();
        _eventMax.Clear();
        _eventDecayPerTurn.Clear();
        _flatStartUntilTurn.Clear();
        _flatStartAmountAtk.Clear();
        _shieldRemaining.Clear();
    }

    public void OnTurnAdvanced(int turnIndex)
    {
        _turnIndex = Mathf.Max(0, turnIndex);

        // Turn stacks: increment up to cap if core exposes one
        var titles = GetTitlesForMonster(_activeBattleMonsterId);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (HasCoreType(t, "TurnBooster"))
            {
                int max = ReadIntFromCore(t, new[] { "maxStacks", "max", "cap" }, fallback: 0);
                int add = ReadIntFromCore(t, new[] { "stacksPerTurn", "perTurn", "addPerTurn", "gainPerTurn" }, fallback: 1);

                if (!_turnStacks.TryGetValue(t.titleId, out var cur)) cur = 0;
                cur += Mathf.Max(0, add);
                if (max > 0) cur = Mathf.Min(cur, max);
                _turnStacks[t.titleId] = cur;
            }

            // Event stack decay
            if (HasCoreType(t, "EventStacks") && _eventStacks.TryGetValue(t.titleId, out var es))
            {
                int decay = _eventDecayPerTurn.TryGetValue(t.titleId, out var d) ? d : 0;
                if (decay > 0)
                {
                    es = Mathf.Max(0, es - decay);
                    _eventStacks[t.titleId] = es;
                }
            }
        }
    }

    public void OnAttackLanded(string attackerId, bool wasCrit)
    {
        // Best-effort: increment EventStacks on crit/hit.
        if (string.IsNullOrEmpty(attackerId)) return;

        var titles = GetTitlesForMonster(attackerId);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;
            if (!HasCoreType(t, "EventStacks")) continue;

            // If core exposes a "requiresCrit" boolean, respect it.
            bool requiresCrit = ReadBoolFromCore(t, new[] { "requiresCrit", "onCritOnly" }, fallback: false);
            if (requiresCrit && !wasCrit) continue;

            int add = ReadIntFromCore(t, new[] { "stacksPerEvent", "addPerEvent", "gainPerEvent", "stacksOnEvent" }, fallback: 1);

            int max = _eventMax.TryGetValue(t.titleId, out var m) ? m : ReadIntFromCore(t, new[] { "maxStacks", "max", "cap" }, fallback: 0);

            if (!_eventStacks.TryGetValue(t.titleId, out var cur)) cur = 0;
            cur += Mathf.Max(0, add);
            if (max > 0) cur = Mathf.Min(cur, max);
            _eventStacks[t.titleId] = cur;
        }
    }

    public void OnHitTaken(string defenderId, int damage, bool wasCrit)
    {
        // Optional: if you later want "on hit taken" event stacking, add here.
        // Keeping it safe/no-op beyond EventStacks if configured.
        if (string.IsNullOrEmpty(defenderId)) return;

        var titles = GetTitlesForMonster(defenderId);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;
            if (!HasCoreType(t, "EventStacks")) continue;

            bool triggersOnHitTaken = ReadBoolFromCore(t, new[] { "onHitTaken", "triggersOnHitTaken", "stackOnHitTaken" }, fallback: false);
            if (!triggersOnHitTaken) continue;

            int add = ReadIntFromCore(t, new[] { "stacksPerEvent", "addPerEvent", "gainPerEvent", "stacksOnEvent" }, fallback: 1);

            int max = _eventMax.TryGetValue(t.titleId, out var m) ? m : ReadIntFromCore(t, new[] { "maxStacks", "max", "cap" }, fallback: 0);

            if (!_eventStacks.TryGetValue(t.titleId, out var cur)) cur = 0;
            cur += Mathf.Max(0, add);
            if (max > 0) cur = Mathf.Min(cur, max);
            _eventStacks[t.titleId] = cur;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Queries: Equipped titles, stats, jobs, filters, rewards
    // ─────────────────────────────────────────────────────────────────────

    public List<TitleSO> GetTitlesForMonster(string monsterId)
    {
        var results = new List<TitleSO>();
        if (string.IsNullOrEmpty(monsterId)) return results;

        // 1) Primary: resolve from SaveManager.Data (OwnedMonster)
        var owned = FindOwnedMonsterById(monsterId);
        if (owned != null)
        {
            // Support: single equipped title id OR list of ids
            var single = ReadString(owned, new[] { "equippedTitleId", "titleId", "equippedTitle", "equippedTitleID" });
            if (!string.IsNullOrEmpty(single))
                TryAddTitle(single, results);

            var list = ReadStringList(owned, new[] { "equippedTitleIds", "titleIds", "equippedTitles", "equippedTitleIDList" });
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                    TryAddTitle(list[i], results);
            }
        }

        // 2) Fallback: if monsterId is a definition id (not owned), check if def carries a title id.
        // This is intentionally best-effort; you generally want ownedId here.
        if (results.Count == 0)
        {
            var def = FindMonsterDefById(monsterId);
            if (def != null)
            {
                var tid = ReadString(def, new[] { "equippedTitleId", "titleId" });
                if (!string.IsNullOrEmpty(tid))
                    TryAddTitle(tid, results);
            }
        }

        // De-dupe by id
        if (results.Count > 1)
            results = results.Where(x => x != null).GroupBy(x => x.titleId).Select(g => g.First()).ToList();

        return results;
    }

    public string GetEquippedTitleId(string ownedMonsterId)
    {
        var owned = FindOwnedMonsterById(ownedMonsterId);
        if (owned == null) return "";

        var single = ReadString(owned, new[] { "equippedTitleId", "titleId", "equippedTitle", "equippedTitleID" });
        return single ?? "";
    }

    public List<ActiveTitleUIState> GetActiveTitleUIStates(string ownedMonsterId)
    {
        var titles = GetTitlesForMonster(ownedMonsterId);
        var list = new List<ActiveTitleUIState>(titles.Count);

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            int stacks = 0;

            // Prefer event stacks if present, else turn stacks, else 0
            if (_eventStacks.TryGetValue(t.titleId, out var es)) stacks = es;
            else if (_turnStacks.TryGetValue(t.titleId, out var ts)) stacks = ts;

            bool active = true;

            // BattleStartFlat active window (until N turns)
            if (_flatStartUntilTurn.TryGetValue(t.titleId, out var until))
                active = _turnIndex < until;

            // BattleStartShield active if shield remains
            if (_shieldRemaining.TryGetValue(t.titleId, out var shield))
                active = shield > 0.0001f;

            list.Add(new ActiveTitleUIState
            {
                titleId = t.titleId,
                displayName = string.IsNullOrEmpty(t.displayName) ? t.name : t.displayName,
                icon = TryGetIconFromTitle(t),
                stacks = stacks,
                isActive = active
            });
        }

        return list;
    }

    public Sprite TryGetIconByTitleName(string titleName)
    {
        if (string.IsNullOrEmpty(titleName)) return null;

        // Commonly you’ll search by displayName; fallback to asset name.
        foreach (var kv in _idToTitle)
        {
            var t = kv.Value;
            if (!t) continue;

            var dn = string.IsNullOrEmpty(t.displayName) ? t.name : t.displayName;
            if (string.Equals(dn, titleName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.name, titleName, StringComparison.OrdinalIgnoreCase))
            {
                return TryGetIconFromTitle(t);
            }
        }
        return null;
    }

    public float GetStatValueRouter(string monsterId, MonsterDataSO def, int level, string statKind, TitleContext ctx, float baseValue)
    {
        // This method is your central router for all stat modifications from Titles.
        // It attempts:
        // 1) Known core types (Stat Booster / Dual / Conditional) via reflection.
        // 2) Turn/Event stacks if the core references them.
        // If nothing matches, returns baseValue.

        if (string.IsNullOrEmpty(monsterId) || string.IsNullOrEmpty(statKind))
            return baseValue;

        level = Mathf.Max(1, level);

        float value = baseValue;

        var titles = GetTitlesForMonster(monsterId);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            // Apply battle-start flat ATK (only when asking for Attack and within duration)
            if (IsStat(statKind, "Attack", "ATK", "Atk") && _flatStartAmountAtk.TryGetValue(t.titleId, out var amt))
            {
                if (_flatStartUntilTurn.TryGetValue(t.titleId, out var until) && _turnIndex < until)
                    value += amt;
            }

            // Apply TurnBooster stacks if this is a TurnBooster core and it targets this stat
            if (HasCoreType(t, "TurnBooster"))
            {
                int stacks = _turnStacks.TryGetValue(t.titleId, out var s) ? s : 0;

                // Fields commonly used: statKind / stat / targetStat, perStackFlat, perStackPct
                string target = ReadStringFromCore(t, new[] { "statKind", "stat", "targetStat" });
                if (!string.IsNullOrEmpty(target) && !IsStat(statKind, target)) { /* not for this stat */ }
                else
                {
                    float perFlat = ReadFloatFromCore(t, new[] { "perStackFlat", "flatPerStack", "amountPerStack" }, 0f);
                    float perPct  = ReadFloatFromCore(t, new[] { "perStackPct", "pctPerStack", "percentPerStack" }, 0f);

                    if (Mathf.Abs(perFlat) > 0.0001f) value += perFlat * stacks;
                    if (Mathf.Abs(perPct)  > 0.0001f) value *= (1f + perPct * stacks);
                }
            }

            // Apply EventStacks similarly
            if (HasCoreType(t, "EventStacks"))
            {
                int stacks = _eventStacks.TryGetValue(t.titleId, out var s) ? s : 0;

                string target = ReadStringFromCore(t, new[] { "statKind", "stat", "targetStat" });
                if (!string.IsNullOrEmpty(target) && !IsStat(statKind, target)) { /* not for this stat */ }
                else
                {
                    float perFlat = ReadFloatFromCore(t, new[] { "perStackFlat", "flatPerStack", "amountPerStack" }, 0f);
                    float perPct  = ReadFloatFromCore(t, new[] { "perStackPct", "pctPerStack", "percentPerStack" }, 0f);

                    if (Mathf.Abs(perFlat) > 0.0001f) value += perFlat * stacks;
                    if (Mathf.Abs(perPct)  > 0.0001f) value *= (1f + perPct * stacks);
                }
            }

            // Stat Booster / Dual / Conditional (best-effort)
            if (HasAnyCoreType(t, "StatBooster", "DualStatBooster", "ConditionalStatBooster", "ConditionalDualStatBooster"))
            {
                // Expected fields (best-effort):
                // - for single: statKind/stat + flat/pct
                // - for dual: statA/statB + flatA/flatB + pctA/pctB OR shared flat/pct with "stats" list
                // - for conditional: context gating; we only apply when ctx matches, if core exposes it

                if (!CoreAllowsContext(t, ctx)) continue;

                // Single stat booster
                string target = ReadStringFromCore(t, new[] { "statKind", "stat", "targetStat" });
                if (!string.IsNullOrEmpty(target))
                {
                    if (!IsStat(statKind, target)) continue;

                    float flat = ReadFloatFromCore(t, new[] { "flat", "flatBonus", "amountFlat", "valueFlat" }, 0f);
                    float pct  = ReadFloatFromCore(t, new[] { "pct", "percent", "percentBonus", "multPct" }, 0f);
                    if (Mathf.Abs(flat) > 0.0001f) value += flat;
                    if (Mathf.Abs(pct)  > 0.0001f) value *= (1f + pct);
                }
                else
                {
                    // Dual stat booster style: try statA/statB
                    string a = ReadStringFromCore(t, new[] { "statA", "stat1", "firstStat" });
                    string b = ReadStringFromCore(t, new[] { "statB", "stat2", "secondStat" });

                    if (IsStat(statKind, a))
                    {
                        float flatA = ReadFloatFromCore(t, new[] { "flatA", "flat1", "firstFlat" }, 0f);
                        float pctA  = ReadFloatFromCore(t, new[] { "pctA", "percentA", "pct1" }, 0f);
                        float flat  = ReadFloatFromCore(t, new[] { "flat", "flatBonus" }, 0f);
                        float pct   = ReadFloatFromCore(t, new[] { "pct", "percent" }, 0f);

                        if (Mathf.Abs(flatA) > 0.0001f) value += flatA;
                        else if (Mathf.Abs(flat) > 0.0001f) value += flat;

                        if (Mathf.Abs(pctA) > 0.0001f) value *= (1f + pctA);
                        else if (Mathf.Abs(pct) > 0.0001f) value *= (1f + pct);
                    }
                    else if (IsStat(statKind, b))
                    {
                        float flatB = ReadFloatFromCore(t, new[] { "flatB", "flat2", "secondFlat" }, 0f);
                        float pctB  = ReadFloatFromCore(t, new[] { "pctB", "percentB", "pct2" }, 0f);
                        float flat  = ReadFloatFromCore(t, new[] { "flat", "flatBonus" }, 0f);
                        float pct   = ReadFloatFromCore(t, new[] { "pct", "percent" }, 0f);

                        if (Mathf.Abs(flatB) > 0.0001f) value += flatB;
                        else if (Mathf.Abs(flat) > 0.0001f) value += flat;

                        if (Mathf.Abs(pctB) > 0.0001f) value *= (1f + pctB);
                        else if (Mathf.Abs(pct) > 0.0001f) value *= (1f + pct);
                    }
                }
            }
        }

        return value;
    }

    // NEW: required by ITitlesRuntime (Job rate multiplier)
    public float GetJobRateMultiplier(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        if (string.IsNullOrEmpty(monsterId)) return 1f;
        level = Mathf.Max(1, level);

        float mul = 1f;
        var titles = GetTitlesForMonster(monsterId);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            // Your core list calls this "Conditional Job Rate Booster"
            if (!HasCoreType(t, "ConditionalJobRateBooster") && !HasCoreType(t, "JobRate")) continue;

            // Gate by job type if core exposes one
            if (!CoreAllowsJobSite(t, site)) continue;

            // Common fields: multiplier/mult/value/pct (as +%)
            float directMult = ReadFloatFromCore(t, new[] { "multiplier", "mult", "rateMult", "jobRateMult" }, float.NaN);
            if (!float.IsNaN(directMult) && !float.IsInfinity(directMult) && directMult > 0f)
            {
                mul *= directMult;
                continue;
            }

            float pct = ReadFloatFromCore(t, new[] { "pct", "percent", "ratePct", "jobRatePct" }, 0f);
            if (Mathf.Abs(pct) > 0.0001f)
            {
                mul *= (1f + pct);
                continue;
            }

            // Method-based fallback: GetMultiplier(monsterId, def, level, site) or similar
            float m2 = InvokeFloatOnCore(t, new[]
            {
                new InvokeSpec("GetMultiplier", new object[] { monsterId, def, level, site }),
                new InvokeSpec("GetJobRateMultiplier", new object[] { monsterId, def, level, site }),
                new InvokeSpec("Evaluate", new object[] { monsterId, def, level, site }),
                new InvokeSpec("GetValue", new object[] { monsterId, def, level, site }),
            }, fallback: 1f);

            if (!float.IsNaN(m2) && !float.IsInfinity(m2) && m2 > 0f)
                mul *= m2;
        }

        return Mathf.Max(0f, mul);
    }

    public float GetJobFatigueMultiplier(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        if (string.IsNullOrEmpty(monsterId)) return 1f;
        level = Mathf.Max(1, level);

        float mul = 1f;
        var titles = GetTitlesForMonster(monsterId);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (!HasCoreType(t, "JobFatigueMult")) continue;
            if (!CoreAllowsJobSite(t, site)) continue;

            float direct = ReadFloatFromCore(t, new[] { "multiplier", "mult", "fatigueMult", "value" }, float.NaN);
            if (!float.IsNaN(direct) && !float.IsInfinity(direct) && direct > 0f) { mul *= direct; continue; }

            float pct = ReadFloatFromCore(t, new[] { "pct", "percent" }, 0f);
            if (Mathf.Abs(pct) > 0.0001f) { mul *= (1f + pct); continue; }

            float m2 = InvokeFloatOnCore(t, new[]
            {
                new InvokeSpec("GetMultiplier", new object[] { monsterId, def, level, site }),
                new InvokeSpec("GetJobFatigueMultiplier", new object[] { monsterId, def, level, site }),
                new InvokeSpec("Evaluate", new object[] { monsterId, def, level, site }),
            }, fallback: 1f);

            if (!float.IsNaN(m2) && !float.IsInfinity(m2) && m2 > 0f) mul *= m2;
        }

        return Mathf.Max(0f, mul);
    }

    public float GetJobAuraPercent(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        if (string.IsNullOrEmpty(monsterId)) return 0f;
        level = Mathf.Max(1, level);

        float pctSum = 0f;
        var titles = GetTitlesForMonster(monsterId);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (!HasCoreType(t, "JobAura")) continue;
            if (!CoreAllowsJobSite(t, site)) continue;

            float pct = ReadFloatFromCore(t, new[] { "percent", "pct", "auraPct", "value" }, 0f);
            if (Mathf.Abs(pct) > 0.0001f) { pctSum += pct; continue; }

            float v2 = InvokeFloatOnCore(t, new[]
            {
                new InvokeSpec("GetPercent", new object[] { monsterId, def, level, site }),
                new InvokeSpec("GetAuraPercent", new object[] { monsterId, def, level, site }),
                new InvokeSpec("Evaluate", new object[] { monsterId, def, level, site }),
            }, fallback: 0f);

            if (!float.IsNaN(v2) && !float.IsInfinity(v2)) pctSum += v2;
        }

        return pctSum;
    }

    public int GetJobCapacityBonusFlat(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        if (string.IsNullOrEmpty(monsterId)) return 0;
        level = Mathf.Max(1, level);

        int flat = 0;
        var titles = GetTitlesForMonster(monsterId);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (!HasCoreType(t, "JobCapacity")) continue;
            if (!CoreAllowsJobSite(t, site)) continue;

            int f = ReadIntFromCore(t, new[] { "flat", "flatBonus", "capacityFlat", "value" }, 0);
            flat += Mathf.Max(0, f);

            // method fallback
            float v2 = InvokeFloatOnCore(t, new[]
            {
                new InvokeSpec("GetFlatBonus", new object[] { monsterId, def, level, site }),
                new InvokeSpec("GetCapacityBonusFlat", new object[] { monsterId, def, level, site }),
                new InvokeSpec("Evaluate", new object[] { monsterId, def, level, site }),
            }, fallback: 0f);

            if (!float.IsNaN(v2) && !float.IsInfinity(v2)) flat += Mathf.Max(0, Mathf.RoundToInt(v2));
        }

        return Mathf.Max(0, flat);
    }

    public object GetDamageFilterBoxed(string monsterId, MonsterDataSO def, int level)
    {
        // Combine all relevant DamageFilter cores (if multiple titles)
        if (string.IsNullOrEmpty(monsterId))
            return new TitleDamageFilter { cannotBeCrit = false, percentReduce = 0f, flatReduce = 0 };

        level = Mathf.Max(1, level);

        bool blockCrit = false;
        float pct = 0f;
        int flat = 0;

        var titles = GetTitlesForMonster(monsterId);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (!HasCoreType(t, "DamageFilter")) continue;

            bool b = ReadBoolFromCore(t, new[] { "cannotBeCrit", "blockCrit", "noCrit" }, false);
            float p = ReadFloatFromCore(t, new[] { "percentReduce", "percent", "pctReduce", "damageReducePct" }, 0f);
            int f = ReadIntFromCore(t, new[] { "flatReduce", "flat", "flatDR", "damageReduceFlat" }, 0);

            blockCrit |= b;
            pct = Mathf.Clamp01(pct + Mathf.Clamp01(p));
            flat += Mathf.Max(0, f);

            // method fallback for damage filter object
            var core = GetCoreObject(t, "DamageFilter");
            if (core != null)
            {
                // If core has method returning a struct/class with matching fields, try to read it.
                object boxed = InvokeObject(core, new[]
                {
                    new InvokeSpec("GetFilter", new object[] { monsterId, def, level }),
                    new InvokeSpec("Evaluate", new object[] { monsterId, def, level }),
                });

                if (boxed != null)
                {
                    blockCrit |= ReadBool(boxed, new[] { "cannotBeCrit", "blockCrit", "noCrit" });
                    pct = Mathf.Clamp01(pct + Mathf.Clamp01(ReadFloat(boxed, new[] { "percentReduce", "percent", "pctReduce" }, 0f)));
                    flat += Mathf.Max(0, ReadInt(boxed, new[] { "flatReduce", "flat", "flatDR" }, 0));
                }
            }
        }

        return new TitleDamageFilter
        {
            cannotBeCrit = blockCrit,
            percentReduce = Mathf.Clamp01(pct),
            flatReduce = Mathf.Max(0, flat)
        };
    }

    public float GetcreditMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        if (string.IsNullOrEmpty(monsterId)) return 1f;

        float mul = 1f;
        var titles = GetTitlesForMonster(monsterId);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (!HasCoreType(t, "CoinBonusOnVictory")) continue;

            float directMult = ReadFloatFromCore(t, new[] { "multiplier", "mult", "coinMult", "creditMult" }, float.NaN);
            if (!float.IsNaN(directMult) && !float.IsInfinity(directMult) && directMult > 0f) { mul *= directMult; continue; }

            float pct = ReadFloatFromCore(t, new[] { "pct", "percent", "coinPct", "creditPct" }, 0f);
            if (Mathf.Abs(pct) > 0.0001f) { mul *= (1f + pct); continue; }

            float m2 = InvokeFloatOnCore(t, new[]
            {
                new InvokeSpec("GetMultiplier", new object[] { monsterId, wild, wildLevel }),
                new InvokeSpec("Evaluate", new object[] { monsterId, wild, wildLevel }),
            }, fallback: 1f);

            if (!float.IsNaN(m2) && !float.IsInfinity(m2) && m2 > 0f) mul *= m2;
        }

        return Mathf.Max(0f, mul);
    }

    public float GetGrowthCoreMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        if (string.IsNullOrEmpty(monsterId)) return 1f;

        float mul = 1f;
        var titles = GetTitlesForMonster(monsterId);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (!HasCoreType(t, "GrowthCoreBonusOnVictory")) continue;

            float directMult = ReadFloatFromCore(t, new[] { "multiplier", "mult", "growthMult", "coreMult" }, float.NaN);
            if (!float.IsNaN(directMult) && !float.IsInfinity(directMult) && directMult > 0f) { mul *= directMult; continue; }

            float pct = ReadFloatFromCore(t, new[] { "pct", "percent", "growthPct", "corePct" }, 0f);
            if (Mathf.Abs(pct) > 0.0001f) { mul *= (1f + pct); continue; }

            float m2 = InvokeFloatOnCore(t, new[]
            {
                new InvokeSpec("GetMultiplier", new object[] { monsterId, wild, wildLevel }),
                new InvokeSpec("Evaluate", new object[] { monsterId, wild, wildLevel }),
            }, fallback: 1f);

            if (!float.IsNaN(m2) && !float.IsInfinity(m2) && m2 > 0f) mul *= m2;
        }

        return Mathf.Max(0f, mul);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers: title lookup, core routing, SaveManager reflection
    // ─────────────────────────────────────────────────────────────────────

    private void TryAddTitle(string titleId, List<TitleSO> dst)
    {
        if (dst == null || string.IsNullOrEmpty(titleId)) return;
        if (_idToTitle.TryGetValue(titleId, out var t) && t != null)
            dst.Add(t);
    }

    private Sprite TryGetIconFromTitle(TitleSO t)
    {
        if (!t) return null;
        // Common names: icon, sprite, badgeIcon
        var spr = ReadSprite(t, new[] { "icon", "sprite", "badgeIcon" });
        return spr;
    }

    private static bool IsStat(string a, string b0)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b0)) return false;
        return string.Equals(a, b0, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStat(string a, params string[] any)
    {
        if (string.IsNullOrEmpty(a) || any == null) return false;
        for (int i = 0; i < any.Length; i++)
        {
            if (string.IsNullOrEmpty(any[i])) continue;
            if (string.Equals(a, any[i], StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool CoreAllowsContext(TitleSO title, TitleContext ctx)
    {
        // If core exposes a context field, respect it; otherwise allow all.
        // Common: context, allowedContext, titleContext
        try
        {
            object core = GetAnyCoreObject(title);
            if (core == null) return true;

            // If it has a TitleContext field/property, require match unless it's "Any"/default(0)
            var v = ReadEnum(core, new[] { "context", "allowedContext", "titleContext" });
            if (v == null) return true;

            if (v is TitleContext c)
            {
                // If you have a TitleContext.Any, this will naturally match.
                if (Enum.IsDefined(typeof(TitleContext), c))
                {
                    if (c.Equals(default(TitleContext))) return true;
                    return c.Equals(ctx);
                }
            }
        }
        catch { }
        return true;
    }

    private static bool CoreAllowsJobSite(TitleSO title, JobType site)
    {
        // If core exposes job/site gating, respect it; else allow.
        try
        {
            object core = GetAnyCoreObject(title);
            if (core == null) return true;

            // Common: job, jobType, site, siteType, allowedJob
            var v = ReadEnum(core, new[] { "job", "jobType", "site", "siteType", "allowedJob" });
            if (v == null) return true;

            if (v is JobType jt)
            {
                // Treat default(JobType) as "any" if your enum starts at 0 as None/Any.
                if (jt.Equals(default(JobType))) return true;
                return jt.Equals(site);
            }
        }
        catch { }
        return true;
    }

    private static bool HasAnyCoreType(TitleSO title, params string[] contains)
    {
        if (!title || contains == null || contains.Length == 0) return false;
        for (int i = 0; i < contains.Length; i++)
            if (HasCoreType(title, contains[i])) return true;
        return false;
    }

    private static bool HasCoreType(TitleSO title, string contains)
    {
        if (!title || string.IsNullOrEmpty(contains)) return false;
        var core = GetCoreObject(title, contains);
        return core != null;
    }

    private static object GetCoreObject(TitleSO title, string containsTypeName)
    {
        if (!title) return null;

        // TitleSO usually holds a reference to one "core" ScriptableObject.
        // We scan all fields/properties and return the first ScriptableObject whose type name contains the keyword.
        var so = title as UnityEngine.Object;
        var t = title.GetType();

        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // fields
        var fields = t.GetFields(BF);
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            if (f == null) continue;
            if (!typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)) continue;

            var v = f.GetValue(title) as UnityEngine.Object;
            if (!v) continue;

            var name = v.GetType().Name;
            if (name.IndexOf(containsTypeName, StringComparison.OrdinalIgnoreCase) >= 0)
                return v;
        }

        // properties
        var props = t.GetProperties(BF);
        for (int i = 0; i < props.Length; i++)
        {
            var p = props[i];
            if (p == null || !p.CanRead) continue;
            if (!typeof(UnityEngine.Object).IsAssignableFrom(p.PropertyType)) continue;

            UnityEngine.Object v = null;
            try { v = p.GetValue(title, null) as UnityEngine.Object; } catch { }
            if (!v) continue;

            var name = v.GetType().Name;
            if (name.IndexOf(containsTypeName, StringComparison.OrdinalIgnoreCase) >= 0)
                return v;
        }

        return null;
    }

    private static object GetAnyCoreObject(TitleSO title)
    {
        if (!title) return null;

        // Prefer a field/property literally named "core" or "effect"
        var t = title.GetType();
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var n in new[] { "core", "effect", "runtime", "titleCore" })
        {
            var f = t.GetField(n, BF);
            if (f != null && typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
            {
                var v = f.GetValue(title) as UnityEngine.Object;
                if (v) return v;
            }

            var p = t.GetProperty(n, BF);
            if (p != null && p.CanRead && typeof(UnityEngine.Object).IsAssignableFrom(p.PropertyType))
            {
                try
                {
                    var v = p.GetValue(title, null) as UnityEngine.Object;
                    if (v) return v;
                }
                catch { }
            }
        }

        // Otherwise: first ScriptableObject reference found
        var fields = t.GetFields(BF);
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            if (f == null) continue;
            if (!typeof(ScriptableObject).IsAssignableFrom(f.FieldType)) continue;

            var v = f.GetValue(title) as ScriptableObject;
            if (v) return v;
        }

        return null;
    }

    private int ReadIntFromCore(TitleSO title, string[] names, int fallback)
    {
        var core = GetAnyCoreObject(title);
        if (core == null) return fallback;
        return ReadInt(core, names, fallback);
    }

    private float ReadFloatFromCore(TitleSO title, string[] names, float fallback)
    {
        var core = GetAnyCoreObject(title);
        if (core == null) return fallback;
        return ReadFloat(core, names, fallback);
    }

    private bool ReadBoolFromCore(TitleSO title, string[] names, bool fallback)
    {
        var core = GetAnyCoreObject(title);
        if (core == null) return fallback;
        return ReadBool(core, names, fallback);
    }

    private string ReadStringFromCore(TitleSO title, string[] names)
    {
        var core = GetAnyCoreObject(title);
        if (core == null) return null;
        return ReadString(core, names);
    }

    // ─────────────────────────────────────────────────────────────────────
    // SaveManager integration (reflection-safe)
    // ─────────────────────────────────────────────────────────────────────

    private static object FindOwnedMonsterById(string ownedMonsterId)
    {
        if (string.IsNullOrEmpty(ownedMonsterId)) return null;

        var data = SaveManager.Data;
        if (data == null) return null;

        // Common: Data.owned is a List<OwnedMonster>
        var ownedListObj = ReadObject(data, new[] { "owned", "Owned", "ownedMonsters" });
        if (ownedListObj is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item == null) continue;
                var mid = ReadString(item, new[] { "monsterId", "id", "ownedId" });
                if (string.Equals(mid, ownedMonsterId, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
        }

        // Some projects keep team entries with monsterId + title info
        var teamObj = ReadObject(data, new[] { "team", "Team", "activeTeam" });
        if (teamObj is System.Collections.IEnumerable enumerable2)
        {
            foreach (var item in enumerable2)
            {
                if (item == null) continue;
                var mid = ReadString(item, new[] { "monsterId", "id", "ownedId" });
                if (string.Equals(mid, ownedMonsterId, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
        }

        return null;
    }

    private static MonsterDataSO FindMonsterDefById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        // Best-effort: Resources lookup by scanning all MonsterDataSO (if you store in Resources)
        // If you have a better library (MonsterLibrarySO), you can swap this later.
        var all = Resources.LoadAll<MonsterDataSO>("");
        for (int i = 0; i < all.Length; i++)
        {
            var d = all[i];
            if (!d) continue;
            if (string.Equals(d.id, id, StringComparison.OrdinalIgnoreCase))
                return d;
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Reflection helpers (generic)
    // ─────────────────────────────────────────────────────────────────────

    private readonly struct InvokeSpec
    {
        public readonly string method;
        public readonly object[] args;
        public InvokeSpec(string m, object[] a) { method = m; args = a; }
    }

    private static float InvokeFloatOnCore(TitleSO title, InvokeSpec[] calls, float fallback)
    {
        var core = GetAnyCoreObject(title);
        if (core == null) return fallback;

        for (int i = 0; i < calls.Length; i++)
        {
            var o = InvokeObject(core, new[] { calls[i] });
            if (o is float f) return f;
            if (o is int ii) return ii;
            if (o is double dd) return (float)dd;
        }

        return fallback;
    }

    private static object InvokeObject(object target, InvokeSpec[] calls)
    {
        if (target == null || calls == null) return null;

        var t = target.GetType();
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < calls.Length; i++)
        {
            var c = calls[i];
            if (string.IsNullOrEmpty(c.method)) continue;

            try
            {
                var mi = t.GetMethod(c.method, BF);
                if (mi == null) continue;

                return mi.Invoke(target, c.args);
            }
            catch { }
        }

        return null;
    }

    private static object ReadObject(object obj, string[] names)
    {
        if (obj == null || names == null) return null;
        var t = obj.GetType();
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < names.Length; i++)
        {
            var n = names[i];
            if (string.IsNullOrEmpty(n)) continue;

            var f = t.GetField(n, BF);
            if (f != null) { try { return f.GetValue(obj); } catch { } }

            var p = t.GetProperty(n, BF);
            if (p != null && p.CanRead) { try { return p.GetValue(obj, null); } catch { } }
        }
        return null;
    }

    private static string ReadString(object obj, string[] names)
    {
        if (obj == null || names == null) return null;
        var t = obj.GetType();
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < names.Length; i++)
        {
            var n = names[i];
            if (string.IsNullOrEmpty(n)) continue;

            var f = t.GetField(n, BF);
            if (f != null && f.FieldType == typeof(string)) { try { return (string)f.GetValue(obj); } catch { } }

            var p = t.GetProperty(n, BF);
            if (p != null && p.CanRead && p.PropertyType == typeof(string)) { try { return (string)p.GetValue(obj, null); } catch { } }
        }
        return null;
    }

    private static List<string> ReadStringList(object obj, string[] names)
    {
        if (obj == null || names == null) return null;

        var raw = ReadObject(obj, names);
        if (raw == null) return null;

        if (raw is List<string> ls) return ls;

        if (raw is System.Collections.IEnumerable enumerable)
        {
            var outList = new List<string>();
            foreach (var it in enumerable)
            {
                if (it is string s && !string.IsNullOrEmpty(s))
                    outList.Add(s);
            }
            return outList;
        }

        return null;
    }

    private static Sprite ReadSprite(object obj, string[] names)
    {
        if (obj == null || names == null) return null;
        var t = obj.GetType();
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < names.Length; i++)
        {
            var n = names[i];
            if (string.IsNullOrEmpty(n)) continue;

            var f = t.GetField(n, BF);
            if (f != null && typeof(Sprite).IsAssignableFrom(f.FieldType))
            {
                try { return f.GetValue(obj) as Sprite; } catch { }
            }

            var p = t.GetProperty(n, BF);
            if (p != null && p.CanRead && typeof(Sprite).IsAssignableFrom(p.PropertyType))
            {
                try { return p.GetValue(obj, null) as Sprite; } catch { }
            }
        }
        return null;
    }

    private static int ReadInt(object obj, string[] names, int fallback)
    {
        if (obj == null || names == null) return fallback;
        var t = obj.GetType();
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < names.Length; i++)
        {
            var n = names[i];
            if (string.IsNullOrEmpty(n)) continue;

            var f = t.GetField(n, BF);
            if (f != null)
            {
                try
                {
                    if (f.FieldType == typeof(int)) return (int)f.GetValue(obj);
                    if (f.FieldType == typeof(float)) return Mathf.RoundToInt((float)f.GetValue(obj));
                }
                catch { }
            }

            var p = t.GetProperty(n, BF);
            if (p != null && p.CanRead)
            {
                try
                {
                    if (p.PropertyType == typeof(int)) return (int)p.GetValue(obj, null);
                    if (p.PropertyType == typeof(float)) return Mathf.RoundToInt((float)p.GetValue(obj, null));
                }
                catch { }
            }
        }

        return fallback;
    }

    private static float ReadFloat(object obj, string[] names, float fallback)
    {
        if (obj == null || names == null) return fallback;
        var t = obj.GetType();
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < names.Length; i++)
        {
            var n = names[i];
            if (string.IsNullOrEmpty(n)) continue;

            var f = t.GetField(n, BF);
            if (f != null)
            {
                try
                {
                    if (f.FieldType == typeof(float)) return (float)f.GetValue(obj);
                    if (f.FieldType == typeof(int)) return (int)f.GetValue(obj);
                    if (f.FieldType == typeof(double)) return (float)(double)f.GetValue(obj);
                }
                catch { }
            }

            var p = t.GetProperty(n, BF);
            if (p != null && p.CanRead)
            {
                try
                {
                    if (p.PropertyType == typeof(float)) return (float)p.GetValue(obj, null);
                    if (p.PropertyType == typeof(int)) return (int)p.GetValue(obj, null);
                    if (p.PropertyType == typeof(double)) return (float)(double)p.GetValue(obj, null);
                }
                catch { }
            }
        }

        return fallback;
    }

    private static bool ReadBool(object obj, string[] names, bool fallback = false)
    {
        if (obj == null || names == null) return fallback;
        var t = obj.GetType();
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < names.Length; i++)
        {
            var n = names[i];
            if (string.IsNullOrEmpty(n)) continue;

            var f = t.GetField(n, BF);
            if (f != null && f.FieldType == typeof(bool))
            {
                try { return (bool)f.GetValue(obj); } catch { }
            }

            var p = t.GetProperty(n, BF);
            if (p != null && p.CanRead && p.PropertyType == typeof(bool))
            {
                try { return (bool)p.GetValue(obj, null); } catch { }
            }
        }

        return fallback;
    }

    private static object ReadEnum(object obj, string[] names)
    {
        if (obj == null || names == null) return null;

        var t = obj.GetType();
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < names.Length; i++)
        {
            var n = names[i];
            if (string.IsNullOrEmpty(n)) continue;

            var f = t.GetField(n, BF);
            if (f != null && f.FieldType.IsEnum)
            {
                try { return f.GetValue(obj); } catch { }
            }

            var p = t.GetProperty(n, BF);
            if (p != null && p.CanRead && p.PropertyType.IsEnum)
            {
                try { return p.GetValue(obj, null); } catch { }
            }
        }

        return null;
    }
}
