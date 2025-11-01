using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TitleManager : MonoBehaviour
{
    public static TitleManager I { get; private set; }

    [Header("Library / Lookup")]
    [Tooltip("If null, manager will scan Resources for all TitleSO assets on Awake.")]
    [SerializeField] private List<TitleSO> preloadTitles = new List<TitleSO>();

    // id -> TitleSO
    private readonly Dictionary<string, TitleSO> _idToTitle = new Dictionary<string, TitleSO>();

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
    // Evaluation helpers
    // ─────────────────────────────────────────────────────────────────────

    // Single/Conditional/Dual stat application
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
            else if (t is DualStatBoosterTitleSO dsb && dsb.enabled)
            {
                if (dsb.statA == stat) current = TitleUtility.ApplyOp(current, dsb.opA, dsb.valueA);
                if (dsb.statB == stat) current = TitleUtility.ApplyOp(current, dsb.opB, dsb.valueB);
            }
            else if (t is DualConditionalBoosterTitleSO dcb)
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
        return current;
    }

    public float GetEffectivenessMultiplier(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float mul = 1f;
        for (int i = 0; i < titles.Count; i++)
            if (titles[i] is EffectivenessModTitleSO em) mul *= Mathf.Max(0f, em.effectivenessMultiplier);
        return mul;
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
    public float GetJobFatigueMultiplier(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float mul = 1f;
        for (int i = 0; i < titles.Count; i++)
            if (titles[i] is JobFatigueBoosterTitleSO jb) mul *= Mathf.Max(0f, jb.fatigueMultiplier);
        return mul;
    }

    // 4-arg overload (kept for API parity with adapter)
    public float GetJobFatigueMultiplier(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        // If you later want site-specific fatigue tuning, use `site` here.
        return GetJobFatigueMultiplier(monsterId, def, level);
    }

    public float GetJobAuraPercent(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float sum = 0f;
        for (int i = 0; i < titles.Count; i++)
            if (titles[i] is JobAuraTitleSO ja)
            {
                // If you later add per-site restriction to aura, check `site` here.
                sum += Mathf.Max(0f, ja.siteAuraPercent);
            }
        return sum;
    }

    public int GetJobCapacityBonusFlat(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        var titles = GetEquippedList(monsterId, def, level);
        int sum = 0;
        for (int i = 0; i < titles.Count; i++)
            if (titles[i] is JobCapacityBoosterTitleSO jc) sum += Mathf.Max(0, jc.capacityBonusFlat);
        return sum;
    }

    public float GetJobRateMult(string monsterId, JobType site)
    {
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
                // If jr.restrictTo != None, only apply when matching the site
                if (jr.restrictTo == JobType.None || jr.restrictTo == site)
                    mul *= Mathf.Max(0f, jr.rateMultiplier);
            }
            else if (TryReadFloat(t, out var v, "rateMultiplier", "jobRateMultiplier", "productionMultiplier", "jobProdMult"))
            {
                // Reflection path can't check restrictTo; prefer strong type where possible
                mul *= Mathf.Max(0f, v);
            }
        }
        return Mathf.Max(0f, mul);
    }

    // Router for adapter
    public float GetStatValueRouter(string monsterId, MonsterDataSO def, int level, string statKind, TitleContext ctx, float baseValue)
    {
        if (!Enum.TryParse<StatKind>(statKind, out var kind))
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
        // Titles can affect job auras, per-worker rate, fatigue, etc.
        GameEvents.JobGlobalModsChanged?.Invoke();
        // Also nudge any job UI that keys off this generic event
        GameEvents.OnJobsChanged?.Invoke();
    }


}
