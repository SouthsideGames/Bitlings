using System.Linq;

public static class TeamUtils
{
    public static bool HasPlayableTeam(int minRequired = 1)
    {
        var data = SaveManager.Data;
        if (data == null || data.team == null) return false;

        int filled = 0;
        for (int i = 0; i < data.team.Count; i++)
        {
            var e = data.team[i];

            if (e != null && !string.IsNullOrEmpty(e.monsterId) && e.currentHP > 0)
            {
                filled++;
                if (filled >= minRequired) return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Team integrity helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Ensures the team list has at least <paramref name="size"/> slots.
    /// </summary>
    public static void EnsureTeamSize(System.Collections.Generic.List<OwnedMonsterData> team, int size = 3)
    {
        if (team == null) return;
        while (team.Count < size) team.Add(new OwnedMonsterData());
    }

    /// <summary>
    /// Removes any other slot that already contains the same ownedUID (preferred) or monsterId (fallback).
    /// This enforces the rule: one owned monster instance per team slot.
    /// </summary>
    public static void RemoveDuplicatesForAssignment(
        System.Collections.Generic.List<OwnedMonsterData> team,
        OwnedMonsterData candidate,
        int exceptSlotIndex)
    {
        if (team == null || candidate == null) return;

        string ownedUid = candidate.ownedUID;
        string monsterId = candidate.monsterId;

        for (int i = 0; i < team.Count; i++)
        {
            if (i == exceptSlotIndex) continue;

            var e = team[i];
            if (e == null) continue;

            // Prefer unique instance id when present.
            if (!string.IsNullOrEmpty(ownedUid))
            {
                if (!string.IsNullOrEmpty(e.ownedUID) && e.ownedUID == ownedUid)
                    team[i] = new OwnedMonsterData();
            }
            else if (!string.IsNullOrEmpty(monsterId))
            {
                // Fallback: if we somehow have no ownedUID, at least prevent identical monsterId duplication.
                if (!string.IsNullOrEmpty(e.monsterId) && e.monsterId == monsterId)
                    team[i] = new OwnedMonsterData();
            }
        }
    }
}
