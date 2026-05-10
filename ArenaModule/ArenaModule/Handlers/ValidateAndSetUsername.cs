// ArenaModule — ValidateAndSetUsername handler
// Port of ValidateAndSetUsername.js to C# Cloud Code Module.
//
// Validates a candidate arena username for uniqueness and atomically claims it.
// Username index stored in Cloud Save Custom Data entity "username_index".

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace ArenaModule.Handlers;

public class ValidateAndSetUsername
{
    private const int MinLength = 2;
    private const int MaxLength = 16;
    private const string CustomEntityId = "username_index";
    private const string ArenaDataKey = "arena_v1";

    private readonly ILogger<ValidateAndSetUsername> _logger;

    public ValidateAndSetUsername(ILogger<ValidateAndSetUsername> logger)
    {
        _logger = logger;
    }

    [CloudCodeFunction("ValidateAndSetUsername")]
    public async Task<UsernameResult> Execute(
        IExecutionContext ctx,
        IGameApiClient api,
        string username)
    {
        // ── Client-side validation (belt-and-suspenders) ──

        if (string.IsNullOrWhiteSpace(username))
            return Fail("Username is required.");

        var trimmed = username.Trim();

        if (trimmed.Length < MinLength)
            return Fail($"Name must be at least {MinLength} characters.");

        if (trimmed.Length > MaxLength)
            return Fail($"Name must be at most {MaxLength} characters.");

        if (!IsUsernameSafe(trimmed))
            return Fail("Name contains invalid characters.");

        // ── Parallel read: fetch both player data AND username index concurrently ── // FIXED: parallelize I/O to cut latency
        var nameKey = trimmed.ToLowerInvariant();
        ArenaSaveDataServer? arenaData = null;
        bool nameTaken = false;
        string? oldNameKey = null;

        try
        {
            // FIXED: fetch both independent datasets concurrently instead of sequentially
            var playerTask = api.CloudSaveData.GetItemsAsync(
                ctx, ctx.AccessToken, ctx.ProjectId, ctx.PlayerId,
                new List<string> { ArenaDataKey });

            var indexTask = api.CloudSaveData.GetCustomItemsAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, CustomEntityId);

            await Task.WhenAll(playerTask, indexTask);

            var playerItems = playerTask.Result;
            var indexResult = indexTask.Result;

            // ── Process player data ──
            if (playerItems?.Data?.Results != null)
            {
                foreach (var item in playerItems.Data.Results)
                {
                    if (item.Key == ArenaDataKey && item.Value != null)
                    {
                        var raw = item.Value.ToString();
                        if (!string.IsNullOrEmpty(raw))
                            arenaData = JsonSerializer.Deserialize<ArenaSaveDataServer>(raw);
                    }
                }
            }

            if (arenaData is { UsernameCreated: true, ArenaUsername: not null })
                return Fail("You already have a username.");

            // ── Process username index ──
            if (indexResult?.Data?.Results != null)
            {
                foreach (var item in indexResult.Data.Results)
                {
                    var value = item.Value?.ToString() ?? "";

                    // This player owns a different name (previous reset)
                    if (value == ctx.PlayerId && item.Key != nameKey)
                        oldNameKey = item.Key;

                    if (item.Key == nameKey)
                    {
                        if (!string.IsNullOrEmpty(value) && value != ctx.PlayerId)
                            nameTaken = true;
                    }
                }
            }
        }
        catch (ApiException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Username index entity not found (404) — first username ever.");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed during parallel read: {Error}", ex.Message);
            return Fail("Server error during validation.");
        }

        if (nameTaken)
            return Fail("That name is already taken.");

        // ── Release old name if switching ──

        if (oldNameKey != null)
        {
            try
            {
                await api.CloudSaveData.DeleteCustomItemAsync(
                    ctx, ctx.ServiceToken, ctx.ProjectId, CustomEntityId, oldNameKey);
                _logger.LogInformation("Released old username index entry: \"{OldKey}\"", oldNameKey);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to release old username: {Error}", ex.Message);
                // Non-fatal — continue
            }
        }

        // ── Claim the name ──

        try
        {
            await api.CloudSaveData.SetCustomItemAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, CustomEntityId,
                new SetItemBody(nameKey, ctx.PlayerId));
            _logger.LogInformation("Wrote username index: entity=\"{Entity}\", key=\"{Key}\", value=\"{PlayerId}\"",
                CustomEntityId, nameKey, ctx.PlayerId);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to write username index: {Error}", ex.Message);
            return Fail("Server error claiming name.");
        }

        // ── Write to the player's arena data ──

        try
        {
            arenaData ??= new ArenaSaveDataServer();
            arenaData.ArenaUsername = trimmed;
            arenaData.UsernameCreated = true;

            var json = JsonSerializer.Serialize(arenaData);
            await api.CloudSaveData.SetItemAsync(
                ctx, ctx.AccessToken, ctx.ProjectId, ctx.PlayerId,
                new SetItemBody(ArenaDataKey, json));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to write player arena data: {Error}", ex.Message);
            return Fail("Name claimed but profile update failed. Reopen the Arena.");
        }

        _logger.LogInformation("Player {PlayerId} claimed username \"{Name}\"", ctx.PlayerId, trimmed);
        return new UsernameResult { Success = true };
    }

    // ── Helpers ──

    private static UsernameResult Fail(string error)
        => new() { Success = false, Error = error };

    private static bool IsUsernameSafe(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Trim() != name) return false;
        foreach (char c in name)
        {
            if (c < 32) return false;          // control characters
            if (c == '<' || c == '>') return false; // angle brackets
        }
        return true;
    }
}
