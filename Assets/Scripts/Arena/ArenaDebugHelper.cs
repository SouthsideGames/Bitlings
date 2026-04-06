#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Non-player-facing debug helpers for the BRN Arena system.
/// Stripped from release builds via preprocessor guard.
/// </summary>
public static class ArenaDebugHelper
{
    const string TAG = "[ArenaDebug]";

    // ─────────────────────────────────────────────────────────────
    // Unlock / Onboarding
    // ─────────────────────────────────────────────────────────────

    public static void ForceUnlockArena()
    {
        var data = SaveManager.GetArenaSaveData();
        data.arenaUnlocked = true;
        data.unlockRewardClaimed = true;
        data.introCompleted = true;
        data.usernameCreated = true;

        if (string.IsNullOrEmpty(data.arenaPlayerId))
            data.arenaPlayerId = Guid.NewGuid().ToString();
        if (string.IsNullOrEmpty(data.arenaUsername))
            data.arenaUsername = "DebugPlayer";

        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();
        Debug.Log($"{TAG} Arena force-unlocked. PlayerId={data.arenaPlayerId}");
    }

    // ─────────────────────────────────────────────────────────────
    // Tickets
    // ─────────────────────────────────────────────────────────────

    public static void GrantTickets(int count)
    {
        var data = SaveManager.GetArenaSaveData();
        data.arenaTickets = Mathf.Clamp(data.arenaTickets + count, 0, 99);
        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();
        Debug.Log($"{TAG} Granted {count} ticket(s). Total={data.arenaTickets}");
    }

    public static void SetTickets(int count)
    {
        var data = SaveManager.GetArenaSaveData();
        data.arenaTickets = Mathf.Clamp(count, 0, 99);
        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();
        Debug.Log($"{TAG} Set tickets to {data.arenaTickets}.");
    }

    // ─────────────────────────────────────────────────────────────
    // Registration / Tournament State
    // ─────────────────────────────────────────────────────────────

    public static void OpenRegistrationState()
    {
        var data = SaveManager.GetArenaSaveData();
        data.currentTournamentCache = new ArenaCurrentTournamentCache();
        data.battleTeamData ??= new ArenaBattleTeamData();
        data.battleTeamData.isLocked = false;
        data.battleTeamData.lockedTournamentId = "";
        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();
        Debug.Log($"{TAG} Reset to open-registration state.");
    }

    // ─────────────────────────────────────────────────────────────
    // Fake Tournament Pool
    // ─────────────────────────────────────────────────────────────

    public static ArenaTournamentRecord CreateFakeTournament(
        int botCount = 31,
        ArenaScoreBand band = ArenaScoreBand.Standard)
    {
        var data = SaveManager.GetArenaSaveData();

        // Build a fake player entry
        var playerEntry = new ArenaTournamentEntry
        {
            entryId = Guid.NewGuid().ToString(),
            tournamentId = "",
            playerId = data.arenaPlayerId ?? "debug-player",
            displayNameSnapshot = string.IsNullOrEmpty(data.arenaUsername) ? "DebugPlayer" : data.arenaUsername,
            isBot = false,
            arenaScore = ArenaScoreCalculator.CalculateArenaTeamScore(),
            teamSnapshot = BuildPlayerSnapshot(data)
        };

        var entries = new List<ArenaTournamentEntry> { playerEntry };

        // Clamp to fill up to BracketSize
        int botsNeeded = Mathf.Clamp(botCount, 1, ArenaConstants.BracketSize - 1);
        var rng = new System.Random();
        var botTemplates = ArenaBotTemplateLibrary.GetTemplatesForBand(band);
        var bots = ArenaBotGenerator.GenerateBotEntries(botsNeeded, band, "debug-tournament", botTemplates, rng);
        entries.AddRange(bots);

        var weekStart = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tournaments = ArenaTournamentBuilder.BuildWeeklyArenaTournaments(entries, weekStart, botTemplates, rng);

        if (tournaments == null || tournaments.Count == 0)
        {
            Debug.LogWarning($"{TAG} BuildWeeklyArenaTournaments returned no tournaments.");
            return null;
        }

        var record = tournaments[0];

        // Wire cache so the game thinks the player is enrolled
        data.currentTournamentCache = new ArenaCurrentTournamentCache
        {
            tournamentId = record.tournamentId,
            weekStartUtc = record.weekStartUtc,
            weekEndUtc = record.weekEndUtc,
            playerEntryId = playerEntry.entryId,
            playerStatus = ArenaPlayerTournamentStatus.Entered,
            currentRoundIndex = 0,
            finalPlacement = 0,
            resultsLastUpdatedUtc = 0
        };

        data.battleTeamData ??= new ArenaBattleTeamData();
        data.battleTeamData.isLocked = true;
        data.battleTeamData.lockedTournamentId = record.tournamentId;
        data.arenaTickets = Mathf.Max(data.arenaTickets - 1, 0);

        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();
        Debug.Log($"{TAG} Created fake tournament '{record.tournamentId}' with {record.entries.Count} entries ({band} band).");
        return record;
    }

    // ─────────────────────────────────────────────────────────────
    // Inject Bots
    // ─────────────────────────────────────────────────────────────

    public static List<ArenaTournamentEntry> InjectBots(
        int count,
        ArenaScoreBand band,
        string tournamentId)
    {
        var rng = new System.Random();
        var templates = ArenaBotTemplateLibrary.GetTemplatesForBand(band);
        var bots = ArenaBotGenerator.GenerateBotEntries(count, band, tournamentId, templates, rng);
        Debug.Log($"{TAG} Generated {bots.Count} bot entries for tournament '{tournamentId}'.");
        return bots;
    }

    // ─────────────────────────────────────────────────────────────
    // Daily Resolution / Full Tournament
    // ─────────────────────────────────────────────────────────────

    public static void SimulateRound(ArenaTournamentRecord record, int roundIndex)
    {
        if (record == null) { Debug.LogWarning($"{TAG} No tournament record provided."); return; }
        int resolved = ArenaMatchResolver.ResolveRound(record, roundIndex);
        ArenaMatchResolver.GenerateNextRoundMatches(record, roundIndex);

        var data = SaveManager.GetArenaSaveData();
        if (data.currentTournamentCache != null &&
            data.currentTournamentCache.tournamentId == record.tournamentId)
        {
            data.currentTournamentCache.currentRoundIndex = roundIndex + 1;
            data.currentTournamentCache.resultsLastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();
        Debug.Log($"{TAG} Resolved round {roundIndex} → {resolved} match(es).");
    }

    public static void InstantlyCompleteTournament(ArenaTournamentRecord record)
    {
        if (record == null) { Debug.LogWarning($"{TAG} No tournament record provided."); return; }

        for (int r = 0; r < ArenaConstants.TotalRounds; r++)
        {
            ArenaMatchResolver.ResolveRound(record, r);
            ArenaMatchResolver.GenerateNextRoundMatches(record, r);
        }

        record.state = ArenaTournamentState.Completed;
        ArenaPlacementService.AssignFinalPlacements(record);

        var data = SaveManager.GetArenaSaveData();
        if (data.currentTournamentCache != null &&
            data.currentTournamentCache.tournamentId == record.tournamentId)
        {
            // Find the player's final placement
            var playerEntryId = data.currentTournamentCache.playerEntryId;
            var playerEntry = record.entries.Find(e => e.entryId == playerEntryId);
            int placement = playerEntry != null ? playerEntry.finalPlacement : record.entries.Count;

            data.currentTournamentCache.playerStatus = ArenaPlayerTournamentStatus.Completed;
            data.currentTournamentCache.currentRoundIndex = ArenaConstants.TotalRounds;
            data.currentTournamentCache.finalPlacement = placement;
            data.currentTournamentCache.resultsLastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Write to history
            data.recentTournamentHistory ??= new List<ArenaTournamentHistoryEntry>();
            data.recentTournamentHistory.Insert(0, new ArenaTournamentHistoryEntry
            {
                tournamentId = record.tournamentId,
                weekStartUtc = record.weekStartUtc,
                finalPlacement = placement,
                totalEntrants = record.entries.Count,
                scoreBand = record.scoreBand,
                teamSnapshot = playerEntry?.teamSnapshot,
                rewardResult = playerEntry?.rewardResult
            });
            ArenaSaveHelper.TrimArenaHistory(ref data);
        }

        data.battleTeamData.isLocked = false;
        data.battleTeamData.lockedTournamentId = "";

        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();
        Debug.Log($"{TAG} Instantly completed tournament '{record.tournamentId}'. State={record.state}");
    }

    // ─────────────────────────────────────────────────────────────
    // Inspect Standings
    // ─────────────────────────────────────────────────────────────

    public static string InspectStandings(ArenaTournamentRecord record)
    {
        if (record == null) return "No tournament record.";

        var sb = new StringBuilder();
        sb.AppendLine($"Tournament: {record.tournamentId}  State: {record.state}  Band: {record.scoreBand}");
        sb.AppendLine($"Entries: {record.entries.Count}  Matches: {record.matches.Count}");
        sb.AppendLine("─────────────────────────────────────");

        // Show entries sorted by placement (0 = unranked)
        var sorted = new List<ArenaTournamentEntry>(record.entries);
        sorted.Sort((a, b) =>
        {
            if (a.finalPlacement == 0 && b.finalPlacement == 0) return 0;
            if (a.finalPlacement == 0) return 1;
            if (b.finalPlacement == 0) return -1;
            return a.finalPlacement.CompareTo(b.finalPlacement);
        });

        foreach (var e in sorted)
        {
            string bot = e.isBot ? " [BOT]" : "";
            string elim = e.eliminatedRoundIndex >= 0 ? $" (elim R{e.eliminatedRoundIndex})" : "";
            sb.AppendLine($"  #{e.finalPlacement,2}  {e.displayNameSnapshot,-20} Score:{e.arenaScore,4}  Seed:{e.seedOrder,2}{bot}{elim}");
        }

        sb.AppendLine("─────────────────────────────────────");
        sb.AppendLine("Matches:");
        foreach (var m in record.matches)
        {
            string winner = string.IsNullOrEmpty(m.winnerEntryId) ? "pending" : m.winnerEntryId[..8];
            sb.AppendLine($"  R{m.roundIndex} {m.matchId[..8]}  L:{m.leftEntryId[..8]} vs R:{m.rightEntryId[..8]}  W:{winner}  T:{m.turnCount}");
        }

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    // Clear History
    // ─────────────────────────────────────────────────────────────

    public static void ClearArenaHistory()
    {
        var data = SaveManager.GetArenaSaveData();
        data.recentTournamentHistory?.Clear();
        data.lifetimeStats = new ArenaLifetimeStats();
        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();
        Debug.Log($"{TAG} Cleared arena history and lifetime stats.");
    }

    public static void FullArenaReset()
    {
        var data = SaveManager.GetArenaSaveData();
        data.arenaUnlocked = false;
        data.unlockRewardClaimed = false;
        data.introCompleted = false;
        data.usernameCreated = false;
        data.arenaPlayerId = "";
        data.arenaUsername = "";
        data.arenaTickets = 0;
        data.weeklyTicketsPurchased = 0;
        data.lastTicketResetUtc = 0;
        data.battleTeamData = new ArenaBattleTeamData();
        data.lifetimeStats = new ArenaLifetimeStats();
        data.currentTournamentCache = new ArenaCurrentTournamentCache();
        data.recentTournamentHistory = new List<ArenaTournamentHistoryEntry>();
        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();
        Debug.Log($"{TAG} Full arena reset complete.");
    }

    // ─────────────────────────────────────────────────────────────
    // Quick Status Dump
    // ─────────────────────────────────────────────────────────────

    public static string DumpSaveState()
    {
        var data = SaveManager.GetArenaSaveData();
        var sb = new StringBuilder();
        sb.AppendLine("=== Arena Save State ===");
        sb.AppendLine($"Unlocked: {data.arenaUnlocked}  Intro: {data.introCompleted}  Username: {data.usernameCreated}");
        sb.AppendLine($"PlayerId: {data.arenaPlayerId}  Username: {data.arenaUsername}");
        sb.AppendLine($"Tickets: {data.arenaTickets}  WeeklyPurchased: {data.weeklyTicketsPurchased}");
        sb.AppendLine($"Team Locked: {data.battleTeamData?.isLocked}  LockedTo: {data.battleTeamData?.lockedTournamentId}");

        var cache = data.currentTournamentCache;
        if (cache != null && !string.IsNullOrEmpty(cache.tournamentId))
        {
            sb.AppendLine($"--- Current Tournament ---");
            sb.AppendLine($"  Id: {cache.tournamentId}");
            sb.AppendLine($"  Status: {cache.playerStatus}  Round: {cache.currentRoundIndex}  Placement: {cache.finalPlacement}");
        }
        else
        {
            sb.AppendLine("No active tournament.");
        }

        var stats = data.lifetimeStats;
        if (stats != null)
        {
            sb.AppendLine($"--- Lifetime Stats ---");
            sb.AppendLine($"  Entered: {stats.tournamentsEntered}  Won: {stats.championshipsWon}  Podium: {stats.podiumFinishes}");
            sb.AppendLine($"  Best: #{stats.bestPlacementAllTime}  HighRank: #{stats.highestRankThisMonth}");
        }

        int histCount = data.recentTournamentHistory?.Count ?? 0;
        sb.AppendLine($"History entries: {histCount}");
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    static ArenaTeamSnapshot BuildPlayerSnapshot(ArenaSaveData data)
    {
        return new ArenaTeamSnapshot
        {
            snapshotId = Guid.NewGuid().ToString(),
            ownerPlayerId = data.arenaPlayerId ?? "debug-player",
            ownerDisplayName = data.arenaUsername ?? "DebugPlayer",
            isBot = false,
            visibilityMode = data.battleTeamData?.visibilityMode ?? ArenaVisibilityMode.FullReveal,
            arenaScore = ArenaScoreCalculator.CalculateArenaTeamScore(),
            createdUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            slotSnapshots = new List<ArenaBitlingSnapshot>()
        };
    }
}
#endif
