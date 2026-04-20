using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using UnityEngine;

/// <summary>
/// Bridges the local <see cref="ArenaSaveData"/> cache with UGS Cloud Save.
///
/// Read/write flow:
///   • <see cref="PushArenaDataAsync"/> — serialises the local cache and uploads it.
///   • <see cref="PullArenaDataAsync"/> — downloads the cloud copy; caller decides merge.
///   • <see cref="SyncOnLoginAsync"/> — pulls cloud data after first sign-in, merges
///     into the local cache (cloud wins for arena fields), then pushes back.
///
/// All public methods require <see cref="UGSInitializer.IsReady"/> to be true.
/// </summary>
public static class CloudSaveSync
{
    private const string ArenaDataKey = "arena_v1";
    private const float PushThrottleSeconds = 5f; // Only push once per 5 seconds

    /// <summary>True after at least one successful pull+merge.</summary>
    public static bool HasSynced { get; private set; }

    /// <summary>Timestamp of last successful push (in seconds, Time.realtimeSinceStartup).</summary>
    private static float _lastPushTimeRealtimeSecs = -PushThrottleSeconds;

    // ═════════════════════════════════════════════════════════════
    //  Push (local → cloud)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Serialises the current <see cref="ArenaSaveData"/> and writes it to Cloud Save.
    /// Throttled to once per ~5 seconds to avoid hammering the server.
    /// Call this after <see cref="SaveManager.Save"/> when online.
    /// </summary>
    public static async Task PushArenaDataAsync()
    {
        if (!IsOnlineReady()) return;

        // Throttle: skip if we pushed recently
        float now = Time.realtimeSinceStartup;
        if (now - _lastPushTimeRealtimeSecs < PushThrottleSeconds)
            return;

        try
        {
            var arena = SaveManager.GetArenaSaveData();
            if (arena == null) return;

            string json = JsonUtility.ToJson(arena);
            var data = new Dictionary<string, object> { { ArenaDataKey, json } };
            await CloudSaveService.Instance.Data.Player.SaveAsync(data);

            _lastPushTimeRealtimeSecs = now;
            Debug.Log("[CloudSaveSync] Arena data pushed to cloud.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CloudSaveSync] Push failed: {ex.Message}");
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Pull (cloud → local)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Downloads the arena data blob from Cloud Save and returns the deserialised
    /// object. Returns <c>null</c> if nothing is stored or if the call fails.
    /// Does NOT modify local save — caller is responsible for merging.
    /// </summary>
    public static async Task<ArenaSaveData> PullArenaDataAsync()
    {
        if (!IsOnlineReady()) return null;

        try
        {
            var keys = new HashSet<string> { ArenaDataKey };
            var response = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);

            if (response.TryGetValue(ArenaDataKey, out var item))
            {
                string json = item.Value.GetAs<string>();
                if (!string.IsNullOrEmpty(json))
                {
                    var cloud = JsonUtility.FromJson<ArenaSaveData>(json);
                    return cloud;
                }
            }

            Debug.Log("[CloudSaveSync] No arena data found in cloud (new player or first sync).");
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CloudSaveSync] Pull failed: {ex.Message}");
            return null;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Sync (login merge)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Called once after <see cref="UGSInitializer.OnReady"/>.
    /// Pulls cloud data, merges into local cache (cloud wins), then pushes merged result.
    /// </summary>
    public static async Task SyncOnLoginAsync()
    {
        if (HasSynced) return;

        var cloud = await PullArenaDataAsync();
        var local = SaveManager.GetArenaSaveData();
        if (local == null) return;

        if (cloud != null)
        {
            MergeCloudIntoLocal(cloud, local);
            SaveManager.Save();
            Debug.Log("[CloudSaveSync] Merged cloud data into local save.");
        }

        // Push the (possibly updated) local data so cloud is always up-to-date.
        await PushArenaDataAsync();
        HasSynced = true;

        Debug.Log("[CloudSaveSync] Login sync complete.");
    }

    // ═════════════════════════════════════════════════════════════
    //  Merge logic
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Merges cloud values into the local cache. Cloud is authoritative for
    /// identity, username, and server-managed fields. Stats use max-wins
    /// so no progress is lost from either side.
    /// </summary>
    private static void MergeCloudIntoLocal(ArenaSaveData cloud, ArenaSaveData local)
    {
        // ── Identity (cloud wins) ──
        if (!string.IsNullOrEmpty(cloud.arenaPlayerId))
            local.arenaPlayerId = cloud.arenaPlayerId;

        if (!string.IsNullOrEmpty(cloud.arenaUsername))
        {
            local.arenaUsername = cloud.arenaUsername;
            local.usernameCreated = true;
        }

        // ── Feature flags (prefer unlocked/completed) ──
        local.arenaUnlocked = local.arenaUnlocked || cloud.arenaUnlocked;
        local.unlockRewardClaimed = local.unlockRewardClaimed || cloud.unlockRewardClaimed;
        local.introCompleted = local.introCompleted || cloud.introCompleted;
        local.usernameCreated = local.usernameCreated || cloud.usernameCreated;

        // ── Tickets (cloud wins — server will be authoritative) ──
        local.arenaTickets = cloud.arenaTickets;
        local.weeklyTicketsPurchased = cloud.weeklyTicketsPurchased;
        local.lastTicketResetUtc = Math.Max(local.lastTicketResetUtc, cloud.lastTicketResetUtc);

        // ── Lifetime stats (max-wins merge — no progress lost) ──
        if (cloud.lifetimeStats != null && local.lifetimeStats != null)
        {
            var ls = local.lifetimeStats;
            var cs = cloud.lifetimeStats;

            ls.tournamentsEntered = Math.Max(ls.tournamentsEntered, cs.tournamentsEntered);
            ls.championshipsWon = Math.Max(ls.championshipsWon, cs.championshipsWon);
            ls.podiumFinishes = Math.Max(ls.podiumFinishes, cs.podiumFinishes);
            ls.totalPlacementSum = Math.Max(ls.totalPlacementSum, cs.totalPlacementSum);

            // Best placement: lower is better (1 = champion), but 0 means "never placed".
            if (cs.bestPlacementAllTime > 0)
            {
                if (ls.bestPlacementAllTime <= 0 || cs.bestPlacementAllTime < ls.bestPlacementAllTime)
                    ls.bestPlacementAllTime = cs.bestPlacementAllTime;
            }

            ls.highestRankThisMonth = Math.Max(ls.highestRankThisMonth, cs.highestRankThisMonth);
            ls.currentMonthTournamentsEntered = Math.Max(ls.currentMonthTournamentsEntered, cs.currentMonthTournamentsEntered);
        }

        // ── Tournament history (cloud wins — server is source of truth) ──
        if (cloud.recentTournamentHistory != null && cloud.recentTournamentHistory.Count > 0)
        {
            local.recentTournamentHistory = cloud.recentTournamentHistory;
        }

        // ── Current tournament cache (cloud wins) ──
        if (cloud.currentTournamentCache != null
            && !string.IsNullOrEmpty(cloud.currentTournamentCache.tournamentId))
        {
            local.currentTournamentCache = cloud.currentTournamentCache;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════

    private static bool IsOnlineReady()
    {
        if (UGSInitializer.I == null || !UGSInitializer.I.IsReady)
        {
            Debug.LogWarning("[CloudSaveSync] UGS not ready — skipping cloud operation.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Force an immediate push to cloud, bypassing throttle.
    /// Use this on critical moments like app pause/quit.
    /// </summary>
    public static async Task ForcePushArenaDataAsync()
    {
        _lastPushTimeRealtimeSecs = -PushThrottleSeconds; // Reset throttle
        await PushArenaDataAsync();
    }
}
