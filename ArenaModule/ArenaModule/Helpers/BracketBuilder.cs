// ArenaModule — Shared bracket-building logic
// Used by both LockAndAssignBrackets and GetTournamentBracket (lazy path).
//
// Writes optimistically: the lock is written FIRST so that concurrent callers
// (e.g. multiple players hitting GetTournamentBracket simultaneously on Wednesday)
// skip duplicate builds. Because the PRNG seed is deterministic (weekId + band),
// any concurrent builds that do race produce identical output — Cloud Save upserts
// are idempotent, so the data is always consistent.

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

        // ── Group by score band ──

        var pools = new List<List<RegistrationData>> { new(), new(), new(), new() };
        foreach (var reg in registrations)
            pools[System.Math.Clamp(reg.ScoreBand, 0, 3)].Add(reg);

        // ── Merge small bands ──

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

        // ── Build and write brackets ──

        var bracketsEntity = $"tournament_brackets_{weekId}";
        var playerMapEntity = $"tournament_player_map_{weekId}";
        long weekStartUtc = ScheduleHelper.WeekIdToEpoch(weekId);
        long weekEndUtc = weekStartUtc + 7 * 24 * 60 * 60 - 1;
        int totalBrackets = 0;

        for (int band = 0; band <= 3; band++)
        {
            var pool = pools[band];
            if (pool.Count == 0) continue;

            // Deterministic shuffle — same weekId+band always produces same ordering.
            new Prng(ScheduleHelper.HashCode($"{weekId}_{band}")).Shuffle(pool);

            // Even distribution: 33 players → [17, 16], not [32, 1].
            var chunks = BracketHelper.SplitEvenly(pool, BracketHelper.BracketSize);

            foreach (var chunk in chunks)
            {
                string tournamentId = $"T{weekId}_{BracketHelper.BandNames[band]}_{totalBrackets}";
                int bracketSeed = ScheduleHelper.HashCode($"{tournamentId}_seed");

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
                    await api.CloudSaveData.SetCustomItemAsync(
                        ctx, ctx.ServiceToken, ctx.ProjectId, bracketsEntity,
                        new SetItemBody(tournamentId, JsonSerializer.Serialize(bracketData)));
                }
                catch (Exception ex)
                {
                    logger.LogError("Failed to write bracket {TournamentId}: {Error}", tournamentId, ex.Message);
                    return new BuildResult(false, 0, 0, "Server error writing bracket data.");
                }

                foreach (var entry in realEntries)
                {
                    try
                    {
                        await api.CloudSaveData.SetCustomItemAsync(
                            ctx, ctx.ServiceToken, ctx.ProjectId, playerMapEntity,
                            new SetItemBody(entry.PlayerId, JsonSerializer.Serialize(new PlayerMapping
                            {
                                TournamentId = tournamentId,
                                EntryId = entry.EntryId,
                                ScoreBand = band
                            })));
                    }
                    catch (Exception ex)
                    {
                        logger.LogError("Failed to map player {PlayerId} → {TournamentId}: {Error}",
                            entry.PlayerId, tournamentId, ex.Message);
                    }
                }

                logger.LogInformation(
                    "Bracket {TournamentId}: {RealCount} real + {BotCount} bots",
                    tournamentId, realEntries.Count, BracketHelper.BracketSize - realEntries.Count);

                totalBrackets++;
            }
        }

        return new BuildResult(true, totalBrackets, registrations.Count);
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
