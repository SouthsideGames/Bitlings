// EvolutionService.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public static class EvolutionService
{
    /// Evolves the specified owned entry from its current monsterId to targetMonsterId.
    /// - Rewrites the entry in SaveManager.Data.owned
    /// - Updates any team slot pointing to that same owned record
    /// - Enforces "no duplicates" for species (unless allowDuplicateSpecies = true)
    /// Returns true if changed, false if blocked or invalid.
    public static bool EvolveOwnedByMonsterId(string fromMonsterId, string targetMonsterId, bool allowDuplicateSpecies = false)
    {
        var data = SaveManager.Data;
        if (data == null || string.IsNullOrEmpty(fromMonsterId) || string.IsNullOrEmpty(targetMonsterId))
            return false;

        var owned = data.owned ?? new List<OwnedMonsterData>();
        int idx = owned.FindIndex(o => o != null && o.monsterId == fromMonsterId);
        if (idx < 0) return false;

        // Enforce uniqueness: if we already own target species, handle it
        if (!allowDuplicateSpecies && owned.Exists(o => o != null && o.monsterId == targetMonsterId))
        {
            // We remove OTHER instances of target species to keep single copy after evolution.
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                if (i == idx) continue;
                var o = owned[i];
                if (o != null && o.monsterId == targetMonsterId)
                {
                    // If the duplicate is on team, clear its team slots
                    ClearTeamSlotsFor(o);
                    owned.RemoveAt(i);
                }
            }
        }

        // Update owned entry in place
        var entry = owned[idx];
        string oldId = entry.monsterId;
        entry.monsterId = targetMonsterId;
        owned[idx] = entry;

        // Update team slots that point to the same *owned record* (by identity)
        SyncTeamSlotIfSameIdentity(oldId, targetMonsterId, entry);

        data.owned = owned;
        SaveManager.Data = data;
        SaveManager.Save();

        // Optional: write audit
        SaveDebugTools.ExportAuditJson(true);

        Debug.Log($"[EvolutionService] Evolved {oldId} → {targetMonsterId}");
        return true;
    }

    /// Utility: evolve based on library definition (if current species has an evolution at/under this level).
    public static bool TryAutoEvolve(OwnedMonsterData owned, MonsterLibrarySO lib)
    {
        if (owned == null || string.IsNullOrEmpty(owned.monsterId) || lib == null) return false;

        var def = lib.GetById(owned.monsterId);
        if (!def) return false;

        if (def.evolutionForm && def.evolutionLevel > 0 && owned.level >= def.evolutionLevel)
        {
            return EvolveOwnedByMonsterId(owned.monsterId, def.evolutionForm.id, false);
        }
        return false;
    }

    // ---------- helpers ----------
    private static void ClearTeamSlotsFor(OwnedMonsterData target)
    {
        var team = SaveManager.Data.team ?? new List<OwnedMonsterData>();
        for (int i = 0; i < team.Count; i++)
        {
            var t = team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            // If team holds same monsterId and likely the same owned identity, clear it.
            if (t.monsterId == target.monsterId && LikelySameOwned(t, target))
                team[i] = new OwnedMonsterData();
        }
        SaveManager.Data.team = team;
    }

    private static void SyncTeamSlotIfSameIdentity(string oldId, string newId, OwnedMonsterData evolvedOwned)
    {
        var team = SaveManager.Data.team ?? new List<OwnedMonsterData>();
        for (int i = 0; i < team.Count; i++)
        {
            var t = team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            // Same "owned" identity? Update the species in team
            if (t.monsterId == oldId && LikelySameOwned(t, evolvedOwned))
            {
                t.monsterId = newId;
                team[i] = t;
            }
        }
        SaveManager.Data.team = team;
    }

    // Heuristic equality if ownedUID exists; otherwise fall back to level+timestamps
    private static bool LikelySameOwned(OwnedMonsterData a, OwnedMonsterData b)
    {
        try
        {
            var f = typeof(OwnedMonsterData).GetField("ownedUID");
            if (f != null)
            {
                string ua = f.GetValue(a) as string;
                string ub = f.GetValue(b) as string;
                if (!string.IsNullOrEmpty(ua) && !string.IsNullOrEmpty(ub))
                    return ua == ub;
            }
        }
        catch { }

        return a.level == b.level && a.lastHPUnix == b.lastHPUnix && a.currentXP == b.currentXP;
    }
}
