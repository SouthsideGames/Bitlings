using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
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
            var response = await CloudCodeService.Instance.CallModuleEndpointAsync<UsernameResponse>(
                "ArenaModule", "ValidateAndSetUsername", args);

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
            var response = await CloudCodeService.Instance.CallModuleEndpointAsync<RegisterResponse>(
                "ArenaModule", "RegisterForTournament", args);

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
    //  Tournament result submission (server-authoritative scoring)
    // ═════════════════════════════════════════════════════════════

    /// <summary>Outcome of a server-side tournament result submission.</summary>
    public struct SubmitResultOutcome
    {
        /// <summary>Server status: "scored", "pending", "already_scored", "rejected", or "" on transport failure.</summary>
        public string status;
        public int placement;
        public int score;
        public string reason;

        /// <summary>True once the server has finished with this submission (no retry needed).</summary>
        public bool IsTerminal =>
            status == "scored" || status == "already_scored" || status == "rejected";

        /// <summary>True when the server accepted the submission but is awaiting corroboration.</summary>
        public bool IsPending => status == "pending";
    }

    /// <summary>
    /// Submits the player's computed final standings to the server, which verifies
    /// them against other real players in the bracket and — on consensus — writes
    /// the leaderboard score itself. Replaces direct client leaderboard writes.
    /// </summary>
    public static async Task<SubmitResultOutcome> SubmitTournamentResultAsync(
        string weekId, string tournamentId, string entryId, int placement, string standingsJson)
    {
        if (!ArenaNetworkGuard.IsOnline)
            return new SubmitResultOutcome { status = "", reason = "No connection." };

        try
        {
            var args = new Dictionary<string, object>
            {
                { "weekId", weekId },
                { "tournamentId", tournamentId },
                { "entryId", entryId },
                { "placement", placement },
                { "standingsJson", standingsJson }
            };
            var response = await CloudCodeService.Instance.CallModuleEndpointAsync<SubmitResultResponse>(
                "ArenaModule", "SubmitTournamentResult", args);

            return new SubmitResultOutcome
            {
                status = response.status,
                placement = response.placement,
                score = response.score,
                reason = response.reason
            };
        }
        catch (CloudCodeException ex)
        {
            Debug.LogWarning($"[ArenaCloudCodeService] SubmitTournamentResult failed: {ex.Message}");
            return new SubmitResultOutcome { status = "", reason = "Server error." };
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArenaCloudCodeService] SubmitTournamentResult unexpected ({ex.GetType().Name}): {ex.Message}");
            return new SubmitResultOutcome { status = "", reason = "Something went wrong." };
        }
    }

    [Serializable]
    private class SubmitResultResponse
    {
        public string status;
        public int placement;
        public int score;
        public string reason;
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
    /// Uses JObject deserialization to bypass the UGS SDK GetAs&lt;T&gt;() string-roundtrip that
    /// can throw a JsonReaderException for complex nested response types.
    /// </summary>
    public static async Task<BracketResult> GetTournamentBracketAsync(string weekId)
    {
        if (!ArenaNetworkGuard.IsOnline)
            return new BracketResult { assigned = false, reason = "No connection." };

        try
        {
            var args = new Dictionary<string, object> { { "weekId", weekId } };

            // Use JObject to bypass the UGS SDK's GetAs<T>() string-roundtrip, which can
            // produce a JsonReaderException for complex nested response types.
            var raw = await CloudCodeService.Instance.CallModuleEndpointAsync<JObject>(
                "ArenaModule", "GetTournamentBracket", args);

            if (raw == null)
                return new BracketResult { assigned = false, reason = "Empty response from server." };

            bool assigned = raw.Value<bool>("assigned");
            if (!assigned)
                return new BracketResult { assigned = false, reason = raw.Value<string>("reason") };

            string entryId = raw.Value<string>("entryId");
            JToken bracketToken = raw["bracket"];

            if (bracketToken == null || bracketToken.Type == JTokenType.Null)
                return new BracketResult { assigned = false, reason = "No bracket data in response." };

            // JToken.ToObject<T>() converts directly from the token without a string roundtrip.
            var bracket = bracketToken.ToObject<BracketData>();
            if (bracket == null)
                return new BracketResult { assigned = false, reason = "Failed to parse bracket data." };

            return new BracketResult { assigned = true, entryId = entryId, bracket = bracket };
        }
        catch (CloudCodeException ex)
        {
            Debug.LogWarning($"[ArenaCloudCodeService] GetTournamentBracket failed: {ex.Message}");
            return new BracketResult { assigned = false, reason = "Server error. Try again." };
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ArenaCloudCodeService] GetTournamentBracket unexpected ({ex.GetType().Name}): {ex.Message}");
            return new BracketResult { assigned = false, reason = "Something went wrong." };
        }
    }
}
