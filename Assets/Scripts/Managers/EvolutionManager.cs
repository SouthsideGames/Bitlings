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

        if (team != null)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var m = team[i];
                if (m != null && m.monsterId == monsterId && m.level >= def.evolutionLevel)
                {
                    m.monsterId = newId;
                    int maxTeamHP = Mathf.RoundToInt(BattleCalc.CalcHP(defNew, Mathf.Max(1, m.level)));
                    m.currentHP = Mathf.Max(1, maxTeamHP);
                    team[i] = m;
                    teamRef = m;
                    changed = true;
                    break;
                }
            }
        }

        var owned = SaveManager.Data.owned;
        if (owned != null)
        {
            int ownedIdx = -1;

            if (teamRef != null && !string.IsNullOrEmpty(teamRef.ownedUID))
                ownedIdx = owned.FindIndex(o => o != null && o.ownedUID == teamRef.ownedUID);

            if (ownedIdx < 0 && teamRef != null)
                ownedIdx = owned.FindIndex(o => o != null && o.monsterId == monsterId && o.level == teamRef.level);

            if (ownedIdx < 0)
                ownedIdx = owned.FindIndex(o => o != null && o.monsterId == monsterId);

            if (ownedIdx >= 0)
            {
                var o = owned[ownedIdx];
                o.monsterId = newId;
                int maxOwnedHP = Mathf.RoundToInt(BattleCalc.CalcHP(defNew, Mathf.Max(1, o.level)));
                o.currentHP = Mathf.Max(1, maxOwnedHP);
                owned[ownedIdx] = o;
                changed = true;
            }
        }

        if (!changed) return false;

        if (SaveManager.Data.trainingMonsterId == monsterId)
            SaveManager.Data.trainingMonsterId = newId;

        RefreshOwnedIds(monsterId, newId);

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
}
