// Assets/Scripts/Arena/ArenaLoadoutManager.cs
// BRN Arena v1 — Manages the 3-slot arena battle team stored in ArenaSaveData.
// Mirrors IdleLoadoutManager's static API so DirectoryPanelUI and MonsterDetailPanelUI
// can route assignment/removal through the same code paths.

using System;
using System.Collections.Generic;
using UnityEngine;

public static class ArenaLoadoutManager
{
    public const int TeamSize = ArenaConstants.BattleTeamSize; // 3

    // ─────────────────────────────────────────────
    // Editing state (set by DirectoryPanelUI when in Arena mode)
    // ─────────────────────────────────────────────

    private static bool _editingArenaTeam;

    /// <summary>True while the Directory is in Arena Battle Team editing mode.</summary>
    public static bool IsEditingArenaTeam => _editingArenaTeam;

    public static void SetEditingArenaTeam(bool editing)
    {
        _editingArenaTeam = editing;
    }

    // ─────────────────────────────────────────────
    // Slot read helpers
    // ─────────────────────────────────────────────

    /// <summary>Returns the three slot UIDs as a list (empty string = empty slot).</summary>
    public static List<string> GetArenaTeamOwnedUids()
    {
        var team = GetTeamData();
        return new List<string>(3)
        {
            team.slot1OwnedBitlingId ?? "",
            team.slot2OwnedBitlingId ?? "",
            team.slot3OwnedBitlingId ?? ""
        };
    }

    /// <summary>Returns resolved OwnedMonsterData for each filled slot.</summary>
    public static List<OwnedMonsterData> GetArenaTeamMembers()
    {
        var list = new List<OwnedMonsterData>(TeamSize);
        var data = SaveManager.Data;
        if (data == null) return list;

        var uids = GetArenaTeamOwnedUids();
        for (int i = 0; i < uids.Count; i++)
        {
            if (string.IsNullOrEmpty(uids[i])) continue;
            var owned = FindOwnedByUid(data, uids[i]);
            if (owned != null && !string.IsNullOrEmpty(owned.monsterId))
                list.Add(owned);
        }

        return list;
    }

    /// <summary>Returns the ownedUID for a given slot index (0–2). Empty string if unset.</summary>
    public static string GetSlotUid(int slotIndex)
    {
        var team = GetTeamData();
        switch (slotIndex)
        {
            case 0: return team.slot1OwnedBitlingId ?? "";
            case 1: return team.slot2OwnedBitlingId ?? "";
            case 2: return team.slot3OwnedBitlingId ?? "";
            default: return "";
        }
    }

    // ─────────────────────────────────────────────
    // Assignment
    // ─────────────────────────────────────────────

    /// <summary>
    /// Assigns <paramref name="candidate"/> to the given arena slot.
    /// Removes <paramref name="candidate"/> from other arena slots if already present.
    /// Removes from Active and Idle teams so no monster is on multiple teams.
    /// Returns false if the team is locked or data is unavailable.
    /// </summary>
    public static bool AssignToArenaSlot(int slotIndex, OwnedMonsterData candidate)
    {
        if (candidate == null) return false;

        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return false;

        var team = arena.battleTeamData;
        if (team == null) return false;

        // Cannot modify while locked to a tournament.
        if (team.isLocked)
        {
            Debug.LogWarning("[ArenaLoadout] Cannot assign — battle team is locked.");
            return false;
        }

        slotIndex = Mathf.Clamp(slotIndex, 0, TeamSize - 1);

        var resolved = XPManager.Resolve(candidate) ?? candidate;
        string uid = resolved.ownedUID;

        if (string.IsNullOrEmpty(uid))
        {
            var data = SaveManager.Data;
            if (data != null)
            {
                var fallback = FindFirstOwnedByMonsterId(data, resolved.monsterId);
                uid = fallback?.ownedUID;
            }
        }

        if (string.IsNullOrEmpty(uid)) return false;

        // Remove from other arena slots (no duplicate instance within arena team).
        ClearUidFromAllSlots(team, uid, exceptSlot: slotIndex);

        // Remove from Active and Idle teams so the same monster isn't on multiple teams.
        RemoveFromActiveByOwnedUid(uid);
        IdleLoadoutManager.RemoveFromIdleByOwnedUid(uid);

        // Set the target slot.
        SetSlot(team, slotIndex, uid);

        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();
        return true;
    }

    /// <summary>Clears the given arena slot. Returns false if locked or already empty.</summary>
    public static bool RemoveFromArenaSlot(int slotIndex)
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena == null) return false;

        var team = arena.battleTeamData;
        if (team == null) return false;

        if (team.isLocked)
        {
            Debug.LogWarning("[ArenaLoadout] Cannot remove — battle team is locked.");
            return false;
        }

        slotIndex = Mathf.Clamp(slotIndex, 0, TeamSize - 1);

        if (string.IsNullOrEmpty(GetSlotUid(slotIndex)))
            return false;

        SetSlot(team, slotIndex, "");

        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();
        return true;
    }

    /// <summary>Removes the given ownedUID from any arena slot it occupies.</summary>
    public static void RemoveFromArenaByOwnedUid(string uid, int exceptSlot = -1)
    {
        if (string.IsNullOrEmpty(uid)) return;

        var arena = SaveManager.GetArenaSaveData();
        if (arena?.battleTeamData == null) return;
        if (arena.battleTeamData.isLocked) return;

        ClearUidFromAllSlots(arena.battleTeamData, uid, exceptSlot);
    }

    // ─────────────────────────────────────────────
    // Visibility mode
    // ─────────────────────────────────────────────

    public static ArenaVisibilityMode GetVisibilityMode()
    {
        return GetTeamData().visibilityMode;
    }

    /// <summary>Sets the visibility mode. Returns false if locked.</summary>
    public static bool SetVisibilityMode(ArenaVisibilityMode mode)
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena?.battleTeamData == null) return false;
        if (arena.battleTeamData.isLocked) return false;

        arena.battleTeamData.visibilityMode = mode;
        SaveManager.Save();
        return true;
    }

    /// <summary>Toggles between FullReveal and LimitedReveal. Returns the new mode.</summary>
    public static ArenaVisibilityMode ToggleVisibilityMode()
    {
        var current = GetVisibilityMode();
        var next = current == ArenaVisibilityMode.FullReveal
            ? ArenaVisibilityMode.LimitedReveal
            : ArenaVisibilityMode.FullReveal;
        SetVisibilityMode(next);
        return next;
    }

    // ─────────────────────────────────────────────
    // Lock / unlock (called by tournament systems, not by player)
    // ─────────────────────────────────────────────

    /// <summary>Locks the battle team to a tournament. Prevents edits until unlocked.</summary>
    public static void LockTeam(string tournamentId)
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena?.battleTeamData == null) return;

        arena.battleTeamData.isLocked = true;
        arena.battleTeamData.lockedTournamentId = tournamentId ?? "";
        SaveManager.Save();
    }

    /// <summary>Unlocks the battle team (called on elimination or tournament completion).</summary>
    public static void UnlockTeam()
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena?.battleTeamData == null) return;

        arena.battleTeamData.isLocked = false;
        arena.battleTeamData.lockedTournamentId = "";
        SaveManager.Save();
    }

    // ─────────────────────────────────────────────
    // Internal helpers
    // ─────────────────────────────────────────────

    private static ArenaBattleTeamData GetTeamData()
    {
        var arena = SaveManager.GetArenaSaveData();
        if (arena?.battleTeamData != null) return arena.battleTeamData;

        // Fallback: return a detached default so callers never get null.
        return new ArenaBattleTeamData();
    }

    private static void SetSlot(ArenaBattleTeamData team, int slotIndex, string uid)
    {
        switch (slotIndex)
        {
            case 0: team.slot1OwnedBitlingId = uid; break;
            case 1: team.slot2OwnedBitlingId = uid; break;
            case 2: team.slot3OwnedBitlingId = uid; break;
        }
    }

    private static string GetSlotValue(ArenaBattleTeamData team, int slotIndex)
    {
        switch (slotIndex)
        {
            case 0: return team.slot1OwnedBitlingId ?? "";
            case 1: return team.slot2OwnedBitlingId ?? "";
            case 2: return team.slot3OwnedBitlingId ?? "";
            default: return "";
        }
    }

    private static void ClearUidFromAllSlots(ArenaBattleTeamData team, string uid, int exceptSlot)
    {
        for (int i = 0; i < TeamSize; i++)
        {
            if (i == exceptSlot) continue;
            if (string.Equals(GetSlotValue(team, i), uid, StringComparison.Ordinal))
                SetSlot(team, i, "");
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
                if (o != null && string.Equals(o.ownedUID, uid, StringComparison.Ordinal))
                    return o;
            }
        }

        if (data.team != null)
        {
            for (int i = 0; i < data.team.Count; i++)
            {
                var t = data.team[i];
                if (t != null && string.Equals(t.ownedUID, uid, StringComparison.Ordinal))
                    return t;
            }
        }

        return null;
    }

    private static void RemoveFromActiveByOwnedUid(string uid)
    {
        var data = SaveManager.Data;
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

    private static OwnedMonsterData FindFirstOwnedByMonsterId(PlayerManager data, string monsterId)
    {
        if (data?.owned == null || string.IsNullOrEmpty(monsterId)) return null;

        for (int i = 0; i < data.owned.Count; i++)
        {
            var o = data.owned[i];
            if (o != null && string.Equals(o.monsterId, monsterId, StringComparison.Ordinal))
                return o;
        }

        return null;
    }
}
