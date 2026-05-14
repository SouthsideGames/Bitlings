// ArenaModule — RegisterForTournament handler
// Port of RegisterForTournament.js to C# Cloud Code Module.
//
// Registers a player for the current week's arena tournament.
// Stores the team snapshot + metadata in Cloud Save Custom Data
// entity "tournament_reg_{weekId}" keyed by playerId.

using System.Text.Json;
using ArenaModule.Helpers;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace ArenaModule.Handlers;

public class RegisterForTournament
{
    private readonly ILogger<RegisterForTournament> _logger;

    public RegisterForTournament(ILogger<RegisterForTournament> logger)
    {
        _logger = logger;
    }

    [CloudCodeFunction("RegisterForTournament")]
    public async Task<RegisterResult> Execute(
        IExecutionContext ctx,
        IGameApiClient api,
        string teamSnapshotJson,
        double arenaScore,
        int scoreBand,       // accepted for API compat but overridden server-side below
        string displayName,
        string weekId,
        string[]? ownedInstanceIds = null)
    {
        // ── Validate parameters ──

        if (string.IsNullOrWhiteSpace(teamSnapshotJson))
            return Fail("Team snapshot is required.");

        if (arenaScore < 0)
            return Fail("Invalid arena score.");

        if (string.IsNullOrWhiteSpace(displayName))
            return Fail("Display name is required.");

        if (string.IsNullOrWhiteSpace(weekId) || !weekId.StartsWith('W'))
            return Fail("Invalid week ID.");

        // ── Validate registration window ──

        var serverWeekId = ScheduleHelper.GetCurrentWeekId();
        if (weekId != serverWeekId)
            return Fail("Registration week mismatch. Please refresh.");

        if (!ScheduleHelper.IsRegistrationOpen())
            return Fail("Registration is currently closed.");

        // ── Validate team snapshot parses ──

        TeamSnapshotServer? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<TeamSnapshotServer>(teamSnapshotJson);
        }
        catch
        {
            return Fail("Invalid team snapshot format.");
        }

        if (snapshot?.SlotSnapshots == null || snapshot.SlotSnapshots.Count != 3)
            return Fail("Team must have exactly 3 Bitlings.");

        // ── Anti-cheat: validate snapshot against server catalogs ──

        var validator = new SnapshotValidator(_logger);
        var validationError = await validator.ValidateAsync(api, ctx, snapshot, arenaScore, ctx.PlayerId);
        if (validationError != null)
        {
            _logger.LogWarning("Snapshot validation failed for {PlayerId}: {Error}", ctx.PlayerId, validationError);
            return Fail(validationError);
        }

        // ── Anti-cheat: ownership fingerprint ──

        if (ownedInstanceIds != null && ownedInstanceIds.Length > 0)
        {
            var ownedSet = new HashSet<string>(ownedInstanceIds);
            foreach (var slot in snapshot.SlotSnapshots)
            {
                if (!ownedSet.Contains(slot.InstanceId))
                {
                    _logger.LogWarning(
                        "Ownership check failed for {PlayerId}: instanceId {Uid} not in owned list",
                        ctx.PlayerId, slot.InstanceId);
                    return Fail("Team contains a Bitling you don't own.");
                }
            }
        }

        // ── Compute score band server-side (ignores client-supplied scoreBand) ──
        // This prevents players from registering with a valid high score but a
        // fake low band to get easier matchups.

        int serverScoreBand = BracketHelper.ScoreToBand((int)arenaScore);

        // ── Store registration ──

        var regEntity = $"tournament_reg_{weekId}";

        var registrationData = new RegistrationData
        {
            PlayerId = ctx.PlayerId,
            DisplayName = displayName.Trim(),
            TeamSnapshotJson = teamSnapshotJson,
            ArenaScore = arenaScore,
            ScoreBand = serverScoreBand,
            RegisteredUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        try
        {
            var json = JsonSerializer.Serialize(registrationData);
            await api.CloudSaveData.SetCustomItemAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, regEntity,
                new SetItemBody(ctx.PlayerId, json));

            _logger.LogInformation(
                "Player {PlayerId} registered for {WeekId} (band={Band}, score={Score})",
                ctx.PlayerId, weekId, serverScoreBand, arenaScore);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to store registration: {Error}", ex.Message);
            return Fail("Server error saving registration.");
        }

        return new RegisterResult { Success = true, WeekId = weekId };
    }

    private static RegisterResult Fail(string error)
        => new() { Success = false, Error = error };
}
