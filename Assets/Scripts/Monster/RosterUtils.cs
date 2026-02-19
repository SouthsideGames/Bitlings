using System.Collections.Generic;
using UnityEngine;

public static class RosterUtils
{
    /// <summary>
    /// Returns a merged list of all monsters the player has (owned + team),
    /// de-duped by ownedUID when available. Never returns null.
    /// </summary>
    public static List<OwnedMonsterData> GetAllOwnedMonstersMerged(bool includeTeam = true)
    {
        var data = SaveManager.Data;
        if (data == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[RosterUtils] SaveManager.Data is null.");
            #endif
            return new List<OwnedMonsterData>();
        }

        data.EnsureTransientSets(); // make sure lists/sets exist

        var result = new List<OwnedMonsterData>(16);
        var seen   = new HashSet<string>();

        // owned first
        if (data.owned != null)
        {
            for (int i = 0; i < data.owned.Count; i++)
            {
                var o = data.owned[i];
                if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;
                result.Add(o);
                if (!string.IsNullOrEmpty(o.ownedUID)) seen.Add(o.ownedUID);
            }
        }

        if (includeTeam && data.team != null)
        {
            for (int i = 0; i < data.team.Count; i++)
            {
                var t = data.team[i];
                if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

                var uid = string.IsNullOrEmpty(t.ownedUID) ? null : t.ownedUID;
                if (uid != null && seen.Contains(uid)) continue;

                result.Add(t);
                if (uid != null) seen.Add(uid);
            }
        }

        return result;
    }

    /// <summary>Ensures team list exists and has exactly 3 slots (padding with empty entries).</summary>
    public static void EnsureTeam3()
    {
        var data = SaveManager.Data;
        if (data == null) return;

        data.team ??= new List<OwnedMonsterData>(3);
        while (data.team.Count < 3) data.team.Add(new OwnedMonsterData());
        while (data.team.Count > 3) data.team.RemoveAt(data.team.Count - 1);
    }

    /// <summary>Convenience: returns true if the save has any real monsters.</summary>
    public static bool HasAny()
    {
        var all = GetAllOwnedMonstersMerged(true);
        for (int i = 0; i < all.Count; i++)
        {
            var m = all[i];
            if (m != null && !string.IsNullOrEmpty(m.monsterId)) return true;
        }
        return false;
    }
}