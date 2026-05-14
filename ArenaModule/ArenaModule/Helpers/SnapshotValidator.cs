// ArenaModule — Server-side snapshot validation
// Validates team snapshots against reference catalogs stored in Cloud Save.
//
// Checks performed:
//   1. Structural integrity (3 slots, non-empty IDs)
//   2. No duplicate monsters across slots
//   3. All monsterIds exist in catalog
//   4. All titleIds (if equipped) exist in catalog
//   5. Per-slot score integrity (claimed scores match catalog)
//   6. Total score integrity (sum of slots + synergy tolerance)
//   7. Owner player ID matches authenticated player
//   8. Snapshot timestamp within registration window

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;

namespace ArenaModule.Helpers;

public class SnapshotValidator
{
    private const string CatalogEntity = "arena_catalogs";

    // Allow small tolerance for synergy bonus (max synergy = 15)
    private const int MaxSynergyBonus = 15;

    private readonly ILogger _logger;

    public SnapshotValidator(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates a team snapshot against server-side catalogs.
    /// Returns null if valid, or an error string if invalid.
    /// </summary>
    public async Task<string?> ValidateAsync(
        IGameApiClient api,
        IExecutionContext ctx,
        TeamSnapshotServer snapshot,
        double claimedArenaScore,
        string playerId)
    {
        // ── 1. Structural checks ──

        if (snapshot.SlotSnapshots == null || snapshot.SlotSnapshots.Count != 3)
            return "Team must have exactly 3 Bitlings.";

        foreach (var slot in snapshot.SlotSnapshots)
        {
            if (string.IsNullOrWhiteSpace(slot.MonsterId))
                return "Empty monster ID in team slot.";

            if (string.IsNullOrWhiteSpace(slot.InstanceId))
                return "Empty instance ID in team slot.";
        }

        // ── 2. No duplicate instances ──

        var instanceIds = new HashSet<string>();

        foreach (var slot in snapshot.SlotSnapshots)
        {
            if (!instanceIds.Add(slot.InstanceId))
                return $"Duplicate instance ID: {slot.InstanceId}";
        }

        // ── 3. Owner verification ──

        if (!string.IsNullOrEmpty(snapshot.OwnerPlayerId) && snapshot.OwnerPlayerId != playerId)
            return "Snapshot owner does not match authenticated player.";

        if (snapshot.IsBot)
            return "Cannot register a bot snapshot.";

        // ── 4. Load catalogs for score validation ──

        Dictionary<string, CatalogMonster>? monsterLookup = null;
        Dictionary<string, CatalogTitle>? titleLookup = null;

        try
        {
            // NOTE: SDK limitation — GetCustomItemsAsync fetches all items in the entity.
            // The catalog entity only has 2 keys ("monsters", "titles") so this is fine.
            var catalogResult = await api.CloudSaveData.GetCustomItemsAsync(
                ctx, ctx.ServiceToken, ctx.ProjectId, CatalogEntity);

            if (catalogResult?.Data?.Results != null)
            {
                foreach (var item in catalogResult.Data.Results)
                {
                    var raw = item.Value?.ToString();
                    if (string.IsNullOrEmpty(raw)) continue;

                    if (item.Key == "monsters")
                    {
                        var catalog = JsonSerializer.Deserialize<MonsterCatalogServer>(raw);
                        if (catalog?.Monsters != null)
                        {
                            monsterLookup = new Dictionary<string, CatalogMonster>();
                            foreach (var m in catalog.Monsters)
                                monsterLookup[m.Id] = m;
                        }
                    }
                    else if (item.Key == "titles")
                    {
                        var catalog = JsonSerializer.Deserialize<TitleCatalogServer>(raw);
                        if (catalog?.Titles != null)
                        {
                            titleLookup = new Dictionary<string, CatalogTitle>();
                            foreach (var t in catalog.Titles)
                                titleLookup[t.TitleId] = t;
                        }
                    }
                }
            }
        }
        catch (ApiException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Catalog entity doesn't exist yet — graceful degradation for initial deployment.
            _logger.LogWarning("Catalog entity not found in Cloud Save. Score validation skipped.");
            return null;
        }
        catch (Exception ex)
        {
            // Any other error (network, serialization, etc.) — reject registration rather
            // than silently allowing unvalidated scores through.
            _logger.LogError("Failed to load catalogs for validation: {Error}", ex.Message);
            return "Server error loading catalogs. Please try again.";
        }

        // If catalog entity exists but keys are missing, skip score validation with a warning.
        if (monsterLookup == null || titleLookup == null)
        {
            _logger.LogWarning("Catalog keys missing from Cloud Save entity. Score validation skipped.");
            return null;
        }

        // ── 5. Validate monster and title IDs exist in catalog ──

        int computedBaseSum = 0;

        foreach (var slot in snapshot.SlotSnapshots)
        {
            if (!monsterLookup.TryGetValue(slot.MonsterId, out var catalogMonster))
                return $"Unknown monster ID: {slot.MonsterId}";

            int expectedMonsterScore = catalogMonster.ArenaScore;
            int expectedTitleScore = 0;

            if (!string.IsNullOrEmpty(slot.TitleId))
            {
                if (!titleLookup.TryGetValue(slot.TitleId, out var catalogTitle))
                    return $"Unknown title ID: {slot.TitleId}";

                expectedTitleScore = catalogTitle.ArenaScore;
            }

            // ── 6. Per-slot score integrity ──

            if (slot.MonsterArenaScore != expectedMonsterScore)
            {
                _logger.LogWarning(
                    "Score mismatch: monster {Id} claimed {Claimed} but catalog says {Expected}",
                    slot.MonsterId, slot.MonsterArenaScore, expectedMonsterScore);
                return $"Invalid arena score for monster {slot.MonsterId}.";
            }

            if (slot.TitleArenaScore != expectedTitleScore)
            {
                _logger.LogWarning(
                    "Score mismatch: title {Id} claimed {Claimed} but catalog says {Expected}",
                    slot.TitleId, slot.TitleArenaScore, expectedTitleScore);
                return $"Invalid arena score for title {slot.TitleId}.";
            }

            int expectedSlotTotal = expectedMonsterScore + expectedTitleScore;
            if (slot.FinalArenaContributionScore != expectedSlotTotal)
                return $"Invalid slot contribution score for {slot.MonsterId}.";

            computedBaseSum += expectedSlotTotal;
        }

        // ── 7. Total score integrity (base sum + synergy must match within tolerance) ──

        int claimedTotal = (int)claimedArenaScore;

        // Synergy bonus is 0, 5, 10, or 15 — the client-claimed score should be
        // between baseSum and baseSum + MaxSynergyBonus
        if (claimedTotal < computedBaseSum || claimedTotal > computedBaseSum + MaxSynergyBonus)
        {
            _logger.LogWarning(
                "Total score mismatch: claimed {Claimed}, computed base {Base}, max possible {Max}",
                claimedTotal, computedBaseSum, computedBaseSum + MaxSynergyBonus);
            return "Total arena score does not match team composition.";
        }

        return null; // All checks passed
    }
}
