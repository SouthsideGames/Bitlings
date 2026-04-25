// ArenaModule — LockAndAssignBrackets handler
// Port of LockAndAssignBrackets.js to C# Cloud Code Module.
//
// Closes registration for a tournament week and assigns players to brackets.
// Idempotent — re-running for the same weekId is a no-op if already locked.
//
// Cloud Save Custom Data entities written:
//   "tournament_brackets_{weekId}"    — key: tournamentId, value: bracket JSON
//   "tournament_player_map_{weekId}"  — key: playerId,     value: tournamentId
//   "tournament_lock_{weekId}"        — key: "status",     value: "done"

using System.Text.Json;
using ArenaModule.Helpers;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace ArenaModule.Handlers;

public class LockAndAssignBrackets
{
    private readonly ILogger<LockAndAssignBrackets> _logger;

    public LockAndAssignBrackets(ILogger<LockAndAssignBrackets> logger)
    {
        _logger = logger;
    }

    [CloudCodeFunction("LockAndAssignBrackets")]
    public async Task<LockResult> Execute(
        IExecutionContext ctx,
        IGameApiClient api,
        string weekId)
    {
        if (string.IsNullOrWhiteSpace(weekId))
            return Fail("weekId is required.");

        var lockEntity = $"tournament_lock_{weekId}";

        // ── Check if already locked ──

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
                        _logger.LogInformation("Week {WeekId} already locked — no-op.", weekId);
                        return new LockResult { Success = true, AlreadyLocked = true };
                    }
                }
            }
        }
        catch (ApiException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Entity doesn't exist yet — not locked, continue
        }
        catch (Exception ex)
        {
            _logger.LogError("Error checking lock status: {Error}", ex.Message);
        }

        // ── Read all registrations ──

        var registrations = await ReadRegistrations(api, ctx, weekId);

        if (registrations == null)
            return Fail("Server error reading registrations.");

        _logger.LogInformation("Found {Count} registration(s) for {WeekId}.", registrations.Count, weekId);

        if (registrations.Count == 0)
        {
            await WriteLock(api, ctx, lockEntity);
            return new LockResult { Success = true, BracketCount = 0, PlayerCount = 0 };
        }

        // ── Group by score band ──

        var pools = new List<List<RegistrationData>> { new(), new(), new(), new() };
        foreach (var reg in registrations)
        {
            int band = System.Math.Clamp(reg.ScoreBand, 0, 3);
            pools[band].Add(reg);
        }

        // ── Merge small bands ──

        for (int band = 0; band <= 3; band++)
        {
            if (pools[band].Count > 0 && pools[band].Count < BracketHelper.MinRealForMerge)
            {
                int target = BracketHelper.FindMergeTarget(pools, band);
                if (target != -1 && target != band)
                {
                    _logger.LogInformation(
                        "Merging {SrcBand} ({SrcCount}) into {DstBand}",
                        BracketHelper.BandNames[band], pools[band].Count, BracketHelper.BandNames[target]);
                    pools[target].AddRange(pools[band]);
                    pools[band].Clear();
                }
            }
        }

        // ── Create brackets ──

        var bracketsEntity = $"tournament_brackets_{weekId}";
        var playerMapEntity = $"tournament_player_map_{weekId}";
        long weekStartUtc = ScheduleHelper.WeekIdToEpoch(weekId);
        long weekEndUtc = weekStartUtc + 7 * 24 * 60 * 60 - 1;
        int totalBrackets = 0;

        for (int band = 0; band <= 3; band++)
        {
            var pool = pools[band];
            if (pool.Count == 0) continue;

            // Shuffle deterministically
            int poolSeed = ScheduleHelper.HashCode($"{weekId}_{band}");
            new Prng(poolSeed).Shuffle(pool);

            // Split into 32-player bracket chunks
            for (int i = 0; i < pool.Count; i += BracketHelper.BracketSize)
            {
                var chunk = pool.GetRange(i, System.Math.Min(BracketHelper.BracketSize, pool.Count - i));
                int bracketIndex = totalBrackets;
                string tournamentId = $"T{weekId}_{BracketHelper.BandNames[band]}_{bracketIndex}";
                int bracketSeed = ScheduleHelper.HashCode($"{tournamentId}_seed");

                // Build real entry objects
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

                // Write bracket data
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
                    return Fail("Server error writing bracket data.");
                }

                // Write player → bracket mapping for each real player
                foreach (var entry in realEntries)
                {
                    try
                    {
                        var mapping = new PlayerMapping
                        {
                            TournamentId = tournamentId,
                            EntryId = entry.EntryId,
                            ScoreBand = band
                        };
                        var mapJson = JsonSerializer.Serialize(mapping);
                        await api.CloudSaveData.SetCustomItemAsync(
                            ctx, ctx.ServiceToken, ctx.ProjectId, playerMapEntity,
                            new SetItemBody(entry.PlayerId, mapJson));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Failed to map player {PlayerId} → {TournamentId}: {Error}",
                            entry.PlayerId, tournamentId, ex.Message);
                    }
                }

                totalBrackets++;
                _logger.LogInformation(
                    "Bracket {TournamentId}: {RealCount} real + {BotCount} bots",
                    tournamentId, realEntries.Count, BracketHelper.BracketSize - realEntries.Count);
            }
        }

        // ── Mark week as locked ──

        await WriteLock(api, ctx, lockEntity);

        _logger.LogInformation(
            "Week {WeekId} locked: {BracketCount} bracket(s), {PlayerCount} player(s).",
            weekId, totalBrackets, registrations.Count);

        return new LockResult
        {
            Success = true,
            BracketCount = totalBrackets,
            PlayerCount = registrations.Count
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Internal helpers (also used by GetTournamentBracket lazy lock)
    // ═══════════════════════════════════════════════════════════

    internal static async Task<List<RegistrationData>?> ReadRegistrations(
        IGameApiClient api, IExecutionContext ctx, string weekId)
    {
        var regEntity = $"tournament_reg_{weekId}";
        var registrations = new List<RegistrationData>();

        try
        {
            var regResult = await api.CloudSaveData.GetCustomItemsAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, regEntity);

            if (regResult?.Data?.Results != null)
            {
                foreach (var item in regResult.Data.Results)
                {
                    try
                    {
                        var raw = item.Value?.ToString();
                        if (!string.IsNullOrEmpty(raw))
                        {
                            var reg = JsonSerializer.Deserialize<RegistrationData>(raw);
                            if (reg != null) registrations.Add(reg);
                        }
                    }
                    catch { /* skip malformed entries */ }
                }
            }
        }
        catch (ApiException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return registrations; // No registrations entity — empty list
        }
        catch
        {
            return null; // signal error
        }

        return registrations;
    }

    internal static async Task WriteLock(IGameApiClient api, IExecutionContext ctx, string lockEntity)
    {
        try
        {
            await api.CloudSaveData.SetCustomItemAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, lockEntity,
                new SetItemBody("status", "done"));
        }
        catch { /* best-effort */ }
    }

    private static LockResult Fail(string error)
        => new() { Success = false, Error = error };
}
