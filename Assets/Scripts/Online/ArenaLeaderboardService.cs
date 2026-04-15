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
    /// Submits the player's weekly tournament placement.
    /// Score is inverted (33 - placement) so that 1st place = highest score (32).
    /// UGS Leaderboards sort descending by default.
    /// </summary>
    public static async Task SubmitWeeklyPlacementAsync(int placement, int bracketSize = ArenaConstants.BracketSize)
    {
        if (!ArenaNetworkGuard.IsOnline) return;

        try
        {
            // Invert: 1st → 32, 2nd → 31, … 32nd → 1
            double score = (bracketSize + 1) - placement;
            await LeaderboardsService.Instance.AddPlayerScoreAsync(WeeklyLeaderboardId, score);
            Debug.Log($"{TAG} Weekly placement submitted: placement={placement}, score={score}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{TAG} Failed to submit weekly score: {ex.Message}");
        }
    }

    /// <summary>
    /// Submits the player's all-time championship count.
    /// Called after each tournament completion when the player won 1st place.
    /// </summary>
    public static async Task SubmitAllTimeChampionshipsAsync(int championshipCount)
    {
        if (!ArenaNetworkGuard.IsOnline) return;

        try
        {
            await LeaderboardsService.Instance.AddPlayerScoreAsync(AllTimeLeaderboardId, championshipCount);
            Debug.Log($"{TAG} All-time championships submitted: {championshipCount}");
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
            // 404 = player has no score yet — expected
            if (!ex.Message.Contains("404"))
                Debug.LogWarning($"{TAG} Failed to fetch player {leaderboardId} entry: {ex.Message}");
            return null;
        }
    }
}
