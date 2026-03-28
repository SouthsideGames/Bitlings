using System;
using UnityEngine;
using System.Collections.Generic;

public static class XPManager
{
    private static OwnedMonsterData FindCanonicalInSave(OwnedMonsterData source)
    {
        var data = SaveManager.Data;
        if (data == null || source == null) return null;

        // 1) Prefer OWNED LIST by ownedUID
        if (!string.IsNullOrEmpty(source.ownedUID) && data.owned != null)
        {
            var match = data.owned.Find(o => o != null && o.ownedUID == source.ownedUID);
            if (match != null) return match;
        }

        // 2) Then OWNED LIST by monsterId (fallback)
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
        int have = rm.Get(ResourceType.GrowthCore);
        if (have < need || need <= 0)
            return false;

        // Spend cores
        rm.Add(ResourceType.GrowthCore, -need);

        // Level up + stat points (on canonical)
        target.level = Mathf.Max(1, target.level + 1);
        target.unspentStatPoints += Mathf.Max(0, pointsPerLevel);

        // Normalize premium identity (defensive)
        NormalizePremiumFields(target);

        // Clamp HP to new max
        MonsterDataSO def = null;
        if (monsterLibrary != null)
            def = monsterLibrary.GetById(target.monsterId);
        else
            def = MonsterLibraryLocator.GetById(target.monsterId);

        if (def)
        {
            int basePlusLevel = Mathf.RoundToInt(BattleCalc.CalcHP(def, target.level));
            int totalMaxHP = basePlusLevel + Mathf.Max(0, target.trainingBonus.hp);

            if (target.currentHP > totalMaxHP)
                SaveManager.SetMonsterHP(target, target.currentHP, stampLastHpUnix: false, save: false, fireEvents: false);
        }

        // Ensure this canonical monster exists in Data.owned and uses THIS reference (or merges correctly)
        var data = SaveManager.Data;
        if (data != null)
        {
            data.owned ??= new List<OwnedMonsterData>();

            OwnedMonsterData ownedMatch = null;

            // Try match by ownedUID
            if (!string.IsNullOrEmpty(target.ownedUID))
                ownedMatch = data.owned.Find(o => o != null && o.ownedUID == target.ownedUID);

            // Fallback: match by monsterId
            if (ownedMatch == null && !string.IsNullOrEmpty(target.monsterId))
                ownedMatch = data.owned.Find(o => o != null && o.monsterId == target.monsterId);

            // If we don't have it in owned yet, add THIS target as the owned instance
            if (ownedMatch == null)
            {
                data.owned.Add(target);
            }
            else if (!ReferenceEquals(ownedMatch, target))
            {
                // Merge target → ownedMatch (never strip premium identity)
                CopyAllGameplayFields(from: target, to: ownedMatch);
            }
        }

        // Mirror back to the UI instance if it's a different object
        if (!ReferenceEquals(raw, target))
        {
            CopyAllGameplayFields(from: target, to: raw);
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

        // Normalize premium identity (defensive)
        NormalizePremiumFields(target);

        // Ensure owned entry exists / merged
        var data = SaveManager.Data;
        if (data != null)
        {
            data.owned ??= new List<OwnedMonsterData>();

            OwnedMonsterData ownedMatch = null;

            if (!string.IsNullOrEmpty(target.ownedUID))
                ownedMatch = data.owned.Find(o => o != null && o.ownedUID == target.ownedUID);

            if (ownedMatch == null && !string.IsNullOrEmpty(target.monsterId))
                ownedMatch = data.owned.Find(o => o != null && o.monsterId == target.monsterId);

            if (ownedMatch == null)
            {
                data.owned.Add(target);
            }
            else if (!ReferenceEquals(ownedMatch, target))
            {
                CopyAllGameplayFields(from: target, to: ownedMatch);
            }
        }

        // Mirror back to raw if needed
        if (!ReferenceEquals(raw, target))
        {
            CopyAllGameplayFields(from: target, to: raw);
        }

        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();
    }

    // ---------------------------------------------------------------------
    // Internal helpers
    // ---------------------------------------------------------------------

    private static void NormalizePremiumFields(OwnedMonsterData om)
    {
        if (om == null) return;

        // Keep both fields consistent for legacy + new saves
        if (om.premiumTier > 0 && !om.isPremium) om.isPremium = true;
        if (om.isPremium && om.premiumTier <= 0) om.premiumTier = 1;
        if (!om.isPremium && om.premiumTier < 0) om.premiumTier = 0;
    }

    private static void CopyAllGameplayFields(OwnedMonsterData from, OwnedMonsterData to)
    {
        if (from == null || to == null) return;

        // Identity
        to.monsterId = from.monsterId;
        to.ownedUID  = from.ownedUID;

        // Core progression
        to.level     = from.level;
        to.currentXP = from.currentXP;
        // Centralized HP contract: copy HP without re-stamping timers.
        SaveManager.SetMonsterHPExact(to, from.currentHP, from.lastHPUnix, save: false, fireEvents: false);

        // HP regen / KO cooldown tracking
        // CRITICAL: must mirror so KO timers don't "tie together" across UI rows,
        // and so assigning to team preserves cooldown state correctly.
        to.lastHPUnix = from.lastHPUnix;

        // Stat/training progression
        to.unspentStatPoints    = from.unspentStatPoints;
        to.trainingBonus        = from.trainingBonus;
        to.lastBucketId         = from.lastBucketId;
        to.autoApply            = from.autoApply;
        to.autoApplyTargetLevel = from.autoApplyTargetLevel;
        to.trainingLastUnix     = from.trainingLastUnix;
        to.lastLevelClaimDay    = from.lastLevelClaimDay;
        to.pendingLevels        = from.pendingLevels;
        to.isTraining           = from.isTraining;

        // Misc combat/training modifiers
        to.flatAtkBonus = from.flatAtkBonus;

        // ✅ Premium identity (MUST persist + mirror)
        to.isPremium   = from.isPremium || from.premiumTier > 0 || to.isPremium;
        to.premiumTier = Mathf.Max(to.premiumTier, from.premiumTier);
        NormalizePremiumFields(to);

    }
}
