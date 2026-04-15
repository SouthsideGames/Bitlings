using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using UnityEngine;

/// <summary>
/// Client-side wrapper for all UGS Cloud Code calls used by the Arena system.
/// Each method maps to a server-side Cloud Code function.
/// </summary>
public static class ArenaCloudCodeService
{
    // ═════════════════════════════════════════════════════════════
    //  Username validation
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Result of a server-side username validation + registration attempt.
    /// </summary>
    public struct UsernameResult
    {
        public bool success;
        public string error;
    }

    /// <summary>
    /// Asks the server to validate and register the given username.
    /// The server checks uniqueness against a global index and, if available,
    /// writes it to the player's Cloud Save data.
    /// </summary>
    public static async Task<UsernameResult> ValidateAndSetUsernameAsync(string username)
    {
        if (!ArenaNetworkGuard.IsOnline)
            return new UsernameResult { success = false, error = "No connection. Try again later." };

        try
        {
            var args = new Dictionary<string, object> { { "username", username } };
            var response = await CloudCodeService.Instance.CallEndpointAsync<UsernameResponse>(
                "ValidateAndSetUsername", args);

            return new UsernameResult
            {
                success = response.success,
                error = response.error
            };
        }
        catch (CloudCodeException ex)
        {
            Debug.LogWarning($"[ArenaCloudCodeService] ValidateAndSetUsername failed: {ex.Message}");
            return new UsernameResult { success = false, error = "Server error. Please try again." };
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArenaCloudCodeService] ValidateAndSetUsername unexpected: {ex.Message}");
            return new UsernameResult { success = false, error = "Something went wrong. Please try again." };
        }
    }

    /// <summary>
    /// JSON shape returned by the ValidateAndSetUsername Cloud Code endpoint.
    /// </summary>
    [Serializable]
    private class UsernameResponse
    {
        public bool success;
        public string error;
    }

    // ═════════════════════════════════════════════════════════════
    //  Tournament registration
    // ═════════════════════════════════════════════════════════════

    public struct TournamentRegistrationResult
    {
        public bool success;
        public string error;
        public string weekId;
    }

    /// <summary>
    /// Registers the player for the current week's tournament.
    /// Sends the frozen team snapshot to the server for bracket assignment.
    /// </summary>
    public static async Task<TournamentRegistrationResult> RegisterForTournamentAsync(
        string teamSnapshotJson, int arenaScore, int scoreBand, string displayName, string weekId)
    {
        if (!ArenaNetworkGuard.IsOnline)
            return new TournamentRegistrationResult { success = false, error = "No connection. Try again later." };

        try
        {
            var args = new Dictionary<string, object>
            {
                { "teamSnapshotJson", teamSnapshotJson },
                { "arenaScore", arenaScore },
                { "scoreBand", scoreBand },
                { "displayName", displayName },
                { "weekId", weekId }
            };
            var response = await CloudCodeService.Instance.CallEndpointAsync<RegisterResponse>(
                "RegisterForTournament", args);

            return new TournamentRegistrationResult
            {
                success = response.success,
                error = response.error,
                weekId = response.weekId
            };
        }
        catch (CloudCodeException ex)
        {
            Debug.LogWarning($"[ArenaCloudCodeService] RegisterForTournament failed: {ex.Message}");
            return new TournamentRegistrationResult { success = false, error = "Server error. Please try again." };
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArenaCloudCodeService] RegisterForTournament unexpected: {ex.Message}");
            return new TournamentRegistrationResult { success = false, error = "Something went wrong. Please try again." };
        }
    }

    [Serializable]
    private class RegisterResponse
    {
        public bool success;
        public string error;
        public string weekId;
    }

    // ═════════════════════════════════════════════════════════════
    //  Bracket retrieval
    // ═════════════════════════════════════════════════════════════

    public struct BracketResult
    {
        public bool assigned;
        public string reason;
        public string entryId;
        public BracketData bracket;
    }

    [Serializable]
    public class BracketData
    {
        public string tournamentId;
        public long weekStartUtc;
        public long weekEndUtc;
        public int scoreBand;
        public int bracketSeed;
        public List<BracketRealEntry> realEntries;
        public int realPlayerCount;
        public int botsNeeded;
    }

    [Serializable]
    public class BracketRealEntry
    {
        public string entryId;
        public string playerId;
        public string displayName;
        public string teamSnapshotJson;
        public int arenaScore;
        public bool isBot;
    }

    /// <summary>
    /// Asks the server for the player's bracket assignment for the given week.
    /// If brackets haven't been built yet and it's past lock time, the server builds them lazily.
    /// </summary>
    public static async Task<BracketResult> GetTournamentBracketAsync(string weekId)
    {
        if (!ArenaNetworkGuard.IsOnline)
            return new BracketResult { assigned = false, reason = "No connection." };

        try
        {
            var args = new Dictionary<string, object> { { "weekId", weekId } };
            var response = await CloudCodeService.Instance.CallEndpointAsync<BracketResponse>(
                "GetTournamentBracket", args);

            if (!response.assigned)
                return new BracketResult { assigned = false, reason = response.reason };

            return new BracketResult
            {
                assigned = true,
                entryId = response.entryId,
                bracket = response.bracket
            };
        }
        catch (CloudCodeException ex)
        {
            Debug.LogWarning($"[ArenaCloudCodeService] GetTournamentBracket failed: {ex.Message}");
            return new BracketResult { assigned = false, reason = "Server error. Try again." };
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArenaCloudCodeService] GetTournamentBracket unexpected: {ex.Message}");
            return new BracketResult { assigned = false, reason = "Something went wrong." };
        }
    }

    [Serializable]
    private class BracketResponse
    {
        public bool assigned;
        public string reason;
        public string entryId;
        public BracketData bracket;
    }
}
