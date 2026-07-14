// ArenaModule — GetTournamentBracket handler
// Port of GetTournamentBracket.js to C# Cloud Code Module.
//
// Returns the calling player's bracket assignment for a given week.
// If brackets haven't been built yet but it's past the lock time,
// triggers bracket building lazily via BracketBuilder (same logic as
// LockAndAssignBrackets). The lock is written optimistically at the START
// of BracketBuilder.BuildAndWriteAsync to minimise the race window when
// multiple players trigger the lazy path simultaneously.

using System.Text.Json;
using ArenaModule.Helpers;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;

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
            // SECURITY: only the CURRENT week may be lazily locked. Without this a
            // client could send a FUTURE weekId on a Wednesday+, build that week with
            // zero registrations, and the idempotent "done" lock would then block the
            // real bracket build when that week actually arrives.
            if (weekId != ScheduleHelper.GetCurrentWeekId())
                return NotAssigned("Brackets are not available for that week.");

            if (!ScheduleHelper.IsPastLockTime())
                return NotAssigned("Brackets haven't been assigned yet. Check back Wednesday.");

            _logger.LogInformation("Lazy-locking week {WeekId}...", weekId);

            var registrations = await LockAndAssignBrackets.ReadRegistrations(api, ctx, weekId);
            if (registrations == null)
                return NotAssigned("Error reading registrations. Try again.");

            // BracketBuilder writes the lock first (optimistic) before building,
            // so concurrent callers that race here will see the lock and skip.
            var buildResult = await BracketBuilder.BuildAndWriteAsync(api, ctx, weekId, registrations, _logger);
            if (!buildResult.Success)
                return NotAssigned("Error building brackets. Try again.");

            _logger.LogInformation("Lazy-lock complete: {Count} bracket(s).", buildResult.BracketCount);
            bracketsBuilt = true;
        }

        // ── Look up player's bracket assignment ──

        var playerMapEntity = $"tournament_player_map_{weekId}";
        PlayerMapping? mapping = null;

        try
        {
            // NOTE: SDK limitation — GetCustomItemsAsync fetches ALL player mappings for the
            // week. There is no single-key overload for custom entity data in this SDK version.
            var mapResult = await api.CloudSaveData.GetCustomItemsAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, playerMapEntity);

            if (mapResult?.Data?.Results != null)
            {
                if (mapResult.Data.Results.Count >= 1000)
                    _logger.LogWarning("Player map for {WeekId} may be paginated — some entries could be missing.", weekId);

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
            // NOTE: SDK limitation — fetches all brackets for the week to find this player's.
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

    private static GetBracketResult NotAssigned(string reason)
        => new() { Assigned = false, Reason = reason };
}
