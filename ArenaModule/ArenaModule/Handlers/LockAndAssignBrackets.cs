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

        // SECURITY: only the current week may be locked. Locking an arbitrary future
        // week with zero registrations would permanently no-op the real lock for that
        // week (the "done" status is idempotent), breaking the arena for everyone.
        if (weekId != ScheduleHelper.GetCurrentWeekId())
            return Fail("Only the current week can be locked.");

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

        // ── Build brackets (writes lock first, then brackets) ──

        var result = await BracketBuilder.BuildAndWriteAsync(api, ctx, weekId, registrations, _logger);

        if (!result.Success)
            return Fail(result.Error ?? "Server error building brackets.");

        _logger.LogInformation(
            "Week {WeekId} locked: {BracketCount} bracket(s), {PlayerCount} player(s).",
            weekId, result.BracketCount, result.PlayerCount);

        return new LockResult
        {
            Success = true,
            BracketCount = result.BracketCount,
            PlayerCount = result.PlayerCount
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Internal helpers (also used by GetTournamentBracket and BracketBuilder)
    // ═══════════════════════════════════════════════════════════

    internal static async Task<List<RegistrationData>?> ReadRegistrations(
        IGameApiClient api, IExecutionContext ctx, string weekId)
    {
        var regEntity = $"tournament_reg_{weekId}";
        var registrations = new List<RegistrationData>();

        try
        {
            // NOTE: SDK limitation — GetCustomItemsAsync fetches ALL registrations in one call.
            // Cloud Save may paginate large result sets; if Results.Count == 1000 some entries
            // may have been truncated. Monitor registration counts if the game grows large.
            var regResult = await api.CloudSaveData.GetCustomItemsAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, regEntity);

            if (regResult?.Data?.Results != null)
            {
                if (regResult.Data.Results.Count >= 1000)
                {
                    // Log so we know pagination may be silently truncating results.
                    // TODO: implement pagination when the Cloud Save SDK supports it.
                }

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

    internal static Task WriteLock(IGameApiClient api, IExecutionContext ctx, string lockEntity)
        => BracketBuilder.WriteLock(api, ctx, lockEntity);

    private static LockResult Fail(string error)
        => new() { Success = false, Error = error };
}
