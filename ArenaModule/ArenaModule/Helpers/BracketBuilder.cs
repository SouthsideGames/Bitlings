// ArenaModule — Shared bracket-building logic
// Used by both LockAndAssignBrackets and GetTournamentBracket (lazy path).
//
// Writes optimistically: the lock is written FIRST so that concurrent callers
// (e.g. multiple players hitting GetTournamentBracket simultaneously on Wednesday)
// skip duplicate builds. Because the PRNG seed is deterministic (weekId + band),
// any concurrent builds that do race produce identical output — Cloud Save upserts
// are idempotent, so the data is always consistent.
//
// All Cloud Save writes (bracket data + player maps) are fired in parallel via
// Task.WhenAll — no sequential round-trips for independent writes.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace ArenaModule.Helpers;

public static class BracketBuilder
{
    public record BuildResult(bool Success, int BracketCount, int PlayerCount, string? Error = null);

    public static async Task<BuildResult> BuildAndWriteAsync(
        IGameApiClient api,
        IExecutionContext ctx,
        string weekId,
        List<RegistrationData> registrations,
        ILogger logger)
    {
        var lockEntity = $"tournament_lock_{weekId}";

        // Write the lock FIRST (optimistic) so concurrent callers see it and skip.
        // If building fails partway through, LockAndAssignBrackets can be re-run
        // manually to repair — it checks the lock but the admin path can force-rebuild.
        await WriteLock(api, ctx, lockEntity);

        if (registrations.Count == 0)
            return new BuildResult(true, 0, 0);

        // ── Phase 1: pure in-memory computation (no I/O) ──

        var pools = new List<List<RegistrationData>> { new(), new(), new(), new() };
        foreach (var reg in registrations)
            pools[System.Math.Clamp(reg.ScoreBand, 0, 3)].Add(reg);

        for (int band = 0; band <= 3; band++)
        {
            if (pools[band].Count > 0 && pools[band].Count < BracketHelper.MinRealForMerge)
            {
                int target = BracketHelper.FindMergeTarget(pools, band);
                if (target != -1 && target != band)
                {
                    logger.LogInformation(
                        "Merging band {Src} ({SrcCount}) into band {Dst}",
                        BracketHelper.BandNames[band], pools[band].Count, BracketHelper.BandNames[target]);
                    pools[target].AddRange(pools[band]);
                    pools[band].Clear();
                }
            }
        }

        var bracketsEntity = $"tournament_brackets_{weekId}";
        var playerMapEntity = $"tournament_player_map_{weekId}";
        long weekStartUtc = ScheduleHelper.WeekIdToEpoch(weekId);
        long weekEndUtc = weekStartUtc + 7 * 24 * 60 * 60 - 1;

        // Build all bracket data and player mappings in memory before touching the network.
        var builtBrackets = new List<BracketData>();
        var builtMappings = new List<(string playerId, PlayerMapping mapping)>();
        int totalBrackets = 0;

        for (int band = 0; band <= 3; band++)
        {
            var pool = pools[band];
            if (pool.Count == 0) continue;

            new Prng(ScheduleHelper.HashCode($"{weekId}_{band}")).Shuffle(pool);

            foreach (var chunk in BracketHelper.SplitEvenly(pool, BracketHelper.BracketSize))
            {
                string tournamentId = $"T{weekId}_{BracketHelper.BandNames[band]}_{totalBrackets}";

                var realEntries = new List<BracketEntry>(chunk.Count);
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

                builtBrackets.Add(new BracketData
                {
                    TournamentId = tournamentId,
                    WeekStartUtc = weekStartUtc,
                    WeekEndUtc = weekEndUtc,
                    ScoreBand = band,
                    BracketSeed = ScheduleHelper.HashCode($"{tournamentId}_seed"),
                    RealEntries = realEntries,
                    RealPlayerCount = realEntries.Count,
                    BotsNeeded = BracketHelper.BracketSize - realEntries.Count
                });

                foreach (var entry in realEntries)
                    builtMappings.Add((entry.PlayerId, new PlayerMapping
                    {
                        TournamentId = tournamentId,
                        EntryId = entry.EntryId,
                        ScoreBand = band
                    }));

                logger.LogInformation(
                    "Bracket {TournamentId}: {RealCount} real + {BotCount} bots",
                    tournamentId, realEntries.Count, BracketHelper.BracketSize - realEntries.Count);

                totalBrackets++;
            }
        }

        // ── Phase 2: fire all writes in parallel ──

        // Bracket writes: any failure is fatal — we need bracket data to exist before
        // players can read it, so collect and surface the first error.
        var bracketTasks = builtBrackets.Select(b => WriteBracketAsync(api, ctx, bracketsEntity, b));
        var bracketErrors = await Task.WhenAll(bracketTasks);
        var firstBracketError = bracketErrors.FirstOrDefault(e => e != null);
        if (firstBracketError != null)
        {
            logger.LogError("Bracket write failed: {Error}", firstBracketError);
            return new BuildResult(false, 0, 0, "Server error writing bracket data.");
        }

        // Player map writes: best-effort — a missed mapping means that player won't see
        // their bracket, but it doesn't corrupt other players' data.
        var mappingTasks = builtMappings.Select(m =>
            WriteMappingAsync(api, ctx, playerMapEntity, m.playerId, m.mapping, logger));
        await Task.WhenAll(mappingTasks);

        return new BuildResult(true, totalBrackets, registrations.Count);
    }

    private static async Task<string?> WriteBracketAsync(
        IGameApiClient api, IExecutionContext ctx, string entity, BracketData bracket)
    {
        try
        {
            await api.CloudSaveData.SetCustomItemAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, entity,
                new SetItemBody(bracket.TournamentId, JsonSerializer.Serialize(bracket)));
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static async Task WriteMappingAsync(
        IGameApiClient api, IExecutionContext ctx, string entity,
        string playerId, PlayerMapping mapping, ILogger logger)
    {
        try
        {
            await api.CloudSaveData.SetCustomItemAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, entity,
                new SetItemBody(playerId, JsonSerializer.Serialize(mapping)));
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to map player {PlayerId} → {TournamentId}: {Error}",
                playerId, mapping.TournamentId, ex.Message);
        }
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
}
