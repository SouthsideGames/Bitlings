using System;
using UnityEngine;

/// <summary>
/// Central authority for monster leveling and training bonuses.
/// All level-ups and permanent stat training should go through here so we
/// always modify the canonical OwnedMonsterData stored in SaveManager.Data.
/// </summary>
public static class XPManager
{
    /// <summary>
    /// Internal helper: try to find the canonical instance for a monster
    /// in SaveManager.Data. Prefer Data.owned, then Data.team.
    /// Returns null if none found.
    /// </summary>
 private static OwnedMonsterData FindCanonicalInSave(OwnedMonsterData source)
{
    var data = SaveManager.Data;
    if (data == null || source == null) return null;

    // 1) Prefer OWED LIST by ownedUID
    if (!string.IsNullOrEmpty(source.ownedUID) && data.owned != null)
    {
        var match = data.owned.Find(o => o != null && o.ownedUID == source.ownedUID);
        if (match != null) return match;
    }

    // 2) Then OWNED LIST by monsterId
    if (!string.IsNullOrEmpty(source.monsterId) && data.owned != null)
    {
        var match = data.owned.Find(o => o != null && o.monsterId == source.monsterId);
        if (match != null) return match;
    }

    // 3) Only fall back to TEAM if we truly can't find it in owned

    // Team by ownedUID
    if (!string.IsNullOrEmpty(source.ownedUID) && data.team != null)
    {
        var match = data.team.Find(o => o != null && o.ownedUID == source.ownedUID);
        if (match != null) return match;
    }

    // Team by monsterId
    if (!string.IsNullOrEmpty(source.monsterId) && data.team != null)
    {
        var match = data.team.Find(o => o != null && o.monsterId == source.monsterId);
        if (match != null) return match;
    }

    return null;
}


    /// <summary>
    /// Public resolver: returns the canonical OwnedMonsterData from SaveManager.Data
    /// that matches this one by ownedUID or monsterId; if none found, falls back
    /// to the original reference.
    /// </summary>
    public static OwnedMonsterData Resolve(OwnedMonsterData source)
    {
        if (source == null) return null;
        var canonical = FindCanonicalInSave(source);
        return canonical ?? source;
    }

    /// <summary>
    /// Perform a single manual level-up:
    /// - Spend Growth Cores
    /// - Increment level
    /// - Add unspent stat points
    /// - Clamp HP to new max
    /// - Save + fire OnTeamChanged
    /// Returns true if level-up succeeded.
    /// </summary>
        public static bool TryManualLevelUp(
        OwnedMonsterData raw,
        int pointsPerLevel,
        LevelCostCurveSO levelCostCurve,
        MonsterLibrarySO monsterLibrary = null)
    {
        if (raw == null || levelCostCurve == null)
            return false;

        // Canonical target is whatever actually lives inside SaveManager.Data
        var target = FindCanonicalInSave(raw) ?? raw;
        if (target == null || string.IsNullOrEmpty(target.monsterId))
            return false;

        var rm = ResourceManager.I;
        if (rm == null) return false;

        // Cost for NEXT level based on current level in save
        int need = levelCostCurve.CoresToNextLevel(target.level);
        int have = rm.Get(ResourceType.GrowthCores);
        if (have < need || need <= 0)
            return false;

        // Spend cores
        rm.Add(ResourceType.GrowthCores, -need);

        // Level up + stat points (on canonical)
        target.level = Mathf.Max(1, target.level + 1);
        target.unspentStatPoints += Mathf.Max(0, pointsPerLevel);

        // Clamp HP to new max
        MonsterDataSO def = null;
        if (monsterLibrary != null)
            def = monsterLibrary.GetById(target.monsterId);
        else
            def = MonsterLibraryLocator.GetById(target.monsterId);

        if (def)
        {
            int newMaxHP = Mathf.RoundToInt(BattleCalc.CalcHP(def, target.level));
            if (target.currentHP > newMaxHP)
                target.currentHP = newMaxHP;
        }

        // 🔥 Ensure this canonical monster exists in Data.owned and uses THIS reference
        var data = SaveManager.Data;
        if (data != null)
        {
            data.owned ??= new System.Collections.Generic.List<OwnedMonsterData>();

            OwnedMonsterData ownedMatch = null;

            // Try match by ownedUID
            if (!string.IsNullOrEmpty(target.ownedUID))
            {
                ownedMatch = data.owned.Find(o => o != null && o.ownedUID == target.ownedUID);
            }

            // Fallback: match by monsterId
            if (ownedMatch == null && !string.IsNullOrEmpty(target.monsterId))
            {
                ownedMatch = data.owned.Find(o => o != null && o.monsterId == target.monsterId);
            }

            // If we don't have it in owned yet, add THIS target as the owned instance
            if (ownedMatch == null)
            {
                data.owned.Add(target);
            }
            else if (!ReferenceEquals(ownedMatch, target))
            {
                // If there is an existing owned entry, update it to match the canonical target
                ownedMatch.level             = target.level;
                ownedMatch.unspentStatPoints = target.unspentStatPoints;
                ownedMatch.currentHP         = target.currentHP;
                ownedMatch.currentXP         = target.currentXP;
                ownedMatch.monsterId         = target.monsterId;
                ownedMatch.ownedUID          = target.ownedUID;
                ownedMatch.trainingBonus     = target.trainingBonus;
            }
        }

        // Mirror back to the UI instance if it's a different object
        if (!ReferenceEquals(raw, target))
        {
            raw.level             = target.level;
            raw.unspentStatPoints = target.unspentStatPoints;
            raw.currentHP         = target.currentHP;
            raw.currentXP         = target.currentXP;
            raw.monsterId         = target.monsterId;
            raw.ownedUID          = target.ownedUID;
            raw.trainingBonus     = target.trainingBonus;
        }

        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();

        return true;
    }


    /// <summary>
    /// Apply stat training (hp/atk/def/spd) via TrainingBonus, then save.
    /// Use this from StatBucketPanelUI.ConfirmSpend or auto-distribute.
    /// </summary>
    public static void ApplyTrainingAndSave(OwnedMonsterData raw, TrainingBonus delta)
    {
        if (raw == null) return;

        var target = FindCanonicalInSave(raw) ?? raw;
        if (target == null) return;

        // Apply to canonical
        MonsterStatApplier.Apply(target, delta);

        // Mirror back to raw if needed
        if (!ReferenceEquals(raw, target))
        {
            raw.trainingBonus = target.trainingBonus;
            raw.level         = target.level;
            raw.currentHP     = target.currentHP;
            raw.currentXP     = target.currentXP;
            raw.ownedUID      = target.ownedUID;
            raw.monsterId     = target.monsterId;
        }

        SaveManager.Save();
        SaveDebugTools.ExportAuditJson(true);
        GameEvents.OnTeamChanged?.Invoke();
    }
}
