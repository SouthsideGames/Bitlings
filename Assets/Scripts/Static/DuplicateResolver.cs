// Scripts/Static/DuplicateResolver.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single-keeper duplicate policy with a settings override:
/// 
/// SettingsState.autoConvertDuplicates:
///   - true  => Keep ONE copy per species. All duplicates convert to Training XP.
///   - false => Keep duplicates normally (no conversion).
/// 
/// Conversion Rules when autoConvertDuplicates = true:
///   1) If you own neither shiny nor non-shiny:
///        - Add the caught monster as normal.
///   2) If you already own a shiny:
///        - Any new duplicate (shiny or not) converts into Training XP for that shiny.
///   3) If you own only a non-shiny:
///        - If caught is non-shiny: convert caught into XP for the existing non-shiny.
///        - If caught is shiny: upgrade the existing non-shiny to shiny and grant XP.
/// 
/// Return value:
///   - true  => We actually added a new owned entry to the roster (first-of-species OR keep-duplicates mode).
///   - false => We converted the catch (duplicate) into XP and did not add a new entry.
/// 
/// 'feedback' returns a short, UI-friendly message about what happened.
/// </summary>
public static class DuplicateResolver
{
    // ---------- Tuning ----------
    public const int   DuplicateBaseXp   = 12;    // flat XP
    public const float DuplicatePerLevel = 4f;    // +XP per wild level
    public const float ShinyBonusMult    = 1.25f; // 25% more when feeding into a shiny

    public static bool TryApplyOnCatch(OwnedMonsterData caught, MonsterDataSO wild, int wildLevel, out string feedback)
    {
        feedback = null;

        PlayerManager save = SaveManager.Data;
        if (save == null || caught == null || wild == null) { feedback = "Catch failed (data not ready)."; return false; }

        EnsureCollections(save);

        // If the player wants to keep duplicates, just add and be done.
        if (!SettingsManager.I || !SettingsManager.I.S.autoConvertDuplicates)
        {
            AddOwned(save, caught, wild);
            feedback = $"Caught {wild.displayName} (Lv {Mathf.Max(1, caught.level)}).";
            return true;
        }

        // Find existing entries for this species (by monsterId/species key).
        OwnedMonsterData existingNonShiny = null;
        OwnedMonsterData existingShiny    = null;

        string speciesId = caught.monsterId; // OwnedMonsterData.monsterId == MonsterDataSO.id
        for (int i = 0; i < save.owned.Count; i++)
        {
            var m = save.owned[i];
            if (m == null || m.monsterId != speciesId) continue;
            if (m.isShiny) existingShiny = m;
            else           existingNonShiny = m;
        }

        // Case A: first copy ever -> add as normal.
        if (existingNonShiny == null && existingShiny == null)
        {
            AddOwned(save, caught, wild);
            feedback = $"Caught {wild.displayName} (Lv {Mathf.Max(1, caught.level)}).";
            return true;
        }

        // Calculate duplicate XP grant.
        int grantXp = Mathf.Max(1, Mathf.RoundToInt(DuplicateBaseXp + DuplicatePerLevel * Mathf.Max(1, wildLevel)));

        // Case B: already own shiny -> feed every duplicate into the shiny.
        if (existingShiny != null)
        {
            int given = GrantTraining(existingShiny, grantXp, isIntoShiny: true);
            feedback = $"+{given} Training XP to shiny {wild.displayName} (duplicate).";
            return false;
        }

        // Case C: only non-shiny owned.
        if (!caught.isShiny)
        {
            // Feed into the existing non-shiny
            int given = GrantTraining(existingNonShiny, grantXp, isIntoShiny: false);
            feedback = $"+{given} Training XP to {wild.displayName} (duplicate).";
            return false;
        }
        else
        {
            // Upgrade path: caught shiny while owning a non-shiny -> mutate existing into shiny.
            UpgradeToShiny(existingNonShiny, caught);

            // Also convert the caught copy's value into XP for the upgraded one.
            int given = GrantTraining(existingNonShiny, grantXp, isIntoShiny: true);
            feedback = $"Upgraded to SHINY {wild.displayName}! (+{given} Training XP from duplicate).";
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static void EnsureCollections(PlayerManager save)
    {
        save.owned             ??= new List<OwnedMonsterData>();
        save.ownedIds          ??= new HashSet<string>();
        save.ownedIdsList      ??= new List<string>();
        save.seenTypes         ??= new HashSet<MonsterType>();
        save.seenTypesList     ??= new List<MonsterType>();
        save.unlockedJobSites  ??= new HashSet<JobType>();
        save.unlockedJobSitesList ??= new List<JobType>();
    }

    private static void AddOwned(PlayerManager save, OwnedMonsterData toAdd, MonsterDataSO wild)
    {
        if (string.IsNullOrEmpty(toAdd.ownedUID))
            toAdd.ownedUID = Guid.NewGuid().ToString("N");
        toAdd.currentHP = (toAdd.currentHP <= 0) ? -1 : toAdd.currentHP;
        toAdd.level     = Mathf.Max(1, toAdd.level);

        save.owned.Add(toAdd);

        // IMPORTANT: add to BOTH the HashSet (ownedIds) and its list mirror (ownedIdsList).
        if (!string.IsNullOrEmpty(wild.id))
        {
            save.ownedIds.Add(wild.id);
            if (!save.ownedIdsList.Contains(wild.id))
                save.ownedIdsList.Add(wild.id);
        }

        SaveManager.Save();
    }

    /// <summary> Merge fields to "upgrade" an existing non-shiny to shiny using data from the caught shiny. </summary>
    private static void UpgradeToShiny(OwnedMonsterData targetExisting, OwnedMonsterData caughtShiny)
    {
        if (targetExisting == null || caughtShiny == null) return;

        targetExisting.isShiny   = true;
        targetExisting.shinyTier = Mathf.Max(targetExisting.shinyTier, caughtShiny.shinyTier);
        targetExisting.level     = Mathf.Max(targetExisting.level, Mathf.Max(1, caughtShiny.level));
        targetExisting.currentHP = -1;

        if (string.IsNullOrEmpty(targetExisting.ownedUID))
            targetExisting.ownedUID = caughtShiny.ownedUID ?? Guid.NewGuid().ToString("N");

        SaveManager.Save();
    }

    private static int GrantTraining(OwnedMonsterData target, int baseAmount, bool isIntoShiny)
    {
        int amount = Mathf.RoundToInt(baseAmount * (isIntoShiny ? ShinyBonusMult : 1f));
        return GrantTrainingIntoTarget(target, amount);
    }

    /// <summary>
    /// Centralized training grant, going through TrainingManager if available so level/evolution hooks fire.
    /// Returns the amount of XP that actually landed on the target (post caps).
    /// </summary>
    private static int GrantTrainingIntoTarget(OwnedMonsterData target, int amount)
    {
        if (target == null || amount <= 0) return 0;

        var tm = TrainingManager.I;
        if (tm != null)
        {
            // Uses the helper we added to TrainingManager
            return tm.GrantInstantTrainingXP(target, amount);
        }

        // Fallback: direct mutation (no level pipeline). Kept as a safe backup.
        int before = target.currentXP;
        target.currentXP += amount;
        SaveManager.Save();
        return Mathf.Max(0, target.currentXP - before);
    }
}
