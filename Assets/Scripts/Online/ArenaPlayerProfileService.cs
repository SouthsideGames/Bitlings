// Assets/Scripts/Online/ArenaPlayerProfileService.cs
// BRN Arena v1 — Manages the player's public profile in Cloud Save.
//
// A lightweight public profile is stored as a Cloud Save player key,
// updated after each tournament completion. Other players can query
// this profile when viewing leaderboard entries.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using UnityEngine;

/// <summary>
/// Serialisable public profile stored in Cloud Save.
/// </summary>
[Serializable]
public class ArenaPublicProfile
{
    public string playerId;
    public string displayName;
    public int championshipsWon;
    public int podiumFinishes;
    public int bestPlacement;
    public int tournamentsPlayed;
    public long lastUpdatedUtc;
}

/// <summary>
/// Reads and writes the player's public arena profile to UGS Cloud Save.
/// </summary>
public static class ArenaPlayerProfileService
{
    const string TAG = "[ArenaProfile]";
    const string ProfileKey = "arena_profile_v1";

    // ═════════════════════════════════════════════════════════════
    //  Write
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a public profile from the local arena save data and pushes it
    /// to Cloud Save. Call after tournament completion.
    /// </summary>
    public static async Task PublishProfileAsync()
    {
        if (!ArenaNetworkGuard.IsOnline) return;

        try
        {
            var arena = SaveManager.GetArenaSaveData();
            if (arena == null) return;

            var stats = arena.lifetimeStats ?? new ArenaLifetimeStats();
            var profile = new ArenaPublicProfile
            {
                playerId = AuthenticationService.Instance.PlayerId,
                displayName = arena.arenaUsername ?? "",
                championshipsWon = stats.championshipsWon,
                podiumFinishes = stats.podiumFinishes,
                bestPlacement = stats.bestPlacementAllTime,
                tournamentsPlayed = stats.tournamentsEntered,
                lastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            string json = JsonUtility.ToJson(profile);
            var data = new Dictionary<string, object> { { ProfileKey, json } };
            await CloudSaveService.Instance.Data.Player.SaveAsync(data);

            DevLog.Log($"{TAG} Public profile published.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{TAG} Failed to publish profile: {ex.Message}");
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Read (local player)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetches the local player's own public profile from Cloud Save.
    /// Returns null if not found or offline.
    /// </summary>
    public static async Task<ArenaPublicProfile> GetOwnProfileAsync()
    {
        if (!ArenaNetworkGuard.IsOnline) return null;

        try
        {
            var keys = new HashSet<string> { ProfileKey };
            var result = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);

            if (result != null && result.TryGetValue(ProfileKey, out var item))
            {
                string json = item.Value.GetAs<string>();
                return JsonUtility.FromJson<ArenaPublicProfile>(json);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{TAG} Failed to load own profile: {ex.Message}");
        }

        return null;
    }
}
