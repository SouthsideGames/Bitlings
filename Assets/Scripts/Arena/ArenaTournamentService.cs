// Assets/Scripts/Arena/ArenaTournamentService.cs
// BRN Arena v1 — Runtime orchestration service that ties together tournament
// creation, entry, round resolution, rewards, and record persisteAnce.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Services.Authentication;
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
    //  Enter Tournament (Online — async Cloud Code)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Online entry flow: validate → spend ticket → freeze snapshot → register via Cloud Code.
    /// Sets status to Registered. Bracket assignment happens later via <see cref="SyncBracketAsync"/>.
    /// </summary>
    public static async Task<(bool success, string error)> TryEnterTournamentAsync()
    {
        // ── Prerequisites ──
        if (!ArenaNetworkGuard.IsOnline)
            return (false, "No connection. Try again later.");

        if (!ArenaSaveHelper.IsArenaUnlocked())
            return (false, "Arena is not unlocked.");

        if (!ArenaSaveHelper.HasArenaUsername())
            return (false, "Set an Arena username first.");

        if (!ArenaTeamValidator.IsBattleTeamComplete())
            return (false, "Complete your Battle Team first.");

        if (ArenaTicketManager.GetTicketCount() <= 0)
            return (false, "You need an Arena Ticket to enter.");

        // UI-only gate — device clock can be manipulated. The authoritative check
        // is enforced server-side in Cloud Code. Do not treat this as a security boundary. // FIXED: documents trust model
        if (!ArenaScheduleService.IsRegistrationOpen())
            return (false, "Registration is currently closed.");

        var arena = SaveManager.GetArenaSaveData();
        var status = arena?.currentTournamentCache?.playerStatus ?? ArenaPlayerTournamentStatus.NotEntered;
        if (status != ArenaPlayerTournamentStatus.NotEntered)
            return (false, "Already entered a tournament this week.");

        // Online snapshot ownership must match Cloud Code's authenticated player.
        string authenticatedPlayerId = ResolveAuthenticatedPlayerId();
        if (string.IsNullOrEmpty(authenticatedPlayerId))
            return (false, "Online session is still initializing. Please try again.");

        // Keep arena identity synced so local systems and cloud validation agree.
        if (arena != null && arena.arenaPlayerId != authenticatedPlayerId)
        {
            arena.arenaPlayerId = authenticatedPlayerId;
            SaveManager.Save();
        }

        // ── Mark pending BEFORE spending so a crash between here and registration is recoverable ──
        var arenaForFlag = SaveManager.GetArenaSaveData();
        if (arenaForFlag != null)
        {
            arenaForFlag.ticketSpentPendingRegistration = true; // FIXED: written to disk before ticket is spent
            SaveManager.Save();
        }

        if (!ArenaTicketManager.TrySpendTicket())
        {
            if (arenaForFlag != null) { arenaForFlag.ticketSpentPendingRegistration = false; SaveManager.Save(); }
            return (false, "Unable to spend ticket.");
        }

        // ── Build player entry snapshot ──
        string playerId = authenticatedPlayerId;
        string displayName = !string.IsNullOrEmpty(arena.arenaUsername) ? arena.arenaUsername : "Player";

        var playerSnapshot = ArenaTournamentBuilder.CreateTournamentEntrySnapshot(playerId, displayName);
        int playerScore = ArenaScoreCalculator.CalculateArenaTeamScore(playerSnapshot);
        var band = ArenaScoreCalculator.GetBattleTeamScoreBand(playerScore);

        string snapshotJson = JsonUtility.ToJson(playerSnapshot);
        string weekId = ArenaScheduleService.GetCurrentWeekId();

        // ── Register via Cloud Code ──
        var result = await ArenaCloudCodeService.RegisterForTournamentAsync(
            snapshotJson, playerScore, (int)band, displayName, weekId);

        if (!result.success)
        {
            // Refund the ticket since registration failed
            var refundArena = SaveManager.GetArenaSaveData();
            if (refundArena != null)
            {
                refundArena.arenaTickets = Math.Min(refundArena.arenaTickets + 1, ArenaConstants.MaxTickets);
                SaveManager.Save();

                // Clear the pending flag now that the outcome is known
                var arenaForClear = SaveManager.GetArenaSaveData();
                if (arenaForClear != null)
                {
                    arenaForClear.ticketSpentPendingRegistration = false; // FIXED: pending flag cleared on known outcome
                    SaveManager.Save();
                }
            }
            return (false, result.error ?? "Registration failed.");
        }

        // ── Update save data ──
        long weekStart = ArenaScheduleService.GetCurrentWeekStartUtc();
        long weekEnd = ArenaScheduleService.GetCurrentWeekEndUtc();

        arena.currentTournamentCache = new ArenaCurrentTournamentCache
        {
            tournamentId = "", // Assigned later when bracket is built
            weekStartUtc = weekStart,
            weekEndUtc = weekEnd,
            playerEntryId = "", // Assigned later
            playerStatus = ArenaPlayerTournamentStatus.Registered,
            currentRoundIndex = 0,
            finalPlacement = 0,
            resultsLastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Lock team
        arena.battleTeamData ??= new ArenaBattleTeamData();
        arena.battleTeamData.isLocked = true;
        arena.battleTeamData.lockedTournamentId = $"pending_{weekId}";

        // Lifetime stats
        arena.lifetimeStats ??= new ArenaLifetimeStats();
        arena.lifetimeStats.tournamentsEntered++;

        SaveManager.Save();

        // Clear the pending flag now that the outcome is known
        var successArenaForClear = SaveManager.GetArenaSaveData();
        if (successArenaForClear != null)
        {
            successArenaForClear.ticketSpentPendingRegistration = false; // FIXED: pending flag cleared on known outcome
            SaveManager.Save();
        }

        GameEvents.ArenaDataChanged?.Invoke();

        DevLog.Log($"{TAG} Registered for tournament week {weekId} ({band} band, score {playerScore}).");
        return (true, null);
    }

    private static string ResolveAuthenticatedPlayerId()
    {
        try
        {
            if (AuthenticationService.Instance != null &&
                AuthenticationService.Instance.IsSignedIn &&
                !string.IsNullOrEmpty(AuthenticationService.Instance.PlayerId))
            {
                return AuthenticationService.Instance.PlayerId;
            }
        }
        catch
        {
            // UGS may not be initialized yet; fall back to initializer cache.
        }

        return UGSInitializer.I != null ? UGSInitializer.I.PlayerId : null;
    }

    // ═════════════════════════════════════════════════════════════
    //  Bracket Sync (poll server for bracket assignment)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Polls the server for the player's bracket assignment.
    /// If assigned, builds the full bracket locally (with deterministic bot backfill)
    /// and transitions status from Registered → Entered.
    /// Returns true if the bracket was newly synced.
    /// </summary>
    public static async Task<(bool synced, string message)> SyncBracketAsync()
    {
        if (!ArenaNetworkGuard.IsOnline)
            return (false, "No connection.");

        var arena = SaveManager.GetArenaSaveData();
        var cache = arena?.currentTournamentCache;
        if (cache == null || cache.playerStatus != ArenaPlayerTournamentStatus.Registered)
            return (false, "Not in Registered state.");

        string weekId = ArenaScheduleService.GetCurrentWeekId();
        var result = await ArenaCloudCodeService.GetTournamentBracketAsync(weekId);

        if (!result.assigned)
            return (false, result.reason ?? "Brackets not ready yet.");

        // ── Build full bracket locally ──
        var bracket = result.bracket;
        var record = BuildBracketFromServerData(bracket, result.entryId);

        if (record == null)
            return (false, "Failed to build bracket from server data.");

        // ── Update save data ──
        cache.tournamentId = record.tournamentId;
        cache.playerEntryId = result.entryId;
        cache.playerStatus = ArenaPlayerTournamentStatus.Entered;
        cache.weekStartUtc = record.weekStartUtc;
        cache.weekEndUtc = record.weekEndUtc;
        cache.currentRoundIndex = 0;
        cache.resultsLastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        arena.battleTeamData.lockedTournamentId = record.tournamentId;

        _activeRecord = record;
        SaveRecordToDisk(record);
        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();

        DevLog.Log($"{TAG} Bracket synced: '{record.tournamentId}' ({record.entries.Count} entries, {bracket.realPlayerCount} real).");
        return (true, null);
    }

    /// <summary>
    /// Builds a full ArenaTournamentRecord from server bracket data.
    /// Creates real player entries from server data, generates bot backfill deterministically,
    /// shuffles all entries with the bracket seed, and creates round 1 match stubs.
    /// </summary>
    private static ArenaTournamentRecord BuildBracketFromServerData(
        ArenaCloudCodeService.BracketData serverBracket, string playerEntryId)
    {
        try
        {
            var rng = new System.Random(serverBracket.bracketSeed);
            var band = (ArenaScoreBand)serverBracket.scoreBand;

            // Deserialize real player entries
            var allEntries = new List<ArenaTournamentEntry>();
            foreach (var re in serverBracket.realEntries)
            {
                var snapshot = JsonUtility.FromJson<ArenaTeamSnapshot>(re.teamSnapshotJson);
                allEntries.Add(new ArenaTournamentEntry
                {
                    entryId = re.entryId,
                    tournamentId = serverBracket.tournamentId,
                    playerId = re.playerId,
                    displayNameSnapshot = re.displayName,
                    isBot = false,
                    arenaScore = re.arenaScore,
                    teamSnapshot = snapshot,
                    eliminatedRoundIndex = -1,
                    finalPlacement = 0
                });
            }

            // Generate bot backfill deterministically
            int botsNeeded = ArenaConstants.BracketSize - allEntries.Count;
            if (botsNeeded > 0)
            {
                var botTemplates = ArenaBotTemplateLibrary.GetTemplatesForBand(band);
                var bots = ArenaBotGenerator.GenerateBotEntries(
                    botsNeeded, band, serverBracket.tournamentId, botTemplates, rng);
                allEntries.AddRange(bots);
            }

            // Fisher-Yates shuffle with bracket seed
            for (int i = allEntries.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (allEntries[i], allEntries[j]) = (allEntries[j], allEntries[i]);
            }

            // Assign seed orders
            for (int i = 0; i < allEntries.Count; i++)
            {
                allEntries[i].seedOrder = i + 1;
                allEntries[i].tournamentId = serverBracket.tournamentId;
            }

            // Generate round 1 match stubs
            var matches = new List<ArenaTournamentMatch>();
            for (int i = 0; i < allEntries.Count; i += 2)
            {
                if (i + 1 >= allEntries.Count) break;
                matches.Add(new ArenaTournamentMatch
                {
                    matchId = $"{serverBracket.tournamentId}_R0_M{i / 2}",
                    tournamentId = serverBracket.tournamentId,
                    roundIndex = 0,
                    leftEntryId = allEntries[i].entryId,
                    rightEntryId = allEntries[i + 1].entryId,
                    matchSeed = rng.Next()
                });
            }

            return new ArenaTournamentRecord
            {
                tournamentId = serverBracket.tournamentId,
                weekStartUtc = serverBracket.weekStartUtc,
                weekEndUtc = serverBracket.weekEndUtc,
                state = ArenaTournamentState.Active,
                bracketSize = ArenaConstants.BracketSize,
                scoreBand = band,
                bracketSeed = serverBracket.bracketSeed,
                entries = allEntries,
                matches = matches
            };
        }
        catch (Exception ex)
        {
            Debug.LogError($"{TAG} BuildBracketFromServerData failed: {ex.Message}");
            return null;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Enter Tournament (Local / Offline — legacy)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Local-only entry flow: validate → spend ticket → freeze team → build bracket → persist.
    /// Used for offline play or testing. Returns true on success.
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

        if (!ArenaSaveHelper.HasArenaUsername())
        {
            errorMessage = "Set an Arena username first.";
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
        string playerId = !string.IsNullOrEmpty(arena.arenaPlayerId)
            ? arena.arenaPlayerId
            : Guid.NewGuid().ToString("N");
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

        DevLog.Log($"{TAG} Entered tournament '{record.tournamentId}' ({band} band, {record.entries.Count} entries). PlayerEntry={entryId}");
        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  Resolve Rounds
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves the next unresolved round. Returns the round index that was resolved, or -1 if none remain.
    /// Online mode: obeys the daily schedule (rounds unlock Wed–Sun at BattleResolveHourET).
    /// Local mode: resolves immediately.
    /// </summary>
    public static int ResolveNextRound(bool respectSchedule = true)
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
            DevLog.Log($"{TAG} All rounds already resolved.");
            return -1;
        }

        // Online mode: only allow resolution when the schedule says this round is available.
        if (respectSchedule && !ArenaScheduleService.IsRoundAvailable(roundIndex))
        {
            DevLog.Log($"{TAG} Round {roundIndex} is not yet available per schedule.");
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
                ArenaLoadoutManager.UnlockTeam();
                DevLog.Log($"{TAG} Player eliminated in round {roundIndex}.");
            }
            else if (cache.playerStatus == ArenaPlayerTournamentStatus.Entered)
            {
                cache.playerStatus = ArenaPlayerTournamentStatus.Active;
            }
        }

        cache.currentRoundIndex = roundIndex + 1;
        GameEvents.ArenaDataChanged?.Invoke();
        cache.resultsLastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Check if this was the final round
        if (roundIndex + 1 >= ArenaConstants.TotalRounds)
        {
            CompleteTournament(record, arena, cache);
        }

        SaveRecordToDisk(record);
        SaveManager.Save();
        GameEvents.ArenaDataChanged?.Invoke();

        DevLog.Log($"{TAG} Resolved round {roundIndex} → {resolved} match(es). Next round: {roundIndex + 1}");
        return roundIndex;
    }

    /// <summary>
    /// Resolves ALL remaining rounds in one call. Useful for testing.
    /// </summary>
    public static void ResolveAllRounds()
    {
        int round;
        do { round = ResolveNextRound(respectSchedule: false); }
        while (round >= 0);
    }

    /// <summary>
    /// Resolves all rounds that are available per the schedule.
    /// Called when the player opens the arena to catch up on missed rounds.
    /// Returns the number of rounds resolved.
    /// </summary>
    public static int ResolveAvailableRounds()
    {
        var cache = SaveManager.GetArenaSaveData()?.currentTournamentCache;
        bool weekEnded = cache != null
                      && cache.weekEndUtc > 0
                      && SaveManager.NowUnix() > cache.weekEndUtc;

        int count = 0;
        int round;
        do
        {
            round = ResolveNextRound(respectSchedule: !weekEnded);
            if (round >= 0) count++;
        }
        while (round >= 0);
        return count;
    }

    /// <summary>
    /// Repairs a missing history row for a completed tournament using the active
    /// tournament record and current cache values. Safe to call repeatedly.
    /// Returns <c>true</c> when a row was inserted.
    /// </summary>
    public static bool TryBackfillMissingHistoryFromActiveRecord()
    {
        var arena = SaveManager.GetArenaSaveData();
        var cache = arena?.currentTournamentCache;
        if (arena == null || cache == null) return false;

        string tournamentId = cache.tournamentId;
        if (string.IsNullOrEmpty(tournamentId)) return false;

        arena.recentTournamentHistory ??= new List<ArenaTournamentHistoryEntry>();
        for (int i = 0; i < arena.recentTournamentHistory.Count; i++)
        {
            var hist = arena.recentTournamentHistory[i];
            if (hist != null && string.Equals(hist.tournamentId, tournamentId, StringComparison.Ordinal))
                return false;
        }

        var record = GetActiveRecord();
        if (record == null) return false;
        if (!string.Equals(record.tournamentId, tournamentId, StringComparison.Ordinal)) return false;

        bool completed = record.state == ArenaTournamentState.Completed
                      || cache.playerStatus == ArenaPlayerTournamentStatus.Completed;
        if (!completed) return false;

        var playerEntry = FindPlayerEntry(record, cache.playerEntryId);
        int placement = cache.finalPlacement > 0
            ? cache.finalPlacement
            : (playerEntry?.finalPlacement ?? 0);
        if (placement <= 0) return false;

        ArenaRewardService.BuildAllRewards(record);

        arena.recentTournamentHistory.Insert(0, new ArenaTournamentHistoryEntry
        {
            tournamentId = record.tournamentId,
            weekStartUtc = record.weekStartUtc,
            finalPlacement = placement,
            totalEntrants = record.entries?.Count ?? ArenaConstants.BracketSize,
            scoreBand = record.scoreBand,
            teamSnapshot = playerEntry?.teamSnapshot,
            rewardResult = playerEntry?.rewardResult
        });

        ArenaSaveHelper.TrimArenaHistory(ref arena);
        SaveRecordToDisk(record);
        SaveManager.Save();

        DevLog.Log($"{TAG} Backfilled missing history row for tournament '{record.tournamentId}'.");
        return true;
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

        // Submit to leaderboards (fire-and-forget)
        SubmitLeaderboardScores(placement, arena.lifetimeStats);

        DevLog.Log($"{TAG} Tournament '{record.tournamentId}' completed. Player placed #{placement}.");
    }

    /// <summary>Fire-and-forget leaderboard score submission after tournament completion.</summary>
    private static async void SubmitLeaderboardScores(int placement, ArenaLifetimeStats stats)
    {
        if (!ArenaNetworkGuard.IsOnline) return;

        try
        {
            await ArenaLeaderboardService.SubmitWeeklyPlacementAsync(placement);

            if (placement == 1 && stats != null)
                await ArenaLeaderboardService.SubmitAllTimeChampionshipsAsync(stats.championshipsWon);

            // Publish updated public profile
            await ArenaPlayerProfileService.PublishProfileAsync();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{TAG} Leaderboard/profile submission failed: {ex.Message}");
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Discard / Reset
    // ═════════════════════════════════════════════════════════════

    /// <summary>Clears the in-memory record and deletes the disk file.</summary>
    public static void DiscardActiveRecord()
    {
        _activeRecord = null;
        DeleteRecordFromDisk();
        DevLog.Log($"{TAG} Active tournament record discarded.");
    }

    // ═════════════════════════════════════════════════════════════
    //  JSON Persistence
    // ═════════════════════════════════════════════════════════════

    private static string RecordFilePath =>
        Path.Combine(Application.persistentDataPath, RecordFileName);

    private static ArenaTournamentEntry FindPlayerEntry(ArenaTournamentRecord record, string playerEntryId)
    {
        if (record?.entries == null) return null;

        if (!string.IsNullOrEmpty(playerEntryId))
        {
            for (int i = 0; i < record.entries.Count; i++)
            {
                var e = record.entries[i];
                if (e != null && string.Equals(e.entryId, playerEntryId, StringComparison.Ordinal))
                    return e;
            }
        }

        for (int i = 0; i < record.entries.Count; i++)
        {
            var e = record.entries[i];
            if (e != null && !e.isBot)
                return e;
        }

        return null;
    }

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
                DevLog.Log($"{TAG} Stale tournament record on disk — discarding.");
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
