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
        if (I != null && I != this) { Destroy(gameObject); return; }
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
            {
                // copy the list (avoid mutating the asset)
                result.Add(new List<TitleSO>(tier.unlockChoices ?? new List<TitleSO>()));
            }
            else
            {
                result.Add(new List<TitleSO>()); // locked tier (empty)
            }
        }
        return result;
    }

    public int GetTierCount(MonsterDataSO def) => def && def.titleTrack ? def.titleTrack.tiers?.Count ?? 0 : 0;

    // ─────────────────────────────────────────────────────────────────────
    // Public: Equip / Get Equipped
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Equip a title in a specific tier. Enforces maxSelectable (usually 1).</summary>
    public bool Equip(string monsterId, MonsterDataSO def, int tierIndex, TitleSO choose)
    {
        if (string.IsNullOrEmpty(monsterId) || !def || !def.titleTrack) return false;
        var tiers = def.titleTrack.tiers;
        if (tiers == null || tierIndex < 0 || tierIndex >= tiers.Count) return false;
        if (choose == null) return false;

        // Make sure it's part of that tier's options
        var tier = tiers[tierIndex];
        if (tier.unlockChoices == null || !tier.unlockChoices.Contains(choose)) return false;

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);

        // Only ONE active title total — clear all previous selections
        save.tierSelections.Clear();

        // Resize to total tier count
        for (int i = 0; i < tiers.Count; i++)
            save.tierSelections.Add("");

        // Assign the new one
        save.tierSelections[tierIndex] = choose.titleId;

        TitleSaveStore.Save();
        return true;
    }


    /// <summary>Clear selection for a given tier.</summary>
    public bool Unequip(string monsterId, MonsterDataSO def, int tierIndex)
    {
        if (string.IsNullOrEmpty(monsterId) || !def || !def.titleTrack) return false;
        var tiers = def.titleTrack.tiers;
        if (tiers == null || tierIndex < 0 || tierIndex >= tiers.Count) return false;

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);
        while (save.tierSelections.Count < tiers.Count) save.tierSelections.Add("");

        save.tierSelections[tierIndex] = "";
        TitleSaveStore.Save();
        return true;
    }


    /// <summary>Returns the equipped TitleSO for each unlocked tier (or null/empty if none).</summary>
    public List<TitleSO> GetEquippedList(string monsterId, MonsterDataSO def, int level)
    {
        var res = new List<TitleSO>();
        if (string.IsNullOrEmpty(monsterId) || !def || !def.titleTrack)
        {
            // still include Always-On titles if present
            if (def && def.defaultAlwaysOnTitles != null)
                res.AddRange(def.defaultAlwaysOnTitles);
            return res;
        }

        var tiers = def.titleTrack.tiers;
        if (tiers == null)
        {
            if (def.defaultAlwaysOnTitles != null) res.AddRange(def.defaultAlwaysOnTitles);
            return res;
        }

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);

        // Always-on first
        if (def.defaultAlwaysOnTitles != null)
            res.AddRange(def.defaultAlwaysOnTitles);

        // Then add equipped per tier (if unlocked by level)
        for (int i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            if (level < Mathf.Max(1, tier.levelRequired)) { res.Add(null); continue; }

            string tid = (i < save.tierSelections.Count) ? save.tierSelections[i] : "";
            if (!string.IsNullOrEmpty(tid) && _idToTitle.TryGetValue(tid, out var t))
                res.Add(t);
            else
                res.Add(null);
        }
        return res;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public: Evaluation helpers (to call from Battle/Jobs)
    // These read the monster's active titles (always-on + equipped) and
    // compute aggregate effects for the requested category.
    // ─────────────────────────────────────────────────────────────────────

    // --- Stat aggregation (flat or % depending on Op) ---
    public float GetStatValue(string monsterId, MonsterDataSO def, int level, StatKind stat, in TitleContext ctx, float baseValue)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float current = baseValue;

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (t is StatBoosterTitleSO sb && sb.stat == stat)
            {
                current = TitleUtility.ApplyOp(current, sb.operation, sb.value);
            }
            else if (t is ConditionalBoosterTitleSO cb && cb.stat == stat)
            {
                if (TitleUtility.CheckCondition(cb, ctx))
                    current = TitleUtility.ApplyOp(current, cb.operation, cb.value);
            }
        }
        return current;
    }

    // --- Effectiveness mod (multiply your type chart result) ---
    public float GetEffectivenessMultiplier(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float mul = 1f;
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as EffectivenessModTitleSO;
            if (t) mul *= Mathf.Max(0f, t.effectivenessMultiplier);
        }
        return mul;
    }

    // --- Damage filter (incoming) ---
    public struct DamageFilter
    {
        public int flatReduce;         // subtract after defense
        public float percentMultiplier;// multiply after flat
        public bool cannotBeCrit;      // true => negate crits
    }

    public DamageFilter GetDamageFilterFor(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        var f = new DamageFilter { flatReduce = 0, percentMultiplier = 1f, cannotBeCrit = false };

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as DamageFilterTitleSO;
            if (!t) continue;

            f.flatReduce += Mathf.Max(0, t.flatReduce);
            f.percentMultiplier *= Mathf.Max(0f, t.percentMultiplier);
            if (t.cannotBeCrit) f.cannotBeCrit = true;
        }
        return f;
    }

    // --- Job boosters (while assigned) ---
    public float GetJobFatigueMultiplier(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float mul = 1f;
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as JobFatigueBoosterTitleSO;
            if (t) mul *= Mathf.Max(0f, t.fatigueMultiplier);
        }
        return mul;
    }

    public float GetJobAuraPercent(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float sum = 0f;
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as JobAuraTitleSO;
            if (t) sum += Mathf.Max(0f, t.siteAuraPercent);
        }
        return sum;
    }

    public int GetJobCapacityBonusFlat(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        int sum = 0;
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as JobCapacityBoosterTitleSO;
            if (t) sum += Mathf.Max(0, t.capacityBonusFlat);
        }
        return sum;
    }

    
}
