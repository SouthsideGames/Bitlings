// ArenaModule — SubmitTournamentResult handler
//
// Server-authoritative tournament scoring. Closes the "client writes its own
// leaderboard score" hole: the client no longer calls the Leaderboards API. It
// submits the FULL final standings it computed (ordered entryIds, champion
// first) plus its own claimed placement, and the server:
//
//   1. verifies the caller belongs to the tournament (via the server-authored
//      tournament_player_map_{weekId}) with the claimed entryId, and that the
//      claimed placement matches the player's position in the submitted standings;
//   2. cross-checks the submitted standings against OTHER real players in the
//      same bracket. Because every honest client resolves the identical
//      deterministic bracket, honest submissions share a standings hash; a lone
//      cheater's forged standings never reaches quorum and is rejected;
//   3. once a standings hash reaches quorum (or the bracket is too small for
//      quorum to exist), the server itself writes the weekly placement score and
//      maintains an authoritative all-time championship counter.
//
// Deploy with a dashboard Access-Control policy that denies player tokens write
// access to arena_weekly / arena_alltime, so this handler is the only writer.
//
// Cloud Save entities:
//   "tournament_player_map_{weekId}" — key playerId  → PlayerMapping     (written by LockAndAssignBrackets)
//   "tournament_brackets_{weekId}"   — key tournamentId → BracketData     (has RealPlayerCount)
//   "tournament_results_{weekId}"    — key playerId  → StoredResult
//   "tournament_scored_{weekId}"     — key playerId  → "done"             (idempotency guard)
//   "arena_championships"            — key playerId  → career championship count (authoritative)

using System.Text.Json;
using ArenaModule.Helpers;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;
using Unity.Services.Leaderboards.Model;

namespace ArenaModule.Handlers;

public class SubmitTournamentResult
{
    private const string WeeklyLeaderboardId = "arena_weekly";
    private const string AllTimeLeaderboardId = "arena_alltime";
    private const string ChampionshipsEntity = "arena_championships";

    private const int BracketSize = 32;

    // Minimum real players (including the submitter) whose independently computed
    // standings must agree before a result is trusted. Brackets with fewer real
    // players than this can never reach quorum and fall back to single-submission
    // trust (logged).
    private const int ConsensusQuorum = 2;

    private readonly ILogger<SubmitTournamentResult> _logger;

    public SubmitTournamentResult(ILogger<SubmitTournamentResult> logger)
    {
        _logger = logger;
    }

    [CloudCodeFunction("SubmitTournamentResult")]
    public async Task<SubmitResultOutcome> Execute(
        IExecutionContext ctx,
        IGameApiClient api,
        string weekId,
        string tournamentId,
        string entryId,
        int placement,
        string standingsJson)
    {
        // ── Parameter validation ──
        if (string.IsNullOrWhiteSpace(weekId) || !weekId.StartsWith('W'))
            return Rejected("Invalid week ID.");
        if (string.IsNullOrWhiteSpace(tournamentId))
            return Rejected("Invalid tournament ID.");
        if (string.IsNullOrWhiteSpace(entryId))
            return Rejected("Invalid entry ID.");
        if (placement < 1 || placement > BracketSize)
            return Rejected("Invalid placement.");
        if (string.IsNullOrWhiteSpace(standingsJson))
            return Rejected("Standings are required.");

        List<string>? standings;
        try
        {
            standings = JsonSerializer.Deserialize<List<string>>(standingsJson);
        }
        catch
        {
            return Rejected("Malformed standings.");
        }
        if (standings == null || standings.Count == 0)
            return Rejected("Standings must be a non-empty array.");

        var playerId = ctx.PlayerId;

        // ── Idempotency: already scored this week? ──
        var scoredEntity = $"tournament_scored_{weekId}";
        if (await GetItem(api, ctx, scoredEntity, playerId) != null)
            return new SubmitResultOutcome { Status = "already_scored" };

        // ── Authority: the player must be mapped to THIS tournament with THIS entryId ──
        var mapEntity = $"tournament_player_map_{weekId}";
        var mappingRaw = await GetItem(api, ctx, mapEntity, playerId);
        if (mappingRaw == null)
            return Rejected("You are not registered for this week.");

        PlayerMapping? mapping;
        try { mapping = JsonSerializer.Deserialize<PlayerMapping>(mappingRaw); }
        catch { return Rejected("Server mapping unreadable."); }

        if (mapping == null || mapping.TournamentId != tournamentId || mapping.EntryId != entryId)
            return Rejected("Tournament/entry mismatch.");

        // ── Internal consistency: claimed placement must match the player's own
        //    position in the submitted standings. ──
        int claimedIndex = standings.IndexOf(entryId);
        if (claimedIndex == -1)
            return Rejected("Your entry is absent from the standings.");
        if (claimedIndex + 1 != placement)
            return Rejected("Claimed placement disagrees with submitted standings.");

        // ── Store this submission (normalized hash so formatting can't split votes) ──
        int standingsHash = ScheduleHelper.HashCode(JsonSerializer.Serialize(standings));
        var resultsEntity = $"tournament_results_{weekId}";
        var stored = new StoredResult
        {
            TournamentId = tournamentId,
            StandingsHash = standingsHash,
            Placement = placement,
            EntryId = entryId,
            SubmittedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        try
        {
            await api.CloudSaveData.SetCustomItemAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, resultsEntity,
                new SetItemBody(playerId, JsonSerializer.Serialize(stored)));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to store result for {PlayerId}: {Error}", playerId, ex.Message);
            return Rejected("Server error saving result.");
        }

        // ── Tally standings hashes across all submissions for this tournament ──
        int matchingVotes = 0;
        int bestVotes = 0;
        int bestHash = 0;
        var hashVotes = new Dictionary<int, int>();

        foreach (var value in await GetAllValues(api, ctx, resultsEntity))
        {
            StoredResult? r;
            try { r = JsonSerializer.Deserialize<StoredResult>(value); }
            catch { continue; }
            if (r == null || r.TournamentId != tournamentId) continue;

            hashVotes.TryGetValue(r.StandingsHash, out int v);
            hashVotes[r.StandingsHash] = v + 1;
            if (r.StandingsHash == standingsHash) matchingVotes++;
        }
        foreach (var kv in hashVotes)
        {
            if (kv.Value > bestVotes) { bestVotes = kv.Value; bestHash = kv.Key; }
        }

        int realPlayerCount = await GetBracketRealPlayerCount(api, ctx, weekId, tournamentId);
        int effectiveQuorum = Math.Min(ConsensusQuorum, Math.Max(1, realPlayerCount));

        bool consensusReached = bestHash == standingsHash && matchingVotes >= effectiveQuorum;

        if (!consensusReached)
        {
            if (bestHash != standingsHash && bestVotes >= effectiveQuorum)
            {
                // A different standings already reached quorum → this is a minority
                // (probably tampered) submission. Reject.
                _logger.LogWarning(
                    "Player {PlayerId} standings disagree with quorum for {Tournament} (theirs={Mine}, quorum={Quorum}).",
                    playerId, tournamentId, matchingVotes, bestVotes);
                return Rejected("Result did not match bracket consensus.");
            }
            // Not enough corroboration yet — keep the submission and ask the client to retry.
            return new SubmitResultOutcome { Status = "pending", Placement = placement };
        }

        if (realPlayerCount < ConsensusQuorum)
        {
            _logger.LogInformation(
                "Bracket {Tournament} has {Count} real player(s); scoring {PlayerId} on single-submission trust.",
                tournamentId, realPlayerCount, playerId);
        }

        // ── Consensus reached: server writes the score. ──
        int weeklyScore = BracketSize + 1 - placement; // 1st → 32, 32nd → 1

        try
        {
            await WriteLeaderboardScore(api, ctx, WeeklyLeaderboardId, playerId, weeklyScore);
        }
        catch (Exception ex)
        {
            _logger.LogError("Weekly score write failed for {PlayerId}: {Error}", playerId, ex.Message);
            return Rejected("Server error writing score.");
        }

        if (placement == 1)
        {
            try
            {
                int champCount = await BumpChampionships(api, ctx, playerId);
                await WriteLeaderboardScore(api, ctx, AllTimeLeaderboardId, playerId, champCount);
            }
            catch (Exception ex)
            {
                // Weekly already written; don't fail the whole call for the all-time bump.
                _logger.LogError("All-time write failed for {PlayerId}: {Error}", playerId, ex.Message);
            }
        }

        // ── Mark scored so re-submits are no-ops ──
        try
        {
            await api.CloudSaveData.SetCustomItemAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, scoredEntity,
                new SetItemBody(playerId, "done"));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to mark {PlayerId} scored: {Error}", playerId, ex.Message);
        }

        _logger.LogInformation(
            "Scored {PlayerId}: {Tournament} placement {Placement} → weekly {Score}.",
            playerId, tournamentId, placement, weeklyScore);

        return new SubmitResultOutcome { Status = "scored", Placement = placement, Score = weeklyScore };
    }

    // ═══════════════════════════════════════════════════════════
    //  Leaderboard write (server-authoritative)
    // ═══════════════════════════════════════════════════════════

    private static async Task WriteLeaderboardScore(
        IGameApiClient api, IExecutionContext ctx, string leaderboardId, string playerId, double score)
    {
        await api.Leaderboards.AddLeaderboardPlayerScoreAsync(
            ctx,
            ctx.ServiceToken,
            Guid.Parse(ctx.ProjectId),
            leaderboardId,
            playerId,
            new LeaderboardScore(score));
    }

    // ═══════════════════════════════════════════════════════════
    //  Cloud Save helpers
    // ═══════════════════════════════════════════════════════════

    private async Task<int> BumpChampionships(IGameApiClient api, IExecutionContext ctx, string playerId)
    {
        int count = 0;
        var existing = await GetItem(api, ctx, ChampionshipsEntity, playerId);
        if (existing != null && int.TryParse(existing, out int n) && n > 0)
            count = n;
        count += 1;
        await api.CloudSaveData.SetCustomItemAsync(
            ctx, ctx.ServiceToken, ctx.ProjectId, ChampionshipsEntity,
            new SetItemBody(playerId, count.ToString()));
        return count;
    }

    private async Task<int> GetBracketRealPlayerCount(
        IGameApiClient api, IExecutionContext ctx, string weekId, string tournamentId)
    {
        var raw = await GetItem(api, ctx, $"tournament_brackets_{weekId}", tournamentId);
        if (raw == null) return 0;
        try
        {
            var b = JsonSerializer.Deserialize<BracketData>(raw);
            if (b == null) return 0;
            if (b.RealPlayerCount > 0) return b.RealPlayerCount;
            return b.RealEntries?.Count ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError("Bad bracket data for {Tournament}: {Error}", tournamentId, ex.Message);
            return 0;
        }
    }

    /// <summary>Returns the value for a single key in an entity, or null.</summary>
    private async Task<string?> GetItem(IGameApiClient api, IExecutionContext ctx, string entity, string key)
    {
        try
        {
            var res = await api.CloudSaveData.GetCustomItemsAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, entity);
            if (res?.Data?.Results != null)
            {
                foreach (var item in res.Data.Results)
                {
                    if (item.Key == key)
                        return item.Value?.ToString();
                }
            }
        }
        catch (ApiException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // entity doesn't exist yet
        }
        catch (Exception ex)
        {
            _logger.LogError("GetItem({Entity},{Key}) error: {Error}", entity, key, ex.Message);
        }
        return null;
    }

    /// <summary>Returns all item values in an entity (JSON strings).</summary>
    private async Task<List<string>> GetAllValues(IGameApiClient api, IExecutionContext ctx, string entity)
    {
        var values = new List<string>();
        try
        {
            var res = await api.CloudSaveData.GetCustomItemsAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, entity);
            if (res?.Data?.Results != null)
            {
                foreach (var item in res.Data.Results)
                {
                    var v = item.Value?.ToString();
                    if (!string.IsNullOrEmpty(v)) values.Add(v);
                }
            }
        }
        catch (ApiException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // entity doesn't exist yet
        }
        catch (Exception ex)
        {
            _logger.LogError("GetAllValues({Entity}) error: {Error}", entity, ex.Message);
        }
        return values;
    }

    private static SubmitResultOutcome Rejected(string reason)
        => new() { Status = "rejected", Reason = reason };
}
