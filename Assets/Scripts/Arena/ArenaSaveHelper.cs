// Assets/Scripts/Arena/ArenaSaveHelper.cs
// BRN Arena v1 — Safe accessors and initialization helpers for arena save data.
// All methods are static and operate on the sidecar ArenaSaveData cache held by SaveManager.

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Utility methods for arena save data access, initialization, and maintenance.
/// Called by SaveManager during load/save and by future arena systems at runtime.
/// </summary>
public static class ArenaSaveHelper
{
    // ─────────────────────────────────────────────
    // Initialization / repair
    // ─────────────────────────────────────────────

    /// <summary>
    /// Ensures the arena save data reference is non-null and all inner objects are initialized.
    /// Safe to call multiple times — idempotent.
    /// Called by SaveManager on load and on overwrite so old saves without arena data get valid defaults.
    /// </summary>
    public static void EnsureArenaDataInitialized(ref ArenaSaveData data)
    {
        data ??= new ArenaSaveData();

        // Identity strings — never null, may be empty until the player registers.
        data.arenaPlayerId ??= "";
        data.arenaUsername ??= "";

        // Generate a stable arenaPlayerId once if missing.
        if (string.IsNullOrEmpty(data.arenaPlayerId))
            data.arenaPlayerId = System.Guid.NewGuid().ToString("N");

        // Sub-objects
        data.battleTeamData ??= new ArenaBattleTeamData();
        data.battleTeamData.slot1OwnedBitlingId ??= "";
        data.battleTeamData.slot2OwnedBitlingId ??= "";
        data.battleTeamData.slot3OwnedBitlingId ??= "";
        data.battleTeamData.lockedTournamentId ??= "";

        data.lifetimeStats ??= new ArenaLifetimeStats();

        data.currentTournamentCache ??= new ArenaCurrentTournamentCache();
        data.currentTournamentCache.tournamentId ??= "";
        data.currentTournamentCache.playerEntryId ??= "";
        data.currentTournamentCache.lastMatchId ??= "";
        if (data.currentTournamentCache.lastViewedRoundIndex < 0)
            data.currentTournamentCache.lastViewedRoundIndex = 0;

        data.recentTournamentHistory ??= new List<ArenaTournamentHistoryEntry>();

        // Clamp tickets to valid range (guard against manual save edits or corruption).
        data.arenaTickets = Mathf.Clamp(data.arenaTickets, 0, ArenaConstants.MaxTickets);
        if (data.weeklyTicketsPurchased < 0) data.weeklyTicketsPurchased = 0;
    }

    // ─────────────────────────────────────────────
    // History trimming
    // ─────────────────────────────────────────────

    /// <summary>
    /// Trims <see cref="ArenaSaveData.recentTournamentHistory"/> to the retention cap
    /// defined in <see cref="ArenaConstants.TournamentHistoryRetention"/>.
    /// Newest entries are at the front of the list; oldest entries at the tail get pruned.
    /// Called automatically by SaveManager before every save.
    /// </summary>
    public static void TrimArenaHistory(ref ArenaSaveData data)
    {
        if (data?.recentTournamentHistory == null) return;

        int cap = ArenaConstants.TournamentHistoryRetention;
        if (data.recentTournamentHistory.Count <= cap) return;

        int excess = data.recentTournamentHistory.Count - cap;
        data.recentTournamentHistory.RemoveRange(cap, excess);
    }

    // ─────────────────────────────────────────────
    // Query helpers (all safe to call even before LoadOrCreate)
    // ─────────────────────────────────────────────

    /// <summary>Returns <c>true</c> if the arena feature is unlocked in save data.</summary>
    public static bool IsArenaUnlocked()
    {
        var arena = SaveManager.GetArenaSaveData();
        return arena != null && arena.arenaUnlocked;
    }

    /// <summary>Returns the current arena ticket count (0 if data unavailable).</summary>
    public static int GetArenaTicketCount()
    {
        var arena = SaveManager.GetArenaSaveData();
        return arena?.arenaTickets ?? 0;
    }

    /// <summary>Returns <c>true</c> if the player has set an arena display name.</summary>
    public static bool HasArenaUsername()
    {
        var arena = SaveManager.GetArenaSaveData();
        return arena != null && !string.IsNullOrEmpty(arena.arenaUsername);
    }

    /// <summary>Returns <c>true</c> if the arena battle team has at least one slot filled.</summary>
    public static bool HasAnyBattleTeamSlotFilled()
    {
        var team = SaveManager.GetArenaSaveData()?.battleTeamData;
        if (team == null) return false;
        return !string.IsNullOrEmpty(team.slot1OwnedBitlingId)
            || !string.IsNullOrEmpty(team.slot2OwnedBitlingId)
            || !string.IsNullOrEmpty(team.slot3OwnedBitlingId);
    }

    /// <summary>Returns <c>true</c> if all three battle team slots are filled.</summary>
    public static bool IsBattleTeamComplete()
    {
        var team = SaveManager.GetArenaSaveData()?.battleTeamData;
        if (team == null) return false;
        return !string.IsNullOrEmpty(team.slot1OwnedBitlingId)
            && !string.IsNullOrEmpty(team.slot2OwnedBitlingId)
            && !string.IsNullOrEmpty(team.slot3OwnedBitlingId);
    }

    /// <summary>Returns <c>true</c> if the battle team is currently locked to a tournament.</summary>
    public static bool IsBattleTeamLocked()
    {
        var team = SaveManager.GetArenaSaveData()?.battleTeamData;
        return team != null && team.isLocked;
    }

    /// <summary>Returns the player's current arena tournament status.</summary>
    public static ArenaPlayerTournamentStatus GetPlayerTournamentStatus()
    {
        var cache = SaveManager.GetArenaSaveData()?.currentTournamentCache;
        return cache?.playerStatus ?? ArenaPlayerTournamentStatus.NotEntered;
    }

    /// <summary>Returns the number of completed tournaments stored in history.</summary>
    public static int GetTournamentHistoryCount()
    {
        var arena = SaveManager.GetArenaSaveData();
        return arena?.recentTournamentHistory?.Count ?? 0;
    }

    /// <summary>
    /// True when a tournament is in progress and at least one newly resolved round
    /// has not yet been acknowledged by opening Arena.
    /// </summary>
    public static bool ShouldShowArenaRoundAlert()
    {
        var cache = SaveManager.GetArenaSaveData()?.currentTournamentCache;
        if (cache == null) return false;

        bool tournamentActive = cache.playerStatus == ArenaPlayerTournamentStatus.Active
                             || cache.playerStatus == ArenaPlayerTournamentStatus.Eliminated;
        if (!tournamentActive) return false;

        return cache.currentRoundIndex > cache.lastViewedRoundIndex;
    }

    /// <summary>
    /// Marks all currently resolved rounds as viewed by the player.
    /// Returns true if the viewed index changed.
    /// </summary>
    public static bool MarkArenaRoundResultsViewed(bool save = true)
    {
        var cache = SaveManager.GetArenaSaveData()?.currentTournamentCache;
        if (cache == null) return false;

        int viewed = Mathf.Max(cache.lastViewedRoundIndex, cache.currentRoundIndex);
        if (viewed == cache.lastViewedRoundIndex) return false;

        cache.lastViewedRoundIndex = viewed;

        if (save)
            SaveManager.Save();

        return true;
    }
}
