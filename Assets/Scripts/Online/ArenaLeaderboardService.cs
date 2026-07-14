// Assets/Scripts/Online/ArenaLeaderboardService.cs
// BRN Arena v1 — Client-side wrapper for UGS Leaderboards.
// Two leaderboards:
//   "arena_weekly"  — placement score submitted after each tournament (lower = better)
//   "arena_alltime" — career championship count (higher = better)

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Models;
using UnityEngine;

/// <summary>
/// Reads and writes arena leaderboard data via UGS Leaderboards.
/// </summary>
public static class ArenaLeaderboardService
{
    const string TAG = "[ArenaLeaderboard]";

    /// <summary>Leaderboard ID for weekly tournament placement (lower = better).</summary>
    public const string WeeklyLeaderboardId = "arena_weekly";

    /// <summary>Leaderboard ID for all-time championship count (higher = better).</summary>
    public const string AllTimeLeaderboardId = "arena_alltime";

    // ═════════════════════════════════════════════════════════════
    //  Score Submission
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// DEPRECATED — do not call. Direct client leaderboard writes are forgeable and
    /// have been replaced by server-authoritative scoring via the
    /// SubmitTournamentResult Cloud Code endpoint (see
    /// ArenaTournamentService.TrySubmitPendingResultAsync). Once the dashboard
    /// Access-Control policy denies player-token writes to arena_weekly, this call
    /// fails anyway. Kept only so older references compile during rollout.
    /// </summary>
    [Obsolete("Leaderboard writes are now server-authoritative via SubmitTournamentResult. Do not call from the client.")]
    public static async Task SubmitWeeklyPlacementAsync(int placement, int bracketSize = ArenaConstants.BracketSize)
    {
        if (!ArenaNetworkGuard.IsOnline) return;

        try
        {
            // Invert: 1st → 32, 2nd → 31, … 32nd → 1
            double score = (bracketSize + 1) - placement;
            await LeaderboardsService.Instance.AddPlayerScoreAsync(WeeklyLeaderboardId, score);
            DevLog.Log($"{TAG} Weekly placement submitted: placement={placement}, score={score}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{TAG} Failed to submit weekly score: {ex.Message}");
        }
    }

    /// <summary>
    /// DEPRECATED — do not call. All-time championships are now maintained
    /// server-side (authoritative counter in SubmitTournamentResult). Kept only so
    /// older references compile during rollout.
    /// </summary>
    [Obsolete("All-time championships are now server-authoritative via SubmitTournamentResult. Do not call from the client.")]
    public static async Task SubmitAllTimeChampionshipsAsync(int championshipCount)
    {
        if (!ArenaNetworkGuard.IsOnline) return;

        try
        {
            await LeaderboardsService.Instance.AddPlayerScoreAsync(AllTimeLeaderboardId, championshipCount);
            DevLog.Log($"{TAG} All-time championships submitted: {championshipCount}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{TAG} Failed to submit all-time score: {ex.Message}");
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Score Retrieval
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetches the top N entries from the weekly leaderboard.
    /// </summary>
    public static async Task<List<LeaderboardEntry>> GetWeeklyTopAsync(int count = 25)
    {
        return await GetTopScoresAsync(WeeklyLeaderboardId, count);
    }

    /// <summary>
    /// Fetches the top N entries from the all-time leaderboard.
    /// </summary>
    public static async Task<List<LeaderboardEntry>> GetAllTimeTopAsync(int count = 25)
    {
        return await GetTopScoresAsync(AllTimeLeaderboardId, count);
    }

    /// <summary>
    /// Fetches the current player's entry on the weekly leaderboard.
    /// Returns null if the player has no entry.
    /// </summary>
    public static async Task<LeaderboardEntry> GetPlayerWeeklyEntryAsync()
    {
        return await GetPlayerEntryAsync(WeeklyLeaderboardId);
    }

    /// <summary>
    /// Fetches the current player's entry on the all-time leaderboard.
    /// Returns null if the player has no entry.
    /// </summary>
    public static async Task<LeaderboardEntry> GetPlayerAllTimeEntryAsync()
    {
        return await GetPlayerEntryAsync(AllTimeLeaderboardId);
    }

    // ═════════════════════════════════════════════════════════════
    //  Internal
    // ═════════════════════════════════════════════════════════════

    private static async Task<List<LeaderboardEntry>> GetTopScoresAsync(string leaderboardId, int count)
    {
        if (!ArenaNetworkGuard.IsOnline) return new List<LeaderboardEntry>();

        try
        {
            var options = new GetScoresOptions { Limit = count };
            var response = await LeaderboardsService.Instance.GetScoresAsync(leaderboardId, options);
            return response?.Results ?? new List<LeaderboardEntry>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{TAG} Failed to fetch {leaderboardId} top scores: {ex.Message}");
            return new List<LeaderboardEntry>();
        }
    }

    private static async Task<LeaderboardEntry> GetPlayerEntryAsync(string leaderboardId)
    {
        if (!ArenaNetworkGuard.IsOnline) return null;

        try
        {
            return await LeaderboardsService.Instance.GetPlayerScoreAsync(leaderboardId);
        }
        catch (Exception ex)
        {
            // Player has no score yet — expected, not an error
            if (!ex.Message.Contains("404") && !ex.Message.Contains("could not be found"))
                Debug.LogWarning($"{TAG} Failed to fetch player {leaderboardId} entry: {ex.Message}");
            return null;
        }
    }
}
