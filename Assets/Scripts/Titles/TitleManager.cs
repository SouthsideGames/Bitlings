using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public sealed class TitleManager : MonoBehaviour
{
    public static TitleManager I { get; private set; }

    [Header("Library / Lookup")]
    [Tooltip("If null, manager will scan Resources for all TitleSO assets on Awake.")]
    [SerializeField] private List<TitleSO> preloadTitles = new List<TitleSO>();

    [Header("Debug")]
    [SerializeField] private bool debugEffectiveness = false;

    // id -> TitleSO
    private readonly Dictionary<string, TitleSO> _idToTitle = new Dictionary<string, TitleSO>();

    // ─────────────────────────────────────────────────────────────────────
    // Per-battle state (TurnBooster / EventStacks / BattleStart)
    // ─────────────────────────────────────────────────────────────────────
    // NOTE: one-simple-bucket-per-monster; expand to keyed tuples if you add multiple variants per monster.
    private readonly Dictionary<string, int>   _turnStacks          = new();   // grows on OnTurnAdvanced up to max (TurnBooster)
    private readonly Dictionary<string, int>   _eventStacks         = new();   // grows on triggers (EventStacks)
    private readonly Dictionary<string, int>   _eventMax            = new();   // cache max for UI/debug (optional)
    private readonly Dictionary<string, int>   _eventDecayPerTurn   = new();   // how many stacks to decay each turn
    private readonly Dictionary<string, int>   _flatStartUntilTurn  = new();   // inclusive last turn index where flat buff applies
    private readonly Dictionary<string, int>   _flatStartAmountAtk  = new();   // flat ATK from BattleStartFlatTitleSO (expand if you add other stats)
    private readonly Dictionary<string, float> _shieldRemaining     = new();   // BattleStartShieldTitleSO: remaining shield HP
    private int _turnIndex;

    // ─────────────────────────────────────────────────────────────────────
    // Unity
    // ─────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        I = this;
        BuildIndex();
    }

    private void BuildIndex()
    {
        _idToTitle.Clear();

        // 1) Include any preloaded titles (drag & drop in inspector)
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

        // 2) Also scan Resources for TitleSO
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
    // Public: Query available titles for a monster (by level & track)
    // ─────────────────────────────────────────────────────────────────────
    public List<List<TitleSO>> GetAvailableByTier(MonsterDataSO def, int level)
    {
        var result = new List<List<TitleSO>>();
        if (!def || !def.titleTrack) return result;

        var tiers = def.titleTrack.tiers;
        if (tiers == null) return result;

        for (int i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            if (level >= Mathf.Max(1, tier.levelRequired))
                result.Add(new List<TitleSO>(tier.unlockChoices ?? new List<TitleSO>()));
            else
                result.Add(new List<TitleSO>()); // locked
        }
        return result;
    }

    public int GetTierCount(MonsterDataSO def) => def && def.titleTrack ? def.titleTrack.tiers?.Count ?? 0 : 0;

    // Convenience for UI
    public string GetEquippedTitleIdForTier(string monsterId, MonsterDataSO def, int tierIndex)
    {
        if (string.IsNullOrEmpty(monsterId) || !def || !def.titleTrack) return "";
        var tiers = def.titleTrack.tiers;
        if (tiers == null || tierIndex < 0 || tierIndex >= tiers.Count) return "";

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);
        if (tierIndex >= save.tierSelections.Count) return "";
        return save.tierSelections[tierIndex];
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public: Equip / Get Equipped
    // (Fires JobGlobalModsChanged for UI/logic that depends on titles)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Equip a title in a specific tier. Enforces maxSelectable (currently 1 total).</summary>
    public bool Equip(string monsterId, MonsterDataSO def, int tierIndex, TitleSO choose)
    {
        if (string.IsNullOrEmpty(monsterId) || !def || !def.titleTrack) return false;
        var tiers = def.titleTrack.tiers;
        if (tiers == null || tierIndex < 0 || tierIndex >= tiers.Count) return false;
        if (!choose) return false;

        // Must be among that tier's choices
        var tier = tiers[tierIndex];
        if (tier.unlockChoices == null || !tier.unlockChoices.Contains(choose)) return false;

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);

        // Only ONE active title total — clear prior picks
        save.tierSelections.Clear();

        // Resize to tiers
        for (int i = 0; i < tiers.Count; i++) save.tierSelections.Add("");

        // If already the same selection, no-op (but still ensure list length)
        bool changed = save.tierSelections[tierIndex] != choose.titleId;
        save.tierSelections[tierIndex] = choose.titleId;

        TitleSaveStore.Save();
        if (changed) RaiseTitleChange();
        return true;
    }

    public bool EquipById(string monsterId, MonsterDataSO def, int tierIndex, string titleId)
    {
        if (string.IsNullOrEmpty(titleId)) return false;
        if (!_idToTitle.TryGetValue(titleId, out var so) || !so) return false;
        return Equip(monsterId, def, tierIndex, so);
    }

    public bool Unequip(string monsterId, MonsterDataSO def, int tierIndex)
    {
        if (string.IsNullOrEmpty(monsterId) || !def || !def.titleTrack) return false;
        var tiers = def.titleTrack.tiers;
        if (tiers == null || tierIndex < 0 || tierIndex >= tiers.Count) return false;

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);
        while (save.tierSelections.Count < tiers.Count) save.tierSelections.Add("");

        bool changed = !string.IsNullOrEmpty(save.tierSelections[tierIndex]);
        save.tierSelections[tierIndex] = "";

        TitleSaveStore.Save();
        if (changed) RaiseTitleChange();
        return true;
    }

    /// <summary>Returns always-on titles + equipped (per unlocked tier). Null for locked/no pick.</summary>
    public List<TitleSO> GetEquippedList(string monsterId, MonsterDataSO def, int level)
    {
        var res = new List<TitleSO>();
        if (string.IsNullOrEmpty(monsterId) || !def || !def.titleTrack)
        {
            if (def && def.defaultAlwaysOnTitles != null) res.AddRange(def.defaultAlwaysOnTitles);
            return res;
        }

        var tiers = def.titleTrack.tiers;
        if (tiers == null)
        {
            if (def.defaultAlwaysOnTitles != null) res.AddRange(def.defaultAlwaysOnTitles);
            return res;
        }

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);

        if (def.defaultAlwaysOnTitles != null) res.AddRange(def.defaultAlwaysOnTitles);

        for (int i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            if (level < Mathf.Max(1, tier.levelRequired)) { res.Add(null); continue; }

            string tid = (i < save.tierSelections.Count) ? save.tierSelections[i] : "";
            if (!string.IsNullOrEmpty(tid) && _idToTitle.TryGetValue(tid, out var t)) res.Add(t);
            else res.Add(null);
        }
        return res;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Adapter-visible: return monster’s equipped titles
    // ─────────────────────────────────────────────────────────────────────
    // Called by TitlesAdapter via reflection: GetTitlesForMonster(string)
    public List<TitleSO> GetTitlesForMonster(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return new List<TitleSO>();
        var def = MonsterLibraryLocator.GetById(monsterId);
        int lvl = GetLevelOr1(monsterId);
        return GetEquippedList(monsterId, def, lvl);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Evaluation helpers
    // ─────────────────────────────────────────────────────────────────────

    // Single/Conditional/Dual stat application + NEW boosters
    public float GetStatValue(string monsterId, MonsterDataSO def, int level, StatKind stat, in TitleContext ctx, float baseValue)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float current = baseValue;

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];

            if (t is StatBoosterTitleSO sb && sb.stat == stat)
                current = TitleUtility.ApplyOp(current, sb.operation, sb.value);

            else if (t is ConditionalStatBoosterTitleSO cb && cb.stat == stat)
            {
                if (TitleUtility.CheckCondition(cb, ctx))
                    current = TitleUtility.ApplyOp(current, cb.operation, cb.value);
            }
            else if (t is DuoStatBoosterTitleSO dsb && dsb.enabled)
            {
                if (dsb.statA == stat) current = TitleUtility.ApplyOp(current, dsb.opA, dsb.valueA);
                if (dsb.statB == stat) current = TitleUtility.ApplyOp(current, dsb.opB, dsb.valueB);
            }
            else if (t is DuoConditionalStatBoosterTitleSO dcb)
            {
                if (TitleUtility.CheckCondition(dcb.condition, dcb.threshold01, dcb.countN, in ctx))
                {
                    if (dcb.statA == stat)
                        current = TitleUtility.ApplyOp(current, dcb.opA, dcb.valueA);
                    if (dcb.statB == stat)
                        current = TitleUtility.ApplyOp(current, dcb.opB, dcb.valueB);
                }
            }
        }

        // ── NEW: BattleStartFlatTitleSO (ATK-only in this simple pass; expand as needed)
        if (stat == StatKind.Attack && _flatStartAmountAtk.TryGetValue(monsterId, out int flat)
            && _flatStartUntilTurn.TryGetValue(monsterId, out int untilTurn)
            && _turnIndex <= untilTurn)
        {
            current += flat;
        }

        // ── NEW: TurnBoosterTitleSO (percent per turn up to max stacks)
        var tb = GetFirstTitle<TurnBoosterTitleSO>(monsterId, def, level);
        if (tb != null && MatchesStat(stat, tb.stat) && _turnStacks.TryGetValue(monsterId, out int tStacks) && tStacks > 0)
        {
            float pct = Mathf.Max(0f, tb.percentPerTurn) / 100f;
            current *= 1f + pct * Mathf.Min(tStacks, Mathf.Max(1, tb.maxStacks));
        }

        // ── NEW: EventStacksTitleSO (percent per stack; grows on triggers; optional decay)
        var es = GetFirstTitle<EventStacksTitleSO>(monsterId, def, level);
        if (es != null && MatchesStat(stat, es.stat) && _eventStacks.TryGetValue(monsterId, out int eStacks) && eStacks > 0)
        {
            float pct = Mathf.Max(0f, es.percentPerStack) / 100f;
            current *= 1f + pct * Mathf.Min(eStacks, Mathf.Max(1, es.maxStacks));
        }

        // ── NEW: ClutchBoosterTitleSO (threshold via ctx.hpPct)
        var clutch = GetFirstTitle<ClutchBoosterTitleSO>(monsterId, def, level);
        float hp01 = ReadHp01(ctx); // ← helper below
        if (clutch != null && hp01 <= Mathf.Clamp01(clutch.hpBelowThreshold01))
        {
            if (stat == StatKind.Attack && clutch.atkPct > 0f)   current *= (1f + clutch.atkPct);
            if (stat == StatKind.Defense && clutch.defPct > 0f)  current *= (1f + clutch.defPct);
            if (stat == StatKind.Speed && clutch.spdPct > 0f)    current *= (1f + clutch.spdPct);
        }

        return current;
    }

    public float GetEffectivenessMultiplier(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float mul = 1f;

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as EffectivenessModTitleSO;
            if (!t || t.mode != EffectivenessMode.Multiply) continue;

            float before = mul;
            mul *= Mathf.Max(0f, t.amount);

            if (debugEffectiveness)
            {
                string msg = $"[EffectivenessMod] {monsterId} MULT x{t.amount:0.00}: {before:0.00} → {mul:0.00}";
                TryBattleLog(msg);
            }
        }

        if (debugEffectiveness)
        {
            string summary = $"[EffectivenessMod] FINAL MULT for {monsterId} = x{mul:0.00}";
            TryBattleLog(summary);
        }

        return mul;
    }

    public float GetEffectivenessAdd(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float add = 0f;

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as EffectivenessModTitleSO;
            if (!t || t.mode != EffectivenessMode.Add) continue;
            add += t.amount;
        }

        if (debugEffectiveness && Mathf.Abs(add) > 0.0001f)
        {
            string msg = $"[EffectivenessMod] FINAL ADD for {monsterId} = +{add:0.00} effectiveness";
            TryBattleLog(msg);
        }

        return add;
    }

    // Adapter expects fields: cannotBeCrit, percentReduce, flatReduce
    public struct TitleDamageFilter
    {
        public bool cannotBeCrit;
        public float percentReduce;   // 0.20 = 20% less damage after DEF
        public int flatReduce;        // subtract after % reduce
    }

    public TitleDamageFilter GetDamageFilter(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        var f = new TitleDamageFilter { cannotBeCrit = false, percentReduce = 0f, flatReduce = 0 };

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as DamageFilterTitleSO;
            if (!t) continue;

            // Convert 1.0-baseline multiplier to a percent reduction amount.
            float reduceFromMult = Mathf.Clamp01(1f - Mathf.Max(0f, t.percentMultiplier));
            f.flatReduce += Mathf.Max(0, t.flatReduce);
            f.percentReduce = Mathf.Clamp01(f.percentReduce + reduceFromMult);
            if (t.cannotBeCrit) f.cannotBeCrit = true;
        }
        return f;
    }

    public object GetDamageFilterBoxed(string monsterId, MonsterDataSO def, int level) => GetDamageFilter(monsterId, def, level);

    // --- Job boosters (while assigned) ---
    // Updated to be site-aware when SO exposes target site or AppliesTo(site)
    public float GetJobFatigueMultiplier(string monsterId, MonsterDataSO def, int level)
    {
        // Legacy callsite support (no site provided) — treat as neutral
        var titles = GetEquippedList(monsterId, def, level);
        float mul = 1f;
        for (int i = 0; i < titles.Count; i++)
            if (titles[i] is JobFatigueBoosterTitleSO jb)
                mul *= Mathf.Max(0f, jb.fatigueMultiplier);
        return mul;
    }

    public float GetJobFatigueMultiplier(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float mul = 1f;
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as JobFatigueBoosterTitleSO;
            if (!t) continue;

            bool applies = false;
            try
            {
                // Preferred: explicit helper on SO
                var m = t.GetType().GetMethod("AppliesTo");
                if (m != null) applies = (bool)m.Invoke(t, new object[] { site });
                else
                {
                    // Fallback: match by field if present
                    var f = t.GetType().GetField("targetJobSite");
                    if (f != null)
                    {
                        var val = (JobType)f.GetValue(t);
                        applies = (val == JobType.None || val == site);
                    }
                    else applies = true; // if no field, treat as global
                }
            }
            catch { applies = true; }

            if (applies) mul *= Mathf.Max(0f, t.fatigueMultiplier);
        }
        return Mathf.Max(0f, mul);
    }

    public float GetJobAuraPercent(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float sum = 0f;
        for (int i = 0; i < titles.Count; i++)
        {
            var ja = titles[i] as JobAuraTitleSO;
            if (!ja) continue;

            bool applies = false;
            try
            {
                var m = ja.GetType().GetMethod("AppliesTo");
                if (m != null) applies = (bool)m.Invoke(ja, new object[] { site });
                else
                {
                    var f = ja.GetType().GetField("targetJobSite");
                    if (f != null)
                    {
                        var val = (JobType)f.GetValue(ja);
                        applies = (val == JobType.None || val == site);
                    }
                    else applies = true;
                }
            }
            catch { applies = true; }

            if (applies) sum += Mathf.Max(0f, ja is JobAuraTitleSO ? ja.siteAuraPercent : 0f);
        }
        return sum;
    }

    public int GetJobCapacityBonusFlat(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        var titles = GetEquippedList(monsterId, def, level);
        int sum = 0;
        for (int i = 0; i < titles.Count; i++)
        {
            var jc = titles[i] as JobCapacityBoosterTitleSO;
            if (!jc) continue;

            bool applies = false;
            try
            {
                var m = jc.GetType().GetMethod("AppliesTo");
                if (m != null) applies = (bool)m.Invoke(jc, new object[] { site });
                else
                {
                    var f = jc.GetType().GetField("targetJobSite");
                    if (f != null)
                    {
                        var val = (JobType)f.GetValue(jc);
                        applies = (val == JobType.None || val == site);
                    }
                    else applies = true;
                }
            }
            catch { applies = true; }

            if (applies) sum += Mathf.Max(0, jc.capacityBonusFlat);
        }
        return sum;
    }

    public float GetJobRateMult(string monsterId, JobType site)
    {
        // kept from your original (with site check for ConditionalJobRateBoosterTitleSO)
        var def = MonsterLibraryLocator.GetById(monsterId);
        int lvl = 1;
        var titles = GetEquippedList(monsterId, def, lvl);

        float mul = 1f;
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (t is ConditionalJobRateBoosterTitleSO jr)
            {
                if (jr.restrictTo == JobType.None || jr.restrictTo == site)
                    mul *= Mathf.Max(0f, jr.rateMultiplier);
            }
            else if (TryReadFloat(t, out var v, "rateMultiplier", "jobRateMultiplier", "productionMultiplier", "jobProdMult"))
            {
                mul *= Mathf.Max(0f, v);
            }
        }
        return Mathf.Max(0f, mul);
    }

    // Router for adapter
    public float GetStatValueRouter(string monsterId, MonsterDataSO def, int level, string statKind, TitleContext ctx, float baseValue)
    {
        var norm = NormalizeStatKey(statKind);
        if (!Enum.TryParse<StatKind>(norm, ignoreCase: true, out var kind))
            return baseValue;

        return GetStatValue(monsterId, def, level, kind, in ctx, baseValue);
    }

    // Small reflection helper (flexible field/property names)
    private static bool TryReadFloat(object obj, out float value, params string[] names)
    {
        value = 0f;
        if (obj == null || names == null) return false;
        var t = obj.GetType();

        for (int i = 0; i < names.Length; i++)
        {
            var f = t.GetField(names[i]);
            if (f != null)
            {
                try { value = Convert.ToSingle(f.GetValue(obj)); return true; } catch { }
            }
            var p = t.GetProperty(names[i]);
            if (p != null)
            {
                try { value = Convert.ToSingle(p.GetValue(obj, null)); return true; } catch { }
            }
        }
        return false;
    }

    public float GetIncomingEffectivenessMultiplier(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float mul = 1f;
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as EffectivenessNullifyTitleSO;
            if (t)
            {
                // Stack multiplicatively (so multiple resistances combine)
                mul *= Mathf.Max(0f, t.incomingEffectivenessMultiplier);
            }
        }
        return mul;
    }

    // ─────────────────────────────────────────────────────────────────────
    // UI-friendly wrappers that ALWAYS raise the event on change
    // ─────────────────────────────────────────────────────────────────────

    public bool AssignTitleToMonster(string monsterId, MonsterDataSO def, int tierIndex, TitleSO choose)
    {
        bool ok = Equip(monsterId, def, tierIndex, choose); // Equip already calls RaiseTitleChange if changed
        return ok;
    }

    public bool RemoveTitleFromMonster(string monsterId, MonsterDataSO def, int tierIndex)
    {
        bool ok = Unequip(monsterId, def, tierIndex); // Unequip already calls RaiseTitleChange if changed
        return ok;
    }

    public bool ToggleTitleOnMonster(string monsterId, MonsterDataSO def, int tierIndex, TitleSO choose)
    {
        if (string.IsNullOrEmpty(monsterId) || !def || choose == null) return false;
        string current = GetEquippedTitleIdForTier(monsterId, def, tierIndex);
        if (current == choose.titleId) return Unequip(monsterId, def, tierIndex);
        return Equip(monsterId, def, tierIndex, choose);
    }

    public void ClearAllFor(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return;

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);
        if (save.tierSelections == null || save.tierSelections.Count == 0) return;

        bool changed = false;
        for (int i = 0; i < save.tierSelections.Count; i++)
        {
            if (!string.IsNullOrEmpty(save.tierSelections[i]))
            {
                save.tierSelections[i] = "";
                changed = true;
            }
        }

        if (changed)
        {
            TitleSaveStore.Save();
            RaiseTitleChange();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Events: notify the game that title-driven job math changed
    // ─────────────────────────────────────────────────────────────────────
    private void RaiseTitleChange()
    {
        GameEvents.JobGlobalModsChanged?.Invoke();
        GameEvents.OnJobsChanged?.Invoke();
    }

    private static string NormalizeStatKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        switch (key.Trim().ToUpperInvariant())
        {
            case "ATK": return "Attack";
            case "DEF": return "Defense";
            case "SPD": return "Speed";
            case "HP":  return "HP";
            default:    return key; // "Attack","Defense","Speed" already fine
        }
    }

    public float GetCoinMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        float mul = 1f;

        var def = MonsterLibraryLocator.GetById(monsterId);
        int lvl = 1;

        var titles = GetEquippedList(monsterId, def, lvl);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (t is CoinBonusOnVictoryTitleSO coin)
            {
                mul *= Mathf.Max(0f, coin.coinMultiplier);
                continue;
            }

            if (TryReadFloat(t, out var v, "coinMultiplier", "coinsMultiplier", "rewardCoinMult"))
                mul *= Mathf.Max(0f, v);
        }

        return Mathf.Max(0f, mul);
    }

    public float GetGrowthCoreMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        float mul = 1f;

        var def = MonsterLibraryLocator.GetById(monsterId);
        int lvl = 1;

        var titles = GetEquippedList(monsterId, def, lvl);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (t is GrowthCoreBonusOnVictoryTitleSO gc)
            {
                mul *= Mathf.Max(0f, gc.growthCoreMultiplier);
                continue;
            }

            if (TryReadFloat(t, out var v, "growthCoreMultiplier", "coreMultiplier", "rewardGrowthMult"))
                mul *= Mathf.Max(0f, v);
        }

        return Mathf.Max(0f, mul);
    }

    // ─────────────────────────────────────────────────────────────────────
    // TitlesAdapter event relays (safe no-ops if unused)
    // ─────────────────────────────────────────────────────────────────────

    // Called by TitlesAdapter.OnBattleStart(...)
    public void OnBattleStart(string activeMonsterId, MonsterDataSO wild, int wildLevel)
    {
        _turnStacks.Clear();
        _eventStacks.Clear();
        _eventMax.Clear();
        _eventDecayPerTurn.Clear();
        _flatStartUntilTurn.Clear();
        _flatStartAmountAtk.Clear();
        _shieldRemaining.Clear();
        _turnIndex = 0;

        // Pre-bake start buffs for active monster (expand to allies if you want)
        if (!string.IsNullOrEmpty(activeMonsterId))
            ApplyBattleStartBonuses(activeMonsterId);
    }

    // Called by TitlesAdapter.OnBattleEnd(...)
    public void OnBattleEnd(string activeMonsterId, bool victory, MonsterDataSO wild, int wildLevel)
    {
        _turnStacks.Clear();
        _eventStacks.Clear();
        _eventMax.Clear();
        _eventDecayPerTurn.Clear();
        _flatStartUntilTurn.Clear();
        _flatStartAmountAtk.Clear();
        _shieldRemaining.Clear();
        _turnIndex = 0;
    }

    // Optional events you may fire from Encounter/Battle
    public void OnMonsterLeveled(string monsterId, int newLevel) { }
    public void OnMonsterCaptured(string monsterId, MonsterType type, int level, bool isShiny) { }
    public void OnMonsterEvolved(string newMonsterId) { }

    // Called by TitlesAdapter.OnTurnAdvanced(...)
    public void OnTurnAdvanced(int turnIndex)
    {
        _turnIndex = Mathf.Max(0, turnIndex);

        var keys = _eventStacks.Keys.ToArray();
        for (int i = 0; i < keys.Length; i++)
        {
            var id = keys[i];
            if (!_eventStacks.TryGetValue(id, out var cur)) continue;
            int decay = _eventDecayPerTurn.TryGetValue(id, out var d) ? d : 0;
            if (decay > 0)
                _eventStacks[id] = Mathf.Max(0, cur - decay);
        }

    }

    // Called by TitlesAdapter.OnAttackLanded(attackerId, wasCrit)
    public void OnAttackLanded(string attackerId, bool wasCrit)
    {
        if (string.IsNullOrEmpty(attackerId)) return;

        var def = MonsterLibraryLocator.GetById(attackerId);
        int lvl = GetLevelOr1(attackerId);
        var es = GetFirstTitle<EventStacksTitleSO>(attackerId, def, lvl);
        if (es == null) return;

        bool trigger = (es.trigger == EventTriggerKind.OnAttack) || (wasCrit && es.trigger == EventTriggerKind.OnCrit);
        if (!trigger) return;

        BumpEventStacks(attackerId, Mathf.Max(1, es.maxStacks), Mathf.Max(0, es.decayPerTurn));
    }

    // Called by TitlesAdapter.OnHitTaken(defenderId, damage, wasCrit)
    public void OnHitTaken(string defenderId, int damage, bool wasCrit)
    {
        if (string.IsNullOrEmpty(defenderId)) return;

        // Shield consumption (if present) — call this from your damage pipeline if you prefer
        if (_shieldRemaining.TryGetValue(defenderId, out float shield) && shield > 0f)
        {
            float used = Mathf.Min(shield, damage);
            shield -= used;
            _shieldRemaining[defenderId] = Mathf.Max(0f, shield);
            // If you want to actually reduce damage here, expose a helper that returns reduced value.
        }

        var def = MonsterLibraryLocator.GetById(defenderId);
        int lvl = GetLevelOr1(defenderId);
        var es = GetFirstTitle<EventStacksTitleSO>(defenderId, def, lvl);
        if (es == null) return;

        bool trigger = (es.trigger == EventTriggerKind.OnHitTaken) || (wasCrit && es.trigger == EventTriggerKind.OnCrit);
        if (!trigger) return;

        BumpEventStacks(defenderId, Mathf.Max(1, es.maxStacks), Mathf.Max(0, es.decayPerTurn));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private void ApplyBattleStartBonuses(string id)
    {
        var def = MonsterLibraryLocator.GetById(id);
        int lvl = GetLevelOr1(id);

        // Flat ATK start buff
        var flat = GetFirstTitle<BattleStartFlatTitleSO>(id, def, lvl);
        if (flat != null)
        {
            _flatStartAmountAtk[id] = Mathf.Max(0, flat.flatAmount);
            _flatStartUntilTurn[id] = Mathf.Max(1, flat.durationTurns <= 0 ? 1 : flat.durationTurns);
        }

        // Shield from MaxHP %
        var shield = GetFirstTitle<BattleStartShieldTitleSO>(id, def, lvl);
        if (shield != null)
        {
            float maxHP = Mathf.Max(1f, BattleCalc.CalcHP(def, Mathf.Max(1, lvl)));
            _shieldRemaining[id] = Mathf.Max(0f, maxHP * (Mathf.Max(0f, shield.shieldPct) / 100f));
        }
    }

    private void BumpEventStacks(string id, int maxStacks, int decayPerTurn)
    {
        _eventStacks.TryGetValue(id, out int cur);
        int next = Mathf.Min(cur + 1, maxStacks);
        _eventStacks[id] = next;
        _eventMax[id] = maxStacks;
        _eventDecayPerTurn[id] = Mathf.Max(0, decayPerTurn);
    }

    private static bool MatchesStat(StatKind stat, BattleStatKind bsk)
    {
        return (stat == StatKind.Attack && bsk == BattleStatKind.ATK)
            || (stat == StatKind.Defense && bsk == BattleStatKind.DEF)
            || (stat == StatKind.Speed && bsk == BattleStatKind.SPD)
            || (stat == StatKind.HP && bsk == BattleStatKind.HP);
    }

    private T GetFirstTitle<T>(string monsterId, MonsterDataSO def, int level) where T : TitleSO
    {
        var list = GetEquippedList(monsterId, def, level);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is T got) return got;
        }
        return null;
    }

    private static int GetLevelOr1(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return 1;

        var owned = SaveManager.Data?.owned;
        if (owned != null)
        {
            for (int i = 0; i < owned.Count; i++)
            {
                var om = owned[i];
                if (om != null && om.monsterId == monsterId)
                    return Mathf.Max(1, om.level);
            }
        }

        var team = SaveManager.Data?.team;
        if (team != null)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var e = team[i];
                if (e != null && e.monsterId == monsterId)
                    return Mathf.Max(1, e.level);
            }
        }

        return 1;
    }

    private static void TryBattleLog(string msg)
    {
        try { BattleLogger.Log(msg, LogScope.Battle); } catch { }
        Debug.Log(msg);
    }

    private static float ReadHp01(TitleContext ctx)
    {
        try
        {
            var t = ctx.GetType();

            // Try common field names
            var f = t.GetField("hpPct") ?? t.GetField("hp01") ?? t.GetField("hp") ?? t.GetField("health01");
            if (f != null)
                return Mathf.Clamp01(Convert.ToSingle(f.GetValue(ctx)));

            // Try common property names
            var p = t.GetProperty("hpPct") ?? t.GetProperty("hp01") ?? t.GetProperty("HP01") ?? t.GetProperty("Health01");
            if (p != null)
                return Mathf.Clamp01(Convert.ToSingle(p.GetValue(ctx, null)));
        }
        catch { /* fall through */ }

        // Default to full health if unavailable to avoid accidental always-on clutch
        return 1f;
    }

}
