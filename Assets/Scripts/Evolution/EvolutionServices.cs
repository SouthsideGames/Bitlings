using System;
using System.Collections.Generic;
using UnityEngine;

public static class EvolutionService
{
     public static bool EvolveOwnedInstance(OwnedMonsterData source, string targetMonsterId, bool allowDuplicateSpecies = true)
    {
        var data = SaveManager.Data;
        if (data == null || source == null || string.IsNullOrEmpty(source.monsterId) || string.IsNullOrEmpty(targetMonsterId))
            return false;

        data.owned ??= new List<OwnedMonsterData>();
        data.team  ??= new List<OwnedMonsterData>();

        // 1) Resolve canonical owned entry (by ownedUID first, then by ref/monsterId)
        OwnedMonsterData ownedEntry = null;

        if (!string.IsNullOrEmpty(source.ownedUID))
        {
            ownedEntry = data.owned.Find(o => o != null && o.ownedUID == source.ownedUID);
        }

        if (ownedEntry == null)
        {
            // Fallback: same reference already in owned?
            int directIdx = data.owned.IndexOf(source);
            if (directIdx >= 0)
            {
                ownedEntry = data.owned[directIdx];
            }
        }

        if (ownedEntry == null)
        {
            // Fallback: first by species (less ideal, but better than nothing)
            ownedEntry = data.owned.Find(o => o != null && o.monsterId == source.monsterId);
        }

        // If we STILL don't have it in owned, treat this source as a new owned entry.
        if (ownedEntry == null)
        {
            if (string.IsNullOrEmpty(source.ownedUID))
                source.ownedUID = Guid.NewGuid().ToString("N");

            ownedEntry = source;
            data.owned.Add(ownedEntry);
        }

        string oldId = ownedEntry.monsterId;

        // 2) Enforce species uniqueness if requested
        if (!allowDuplicateSpecies)
        {
            for (int i = data.owned.Count - 1; i >= 0; i--)
            {
                var o = data.owned[i];
                if (o == null) continue;

                // Skip the evolving instance itself
                if (ReferenceEquals(o, ownedEntry)) continue;
                if (!string.IsNullOrEmpty(o.ownedUID) && !string.IsNullOrEmpty(ownedEntry.ownedUID) &&
                    o.ownedUID == ownedEntry.ownedUID)
                    continue;

                // Remove OTHER instances of the target species
                if (o.monsterId == targetMonsterId)
                {
                    ClearTeamSlotsForInstance(o);
                    data.owned.RemoveAt(i);
                }
            }
        }

        // 3) Actually evolve the canonical owned entry
        ownedEntry.monsterId = targetMonsterId;

        // 4) Sync any team slots that reference this exact instance
        SyncTeamSlotsForInstance(ownedEntry);

        // Stamp evolution snapshot
        if (!string.IsNullOrEmpty(ownedEntry.ownedUID))
        {
            var stats = SaveManager.GetOrCreateStats(ownedEntry.ownedUID);
            long now = SaveManager.NowUnix();
            stats.evolvedAtUnix = now;
            stats.evolvedFromMonsterId = oldId;
            stats.levelAtEvolution = ownedEntry.level;

            var equip = TitleSaveStore.GetOrCreateEquip(ownedEntry.ownedUID);
            stats.titlesEquippedAtEvolution = new List<string>();
            if (equip != null && equip.tierSelections != null)
            {
                for (int i = 0; i < equip.tierSelections.Count; i++)
                {
                    string id = equip.tierSelections[i];
                    if (!string.IsNullOrWhiteSpace(id))
                        stats.titlesEquippedAtEvolution.Add(id);
                }
            }

            GameEvents.EvolutionCeremonyRequested?.Invoke(oldId, targetMonsterId, ownedEntry.ownedUID);
        }

        SaveManager.Save();

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        DevLog.Log($"[EvolutionService] Evolved {oldId} → {targetMonsterId} (UID: {ownedEntry.ownedUID})");
        #endif
        return true;
    }


   public static bool TryAutoEvolve(OwnedMonsterData owned, MonsterLibrarySO lib)
    {
        if (owned == null || string.IsNullOrEmpty(owned.monsterId) || lib == null) return false;

        var def = lib.GetById(owned.monsterId);
        if (!def) return false;

        if (def.evolutionForm && def.evolutionLevel > 0 && owned.level >= def.evolutionLevel)
        {
            // ✅ Allow duplicates now.
            return EvolveOwnedInstance(owned, def.evolutionForm.id, allowDuplicateSpecies: true);
        }
        return false;
    }

    // ---------- helpers ----------

    private static void ClearTeamSlotsForInstance(OwnedMonsterData target)
    {
        var data = SaveManager.Data;
        if (data == null) return;

        var team = data.team ?? new List<OwnedMonsterData>();
        for (int i = 0; i < team.Count; i++)
        {
            var t = team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            if (IsSameInstance(t, target))
            {
                // Replace with an empty placeholder
                team[i] = new OwnedMonsterData();
            }
        }
        data.team = team;
    }

    /// <summary>
    /// Updates any team slot that references this owned instance to the new monsterId.
    /// </summary>
    private static void SyncTeamSlotsForInstance(OwnedMonsterData evolvedOwned)
    {
        var data = SaveManager.Data;
        if (data == null) return;

        var team = data.team ?? new List<OwnedMonsterData>();
        for (int i = 0; i < team.Count; i++)
        {
            var t = team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            if (IsSameInstance(t, evolvedOwned))
            {
                t.monsterId = evolvedOwned.monsterId;
                team[i] = t;
            }
        }
        data.team = team;
    }

    /// <summary>
    /// Checks if two OwnedMonsterData entries represent the same logical instance
    /// using ownedUID when available, otherwise falling back to reference equality.
    /// </summary>
    private static bool IsSameInstance(OwnedMonsterData a, OwnedMonsterData b)
    {
        if (a == null || b == null) return false;

        if (!string.IsNullOrEmpty(a.ownedUID) && !string.IsNullOrEmpty(b.ownedUID))
            return a.ownedUID == b.ownedUID;

        // Fallback: direct reference
        return ReferenceEquals(a, b);
    }
}