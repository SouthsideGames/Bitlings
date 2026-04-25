using System;
using System.Collections.Generic;
using UnityEngine;

public static class IdleLoadoutManager
{
    public const int TeamSize = 3;

    private static bool _editingIdleTeam;

    public static bool IsEditingIdleTeam => _editingIdleTeam;

    public static void SetEditingIdleTeam(bool editingIdleTeam)
    {
        _editingIdleTeam = editingIdleTeam;
    }

    public static void EnsureInitialized(PlayerManager data)
    {
        if (data == null) return;

        data.idleTeamOwnedUIDs ??= new List<string>();
        while (data.idleTeamOwnedUIDs.Count < TeamSize) data.idleTeamOwnedUIDs.Add(null);
        while (data.idleTeamOwnedUIDs.Count > TeamSize) data.idleTeamOwnedUIDs.RemoveAt(data.idleTeamOwnedUIDs.Count - 1);

        for (int i = 0; i < data.idleTeamOwnedUIDs.Count; i++)
        {
            string uid = data.idleTeamOwnedUIDs[i];
            if (string.IsNullOrEmpty(uid)) continue;
            if (FindOwnedByUid(data, uid) == null)
                data.idleTeamOwnedUIDs[i] = null;
        }

        // Deduplicate idle slots by UID while preserving first occurrence.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < data.idleTeamOwnedUIDs.Count; i++)
        {
            string uid = data.idleTeamOwnedUIDs[i];
            if (string.IsNullOrEmpty(uid)) continue;
            if (!seen.Add(uid)) data.idleTeamOwnedUIDs[i] = null;
        }

        // Idle and active team cannot share the same ownedUID.
        if (data.team != null)
        {
            var activeUids = new HashSet<string>(StringComparer.Ordinal);
            int activeCount = Mathf.Min(TeamSize, data.team.Count);
            for (int i = 0; i < activeCount; i++)
            {
                var t = data.team[i];
                if (t == null || string.IsNullOrEmpty(t.ownedUID)) continue;
                activeUids.Add(t.ownedUID);
            }

            for (int i = 0; i < data.idleTeamOwnedUIDs.Count; i++)
            {
                string uid = data.idleTeamOwnedUIDs[i];
                if (string.IsNullOrEmpty(uid)) continue;
                if (activeUids.Contains(uid))
                    data.idleTeamOwnedUIDs[i] = null;
            }
        }
    }

    public static IReadOnlyList<string> GetIdleTeamOwnedUids()
    {
        var data = SaveManager.Data;
        if (data == null) return Array.Empty<string>();

        EnsureInitialized(data);
        return data.idleTeamOwnedUIDs;
    }

    public static bool IsIdleTeamEmpty()
    {
        var data = SaveManager.Data;
        if (data == null) return true;

        EnsureInitialized(data);

        for (int i = 0; i < data.idleTeamOwnedUIDs.Count; i++)
        {
            if (!string.IsNullOrEmpty(data.idleTeamOwnedUIDs[i]))
                return false;
        }

        return true;
    }

    public static List<OwnedMonsterData> GetIdleTeamMembers()
    {
        var list = new List<OwnedMonsterData>(TeamSize);
        var data = SaveManager.Data;
        if (data == null) return list;

        EnsureInitialized(data);

        for (int i = 0; i < TeamSize; i++)
        {
            string uid = data.idleTeamOwnedUIDs[i];
            if (string.IsNullOrEmpty(uid)) continue;

            var owned = FindOwnedByUid(data, uid);
            if (owned == null || string.IsNullOrEmpty(owned.monsterId)) continue;
            list.Add(owned);
        }

        return list;
    }

    public static List<OwnedMonsterData> GetIdleBattleTeamWithFallback()
    {
        var idleTeam = GetIdleTeamMembers();
        if (idleTeam.Count > 0)
            return idleTeam;

        var fallback = new List<OwnedMonsterData>(TeamSize);
        var data = SaveManager.Data;
        if (data?.team == null) return fallback;

        int n = Mathf.Min(TeamSize, data.team.Count);
        for (int i = 0; i < n; i++)
        {
            var t = data.team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;
            fallback.Add(t);
        }

        return fallback;
    }

    public static bool AssignToIdleSlot(int slotIndex, OwnedMonsterData candidate)
    {
        var data = SaveManager.Data;
        if (data == null || candidate == null) return false;

        EnsureInitialized(data);

        slotIndex = Mathf.Clamp(slotIndex, 0, TeamSize - 1);

        var resolved = XPManager.Resolve(candidate) ?? candidate;
        string uid = resolved.ownedUID;

        if (string.IsNullOrEmpty(uid))
        {
            var fallback = FindFirstOwnedByMonsterId(data, resolved.monsterId);
            uid = fallback != null ? fallback.ownedUID : null;
        }

        if (string.IsNullOrEmpty(uid))
            return false;

        RemoveFromIdleByOwnedUid(uid, slotIndex);
        RemoveFromActiveByOwnedUid(data, uid);
        ArenaLoadoutManager.RemoveFromArenaByOwnedUid(uid);

        data.idleTeamOwnedUIDs[slotIndex] = uid;
        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();
        return true;
    }

    public static bool RemoveFromIdleSlot(int slotIndex)
    {
        var data = SaveManager.Data;
        if (data == null) return false;

        EnsureInitialized(data);
        slotIndex = Mathf.Clamp(slotIndex, 0, TeamSize - 1);

        if (string.IsNullOrEmpty(data.idleTeamOwnedUIDs[slotIndex]))
            return false;

        data.idleTeamOwnedUIDs[slotIndex] = null;
        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();
        return true;
    }

    public static void RemoveFromIdleByOwnedUid(string uid, int exceptSlot = -1)
    {
        if (string.IsNullOrEmpty(uid)) return;

        var data = SaveManager.Data;
        if (data == null) return;

        EnsureInitialized(data);

        for (int i = 0; i < data.idleTeamOwnedUIDs.Count; i++)
        {
            if (i == exceptSlot) continue;
            if (!string.Equals(data.idleTeamOwnedUIDs[i], uid, StringComparison.Ordinal)) continue;
            data.idleTeamOwnedUIDs[i] = null;
        }
    }

    private static void RemoveFromActiveByOwnedUid(PlayerManager data, string uid)
    {
        if (data?.team == null || string.IsNullOrEmpty(uid)) return;

        int n = Mathf.Min(TeamSize, data.team.Count);
        for (int i = 0; i < n; i++)
        {
            var t = data.team[i];
            if (t == null || string.IsNullOrEmpty(t.ownedUID)) continue;
            if (!string.Equals(t.ownedUID, uid, StringComparison.Ordinal)) continue;
            data.team[i] = new OwnedMonsterData();
        }
    }

    private static OwnedMonsterData FindOwnedByUid(PlayerManager data, string uid)
    {
        if (data == null || string.IsNullOrEmpty(uid)) return null;

        if (data.owned != null)
        {
            for (int i = 0; i < data.owned.Count; i++)
            {
                var o = data.owned[i];
                if (o == null || string.IsNullOrEmpty(o.ownedUID)) continue;
                if (string.Equals(o.ownedUID, uid, StringComparison.Ordinal)) return o;
            }
        }

        if (data.team != null)
        {
            for (int i = 0; i < data.team.Count; i++)
            {
                var t = data.team[i];
                if (t == null || string.IsNullOrEmpty(t.ownedUID)) continue;
                if (string.Equals(t.ownedUID, uid, StringComparison.Ordinal)) return t;
            }
        }

        return null;
    }

    private static OwnedMonsterData FindFirstOwnedByMonsterId(PlayerManager data, string monsterId)
    {
        if (data == null || string.IsNullOrEmpty(monsterId)) return null;

        if (data.owned != null)
        {
            for (int i = 0; i < data.owned.Count; i++)
            {
                var o = data.owned[i];
                if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;
                if (string.Equals(o.monsterId, monsterId, StringComparison.Ordinal)) return o;
            }
        }

        if (data.team != null)
        {
            for (int i = 0; i < data.team.Count; i++)
            {
                var t = data.team[i];
                if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;
                if (string.Equals(t.monsterId, monsterId, StringComparison.Ordinal)) return t;
            }
        }

        return null;
    }
}
