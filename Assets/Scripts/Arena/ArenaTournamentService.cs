// Assets/Scripts/Arena/ArenaTournamentService.cs
// BRN Arena v1 — Runtime orchestration service that ties together tournament
// creation, entry, round resolution, rewards, and record persistence.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Static runtime service that manages the active tournament lifecycle.
/// Bridges the UI (ArenaMainPanelUI) with the underlying systems
/// (ArenaTournamentBuilder, ArenaMatchResolver, ArenaRewardService, etc.).
/// </summary>
public static class ArenaTournamentService
{
    const string TAG = "[ArenaTournamentService]";
    const string RecordFileName = "arena_active_tournament.json";

    // ═════════════════════════════════════════════════════════════
    //  In-memory record
    // ═════════════════════════════════════════════════════════════

    private static ArenaTournamentRecord _activeRecord;

    /// <summary>
    /// Returns the in-memory tournament record, or attempts to load from disk.
    /// May return null if no active tournament exists.
    /// </summary>
    public static ArenaTournamentRecord GetActiveRecord()
    {
        if (_activeRecord != null) return _activeRecord;
        _activeRecord = LoadRecordFromDisk();
        return _activeRecord;
    }

    /// <summary>True if a tournament record is currently loaded.</summary>
    public static bool HasActiveRecord => GetActiveRecord() != null;

    // ═════════════════════════════════════════════════════════════
    //  Enter Tournament
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Full entry flow: validate → spend ticket → freeze team → build bracket → persist.
    /// Returns true on success.
    /// </summary>
    public static bool TryEnterTournament(out string errorMessage)
    {
        errorMessage = null;

        // ── Prerequisites ──
        if (!ArenaSaveHelper.IsArenaUnlocked())
        {
            errorMessage = "Arena is not unlocked.";
            return false;
        }

        if (!ArenaTeamValidator.IsBattleTeamComplete())
        {
            errorMessage = "Complete your Battle Team first.";
            return false;
        }

        if (ArenaTicketManager.GetTicketCount() <= 0)
        {
            errorMessage = "You need an Arena Ticket to enter.";
            return false;
        }

        var arena = SaveManager.GetArenaSaveData();
        var status = arena?.currentTournamentCache?.playerStatus ?? ArenaPlayerTournamentStatus.NotEntered;
        if (status != ArenaPlayerTournamentStatus.NotEntered)
        {
            errorMessage = "Already entered a tournament this week.";
            return false;
        }

        // ── Spend ticket ──
        if (!ArenaTicketManager.TrySpendTicket())
        {
            errorMessage = "Unable to spend ticket.";
            return false;
        }

        // ── Build player entry ──
        string playerId = arena.arenaPlayerId ?? Guid.NewGuid().ToString();
        string displayName = !string.IsNullOrEmpty(arena.arenaUsername) ? arena.arenaUsername : "Player";

        var playerSnapshot = ArenaTournamentBuilder.CreateTournamentEntrySnapshot(playerId, displayName);
        int playerScore = ArenaScoreCalculator.CalculateArenaTeamScore(playerSnapshot);
        var band = ArenaScoreCalculator.GetBattleTeamScoreBand(playerScore);

        var playerEntry = new ArenaTournamentEntry
        {
            entryId = Guid.NewGuid().ToString(),
            tournamentId = "",
            playerId = playerId,
            displayNameSnapshot = displayName,
            isBot = false,
            arenaScore = playerScore,
            teamSnapshot = playerSnapshot,
            eliminatedRoundIndex = -1,
            finalPlacement = 0
        };

        // ── Generate bots + build bracket ──
        var rng = new System.Random((int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() ^ playerId.GetHashCode()));
        var botTemplates = ArenaBotTemplateLibrary.GetTemplatesForBand(band);
        int botsNeeded = ArenaConstants.BracketSize - 1;
        var bots = ArenaBotGenerator.GenerateBotEntries(botsNeeded, band, "pending", botTemplates, rng);

        var allEntries = new List<ArenaTournamentEntry>(ArenaConstants.BracketSize) { playerEntry };
        allEntries.AddRange(bots);

        long weekStart = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tournaments = ArenaTournamentBuilder.BuildWeeklyArenaTournaments(allEntries, weekStart, botTemplates, rng);

        if (tournaments == null || tournaments.Count == 0)
        {
            errorMessage = "Failed to build tournament bracket.";
            Debug.LogError($"{TAG} BuildWeeklyArenaTournaments returned no results.");
            return false;
        }

        var record = tournaments[0];
        record.state = ArenaTournamentState.Active;

        // Reassign playerEntry reference (builder may have reorganised entries)
        var resolvedPlayerEntry = record.entries.Find(e => !e.isBot);
        string entryId = resolvedPlayerEntry?.entryId ?? playerEntry.entryId;

        // ── Update save data ──
        long weekEnd = weekStart + (7 * 24 * 60 * 60);
        arena.currentTournamentCache = new ArenaCurrentTournamentCache
        {
            tournamentId = record.tournamentId,
            weekStartUtc = record.weekStartUtc,
            weekEndUtc = weekEnd,
            playerEntryId = entryId,
            playerStatus = ArenaPlayerTournamentStatus.Entered,
            currentRoundIndex = 0,
            finalPlacement = 0,
            resultsLastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Lock team
        arena.battleTeamData ??= new ArenaBattleTeamData();
        arena.battleTeamData.isLocked = true;
        arena.battleTeamData.lockedTournamentId = record.tournamentId;

        // Lifetime stats
        arena.lifetimeStats ??= new ArenaLifetimeStats();
        arena.lifetimeStats.tournamentsEntered++;

        _activeRecord = record;
        SaveRecordToDisk(record);
        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();

        Debug.Log($"{TAG} Entered tournament '{record.tournamentId}' ({band} band, {record.entries.Count} entries). PlayerEntry={entryId}");
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  Resolve Rounds
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves the next unresolved round. Returns the round index that was resolved, or -1 if none remain.
    /// </summary>
    public static int ResolveNextRound()
    {
        var record = GetActiveRecord();
        if (record == null)
        {
            Debug.LogWarning($"{TAG} No active tournament to resolve.");
            return -1;
        }

        var arena = SaveManager.GetArenaSaveData();
        var cache = arena?.currentTournamentCache;
        int roundIndex = cache?.currentRoundIndex ?? 0;

        if (roundIndex >= ArenaConstants.TotalRounds)
        {
            Debug.Log($"{TAG} All rounds already resolved.");
            return -1;
        }

        int resolved = ArenaMatchResolver.ResolveRound(record, roundIndex);
        ArenaMatchResolver.GenerateNextRoundMatches(record, roundIndex);

        // Check if player was eliminated this round
        string playerEntryId = cache?.playerEntryId;
        if (!string.IsNullOrEmpty(playerEntryId))
        {
            var playerEntry = record.entries.Find(e => e.entryId == playerEntryId);
            if (playerEntry != null && playerEntry.eliminatedRoundIndex == roundIndex)
            {
                cache.playerStatus = ArenaPlayerTournamentStatus.Eliminated;
                Debug.Log($"{TAG} Player eliminated in round {roundIndex}.");
            }
            else if (cache.playerStatus == ArenaPlayerTournamentStatus.Entered)
            {
                cache.playerStatus = ArenaPlayerTournamentStatus.Active;
            }
        }

        cache.currentRoundIndex = roundIndex + 1;
        cache.resultsLastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Check if this was the final round
        if (roundIndex + 1 >= ArenaConstants.TotalRounds)
        {
            CompleteTournament(record, arena, cache);
        }

        SaveRecordToDisk(record);
        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();

        Debug.Log($"{TAG} Resolved round {roundIndex} → {resolved} match(es). Next round: {roundIndex + 1}");
        return roundIndex;
    }

    /// <summary>
    /// Resolves ALL remaining rounds in one call. Useful for testing.
    /// </summary>
    public static void ResolveAllRounds()
    {
        int round;
        do { round = ResolveNextRound(); }
        while (round >= 0);
    }

    // ═════════════════════════════════════════════════════════════
    //  Complete Tournament
    // ═════════════════════════════════════════════════════════════

    private static void CompleteTournament(
        ArenaTournamentRecord record,
        ArenaSaveData arena,
        ArenaCurrentTournamentCache cache)
    {
        record.state = ArenaTournamentState.Completed;
        record.resultsPublishedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Placements
        ArenaPlacementService.AssignFinalPlacements(record);

        // Rewards
        ArenaRewardService.BuildAllRewards(record);

        // Find player placement
        string playerEntryId = cache?.playerEntryId;
        var playerEntry = !string.IsNullOrEmpty(playerEntryId)
            ? record.entries.Find(e => e.entryId == playerEntryId)
            : null;

        int placement = playerEntry?.finalPlacement ?? record.entries.Count;

        // Grant player rewards
        if (playerEntry != null)
            ArenaRewardService.TryGrantPlayerRewards(record, playerEntryId);

        // Update cache
        if (cache != null)
        {
            if (cache.playerStatus != ArenaPlayerTournamentStatus.Eliminated)
                cache.playerStatus = ArenaPlayerTournamentStatus.Completed;
            else
                cache.playerStatus = ArenaPlayerTournamentStatus.Completed;

            cache.currentRoundIndex = ArenaConstants.TotalRounds;
            cache.finalPlacement = placement;
            cache.resultsLastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        // Update lifetime stats
        var stats = arena.lifetimeStats ?? new ArenaLifetimeStats();
        if (placement == 1) stats.championshipsWon++;
        if (placement <= 3) stats.podiumFinishes++;
        if (stats.bestPlacementAllTime <= 0 || placement < stats.bestPlacementAllTime)
            stats.bestPlacementAllTime = placement;
        if (stats.highestRankThisMonth <= 0 || placement < stats.highestRankThisMonth)
            stats.highestRankThisMonth = placement;
        stats.totalPlacementSum += placement;
        arena.lifetimeStats = stats;

        // Write to history
        arena.recentTournamentHistory ??= new List<ArenaTournamentHistoryEntry>();
        arena.recentTournamentHistory.Insert(0, new ArenaTournamentHistoryEntry
        {
            tournamentId = record.tournamentId,
            weekStartUtc = record.weekStartUtc,
            finalPlacement = placement,
            totalEntrants = record.entries.Count,
            scoreBand = record.scoreBand,
            teamSnapshot = playerEntry?.teamSnapshot,
            rewardResult = playerEntry?.rewardResult
        });
        ArenaSaveHelper.TrimArenaHistory(ref arena);

        // Unlock team
        arena.battleTeamData.isLocked = false;
        arena.battleTeamData.lockedTournamentId = "";

        Debug.Log($"{TAG} Tournament '{record.tournamentId}' completed. Player placed #{placement}.");
    }

    // ═════════════════════════════════════════════════════════════
    //  Discard / Reset
    // ═════════════════════════════════════════════════════════════

    /// <summary>Clears the in-memory record and deletes the disk file.</summary>
    public static void DiscardActiveRecord()
    {
        _activeRecord = null;
        DeleteRecordFromDisk();
        Debug.Log($"{TAG} Active tournament record discarded.");
    }

    // ═════════════════════════════════════════════════════════════
    //  JSON Persistence
    // ═════════════════════════════════════════════════════════════

    private static string RecordFilePath =>
        Path.Combine(Application.persistentDataPath, RecordFileName);

    private static void SaveRecordToDisk(ArenaTournamentRecord record)
    {
        try
        {
            string json = JsonUtility.ToJson(record, false);
            File.WriteAllText(RecordFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"{TAG} Failed to save tournament record: {ex.Message}");
        }
    }

    private static ArenaTournamentRecord LoadRecordFromDisk()
    {
        try
        {
            string path = RecordFilePath;
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            if (string.IsNullOrEmpty(json)) return null;

            var record = JsonUtility.FromJson<ArenaTournamentRecord>(json);

            // Validate that the record matches the current save cache
            var arena = SaveManager.GetArenaSaveData();
            var cache = arena?.currentTournamentCache;
            if (cache == null || string.IsNullOrEmpty(cache.tournamentId)) return null;
            if (!string.Equals(record?.tournamentId, cache.tournamentId, StringComparison.Ordinal))
            {
                Debug.Log($"{TAG} Stale tournament record on disk — discarding.");
                DeleteRecordFromDisk();
                return null;
            }

            return record;
        }
        catch (Exception ex)
        {
            Debug.LogError($"{TAG} Failed to load tournament record: {ex.Message}");
            return null;
        }
    }

    private static void DeleteRecordFromDisk()
    {
        try
        {
            string path = RecordFilePath;
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{TAG} Failed to delete tournament record: {ex.Message}");
        }
    }
}
