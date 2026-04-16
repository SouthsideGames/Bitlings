// ArenaModule — GetTournamentBracket handler
// Port of GetTournamentBracket.js to C# Cloud Code Module.
//
// Returns the calling player's bracket assignment for a given week.
// If brackets haven't been built yet but it's past the lock time,
// triggers bracket building lazily (same logic as LockAndAssignBrackets).

using System.Text.Json;
using ArenaModule.Helpers;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace ArenaModule.Handlers;

public class GetTournamentBracket
{
    private readonly ILogger<GetTournamentBracket> _logger;

    public GetTournamentBracket(ILogger<GetTournamentBracket> logger)
    {
        _logger = logger;
    }

    [CloudCodeFunction("GetTournamentBracket")]
    public async Task<GetBracketResult> Execute(
        IExecutionContext ctx,
        IGameApiClient api,
        string weekId)
    {
        if (string.IsNullOrWhiteSpace(weekId))
            return NotAssigned("Invalid week ID.");

        var lockEntity = $"tournament_lock_{weekId}";
        bool bracketsBuilt = false;

        // ── Check if brackets are built ──

        try
        {
            var lockResult = await api.CloudSaveData.GetCustomItemsAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, lockEntity);

            if (lockResult?.Data?.Results != null)
            {
                foreach (var item in lockResult.Data.Results)
                {
                    if (item.Key == "status" && item.Value?.ToString() == "done")
                    {
                        bracketsBuilt = true;
                        break;
                    }
                }
            }
        }
        catch (ApiException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Not locked yet
        }
        catch (Exception ex)
        {
            _logger.LogError("Error checking lock: {Error}", ex.Message);
        }

        // ── If not built and past lock time, build now (lazy lock) ──

        if (!bracketsBuilt)
        {
            if (!ScheduleHelper.IsPastLockTime())
                return NotAssigned("Brackets haven't been assigned yet. Check back Wednesday.");

            _logger.LogInformation("Lazy-locking week {WeekId}...", weekId);
            bool lockSuccess = await LazyBuildBrackets(api, ctx, weekId);
            if (!lockSuccess)
                return NotAssigned("Error building brackets. Try again.");
            bracketsBuilt = true;
        }

        // ── Look up player's bracket assignment ──

        var playerMapEntity = $"tournament_player_map_{weekId}";
        PlayerMapping? mapping = null;

        try
        {
            var mapResult = await api.CloudSaveData.GetCustomItemsAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, playerMapEntity);

            if (mapResult?.Data?.Results != null)
            {
                foreach (var item in mapResult.Data.Results)
                {
                    if (item.Key == ctx.PlayerId)
                    {
                        var raw = item.Value?.ToString();
                        if (!string.IsNullOrEmpty(raw))
                            mapping = JsonSerializer.Deserialize<PlayerMapping>(raw);
                        break;
                    }
                }
            }
        }
        catch (ApiException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Player map entity doesn't exist
        }
        catch (Exception ex)
        {
            _logger.LogError("Error reading player map: {Error}", ex.Message);
            return NotAssigned("Server error. Try again.");
        }

        if (mapping == null)
            return NotAssigned("You are not registered for this week's tournament.");

        // ── Fetch bracket data ──

        var bracketsEntity = $"tournament_brackets_{weekId}";
        BracketData? bracketData = null;

        try
        {
            var bracketResult = await api.CloudSaveData.GetCustomItemsAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, bracketsEntity);

            if (bracketResult?.Data?.Results != null)
            {
                foreach (var item in bracketResult.Data.Results)
                {
                    if (item.Key == mapping.TournamentId)
                    {
                        var raw = item.Value?.ToString();
                        if (!string.IsNullOrEmpty(raw))
                            bracketData = JsonSerializer.Deserialize<BracketData>(raw);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error reading bracket: {Error}", ex.Message);
            return NotAssigned("Server error reading bracket.");
        }

        if (bracketData == null)
            return NotAssigned("Bracket data not found. Try again later.");

        return new GetBracketResult
        {
            Assigned = true,
            EntryId = mapping.EntryId,
            Bracket = bracketData
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Lazy bracket building (reuses LockAndAssignBrackets logic)
    // ═══════════════════════════════════════════════════════════

    private async Task<bool> LazyBuildBrackets(IGameApiClient api, IExecutionContext ctx, string weekId)
    {
        var lockEntity = $"tournament_lock_{weekId}";

        var registrations = await LockAndAssignBrackets.ReadRegistrations(api, ctx, weekId);
        if (registrations == null)
            return false;

        if (registrations.Count == 0)
        {
            await LockAndAssignBrackets.WriteLock(api, ctx, lockEntity);
            return true;
        }

        // Group by band
        var pools = new List<List<RegistrationData>> { new(), new(), new(), new() };
        foreach (var reg in registrations)
            pools[System.Math.Clamp(reg.ScoreBand, 0, 3)].Add(reg);

        // Merge small bands
        for (int band = 0; band <= 3; band++)
        {
            if (pools[band].Count > 0 && pools[band].Count < BracketHelper.MinRealForMerge)
            {
                int target = BracketHelper.FindMergeTarget(pools, band);
                if (target != -1 && target != band)
                {
                    pools[target].AddRange(pools[band]);
                    pools[band].Clear();
                }
            }
        }

        // Create brackets
        var bracketsEntity = $"tournament_brackets_{weekId}";
        var playerMapEntity = $"tournament_player_map_{weekId}";
        long weekStartUtc = ScheduleHelper.WeekIdToEpoch(weekId);
        long weekEndUtc = weekStartUtc + 7 * 24 * 60 * 60 - 1;
        int totalBrackets = 0;

        for (int band = 0; band <= 3; band++)
        {
            var pool = pools[band];
            if (pool.Count == 0) continue;

            new Prng(ScheduleHelper.HashCode($"{weekId}_{band}")).Shuffle(pool);

            for (int i = 0; i < pool.Count; i += BracketHelper.BracketSize)
            {
                var chunk = pool.GetRange(i, System.Math.Min(BracketHelper.BracketSize, pool.Count - i));
                string tournamentId = $"T{weekId}_{BracketHelper.BandNames[band]}_{totalBrackets}";
                int bracketSeed = ScheduleHelper.HashCode($"{tournamentId}_seed");

                var realEntries = new List<BracketEntry>();
                for (int idx = 0; idx < chunk.Count; idx++)
                {
                    var reg = chunk[idx];
                    realEntries.Add(new BracketEntry
                    {
                        EntryId = $"{tournamentId}_E_{idx}",
                        PlayerId = reg.PlayerId,
                        DisplayName = reg.DisplayName,
                        TeamSnapshotJson = reg.TeamSnapshotJson,
                        ArenaScore = reg.ArenaScore,
                        IsBot = false
                    });
                }

                var bracketData = new BracketData
                {
                    TournamentId = tournamentId,
                    WeekStartUtc = weekStartUtc,
                    WeekEndUtc = weekEndUtc,
                    ScoreBand = band,
                    BracketSeed = bracketSeed,
                    RealEntries = realEntries,
                    RealPlayerCount = realEntries.Count,
                    BotsNeeded = BracketHelper.BracketSize - realEntries.Count
                };

                try
                {
                    var json = JsonSerializer.Serialize(bracketData);
                    await api.CloudSaveData.SetCustomItemAsync(
                        ctx, ctx.ServiceToken, ctx.ProjectId, bracketsEntity,
                        new SetItemBody(tournamentId, json));
                }
                catch (Exception ex)
                {
                    _logger.LogError("Failed to write bracket {TournamentId}: {Error}", tournamentId, ex.Message);
                    return false;
                }

                foreach (var entry in realEntries)
                {
                    try
                    {
                        var mapJson = JsonSerializer.Serialize(new PlayerMapping
                        {
                            TournamentId = tournamentId,
                            EntryId = entry.EntryId,
                            ScoreBand = band
                        });
                        await api.CloudSaveData.SetCustomItemAsync(
                            ctx, ctx.ServiceToken, ctx.ProjectId, playerMapEntity,
                            new SetItemBody(entry.PlayerId, mapJson));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Failed to map player {PlayerId}: {Error}", entry.PlayerId, ex.Message);
                    }
                }

                totalBrackets++;
            }
        }

        await LockAndAssignBrackets.WriteLock(api, ctx, lockEntity);
        _logger.LogInformation("Lazy-lock complete: {Count} bracket(s).", totalBrackets);
        return true;
    }

    private static GetBracketResult NotAssigned(string reason)
        => new() { Assigned = false, Reason = reason };
}
