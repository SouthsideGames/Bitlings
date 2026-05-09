using UnityEngine;
using System.Collections.Generic;

public static class EvolutionManager
{
    public static bool TryEvolve(string monsterId, MonsterLibrarySO library)
    {
        if (SaveManager.Data == null || string.IsNullOrEmpty(monsterId) || library == null) return false;

        var def = library.GetById(monsterId);
        if (def == null || def.evolutionForm == null || def.evolutionLevel <= 0) return false;

        string newId = def.evolutionForm.id;
        var defNew = library.GetById(newId);
        if (defNew == null) return false;

        bool changed = false;
        var team = SaveManager.Data.team;
        OwnedMonsterData teamRef = null;
        string evolvedOwnedUID = null;
        int evolvedLevel = 0;

        // ---- TEAM PASS ----
        if (team != null)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var m = team[i];
                if (m != null && m.monsterId == monsterId && m.level >= def.evolutionLevel)
                {
                    m.monsterId = newId;

                    // Full heal using NEW BASE + SAME LEVEL scaling + training bonus
                    EvolutionHelper.FullHealToMax(m, defNew);

                    team[i] = m;
                    teamRef = m;
                    evolvedOwnedUID = m.ownedUID;
                    evolvedLevel = m.level;
                    changed = true;
                    break;
                }
            }
        }

        // ---- OWNED PASS ----
        var owned = SaveManager.Data.owned;
        if (owned != null)
        {
            int ownedIdx = -1;

            // Prefer matching by ownedUID if we evolved a team ref above
            if (teamRef != null && !string.IsNullOrEmpty(teamRef.ownedUID))
                ownedIdx = owned.FindIndex(o => o != null && o.ownedUID == teamRef.ownedUID);

            // Fallback match by id+level if needed
            if (ownedIdx < 0 && teamRef != null)
                ownedIdx = owned.FindIndex(o => o != null && o.monsterId == monsterId && o.level == teamRef.level);

            // Final fallback: first by id
            if (ownedIdx < 0)
                ownedIdx = owned.FindIndex(o => o != null && o.monsterId == monsterId);

            if (ownedIdx >= 0)
            {
                var o = owned[ownedIdx];
                o.monsterId = newId;

                // Full heal using NEW BASE + SAME LEVEL scaling + training bonus
                EvolutionHelper.FullHealToMax(o, defNew);

                owned[ownedIdx] = o;
                if (string.IsNullOrEmpty(evolvedOwnedUID) && !string.IsNullOrEmpty(o.ownedUID))
                {
                    evolvedOwnedUID = o.ownedUID;
                    evolvedLevel = o.level;
                }
                changed = true;
            }
        }

        if (!changed) return false;

        // Update training ref if pointing at old id
        if (SaveManager.Data.trainingMonsterId == monsterId)
            SaveManager.Data.trainingMonsterId = newId;

        RefreshOwnedIds(monsterId, newId);

        // Ensure jobAssignments worker IDs don't hold stale references
        CleanupJobAssignmentsAfterEvolution();

        // Stamp evolution snapshot
        if (!string.IsNullOrEmpty(evolvedOwnedUID))
        {
            var stats = SaveManager.GetOrCreateStats(evolvedOwnedUID);
            long now = SaveManager.NowUnix();
            stats.evolvedAtUnix = now;
            stats.evolvedFromMonsterId = monsterId;
            stats.levelAtEvolution = evolvedLevel;

            var equip = TitleSaveStore.GetOrCreateEquip(evolvedOwnedUID);
            stats.titlesEquippedAtEvolution = new System.Collections.Generic.List<string>();
            if (equip != null && equip.tierSelections != null)
            {
                for (int i = 0; i < equip.tierSelections.Count; i++)
                {
                    string id = equip.tierSelections[i];
                    if (!string.IsNullOrWhiteSpace(id))
                        stats.titlesEquippedAtEvolution.Add(id);
                }
            }

            GameEvents.EvolutionCeremonyRequested?.Invoke(monsterId, newId, evolvedOwnedUID);
        }

        SaveManager.Save();

        GameEvents.OnTeamChanged?.Invoke();
        GameEvents.MonsterEvolved?.Invoke(newId);
        TitlesAdapter.OnMonsterEvolved(newId);
        return true;
    }

    private static void RefreshOwnedIds(string oldId, string newId)
    {
        var data = SaveManager.Data;
        if (data == null) return;

        if (data.ownedIds == null) data.ownedIds = new HashSet<string>();
        data.ownedIds.Add(newId);

        bool anyOldInOwned = data.owned != null && data.owned.Exists(o => o != null && o.monsterId == oldId);
        bool anyOldInTeam  = data.team  != null && data.team.Exists(o  => o != null && o.monsterId == oldId);

        if (!anyOldInOwned && !anyOldInTeam)
            data.ownedIds.Remove(oldId);
    }

    private static void CleanupJobAssignmentsAfterEvolution()
    {
        var data = SaveManager.Data;
        if (data == null) return;
        if (data.jobAssignments == null || data.jobAssignments.Count == 0) return;

        var validUIDs = new HashSet<string>();
        var all = data.GetAllOwnedMonsters(includeTeam: true);
        if (all != null)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var om = all[i];
                if (om != null && !string.IsNullOrEmpty(om.ownedUID))
                    validUIDs.Add(om.ownedUID);
            }
        }

        for (int j = 0; j < data.jobAssignments.Count; j++)
        {
            var job = data.jobAssignments[j];
            if (job == null || job.workerIds == null) continue;

            job.workerIds.RemoveAll(id => string.IsNullOrEmpty(id) || !validUIDs.Contains(id));
        }
    }
}
