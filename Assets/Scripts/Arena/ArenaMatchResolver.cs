// Assets/Scripts/Arena/ArenaMatchResolver.cs
// BRN Arena v1 — High-level match resolution service.
// Resolves individual matches or full tournament rounds, packages results into
// ArenaTournamentMatch, and generates one-line summaries for history lists.
// All resolution is deterministic and async-safe (no coroutines, no UI, no player input).

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static service that resolves arena matches (single or batch) and packages
/// the results into <see cref="ArenaTournamentMatch"/> records.
/// Delegates actual battle simulation to <see cref="ArenaBattleSimulator"/>.
/// </summary>
public static class ArenaMatchResolver
{
    // ═════════════════════════════════════════════════════════════
    //  Public API — Single match
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves a single match in-place.  Populates <c>winnerEntryId</c>,
    /// <c>loserEntryId</c>, <c>turnCount</c>, <c>battleLog</c>, and <c>processedUtc</c>
    /// on the provided <paramref name="match"/>.
    /// </summary>
    /// <param name="match">Match stub with leftEntryId, rightEntryId, and matchSeed already set.</param>
    /// <param name="record">Tournament record containing the entries (used to look up snapshots).</param>
    /// <returns>True if the match was resolved successfully; false if data was missing.</returns>
    public static bool ResolveMatch(ArenaTournamentMatch match, ArenaTournamentRecord record)
    {
        if (match == null || record == null) return false;

        var leftEntry = FindEntry(record, match.leftEntryId);
        var rightEntry = FindEntry(record, match.rightEntryId);

        if (leftEntry == null || rightEntry == null)
        {
            Debug.LogWarning($"[ArenaMatchResolver] Cannot resolve {match.matchId}: " +
                             $"missing entry (left={match.leftEntryId}, right={match.rightEntryId}).");
            return false;
        }

        if (leftEntry.teamSnapshot == null || rightEntry.teamSnapshot == null)
        {
            Debug.LogWarning($"[ArenaMatchResolver] Cannot resolve {match.matchId}: missing team snapshot.");
            return false;
        }

        // ── Build deterministic seed ──
        int seed = BuildDeterministicSeed(
            record.tournamentId, match.roundIndex,
            match.leftEntryId, match.rightEntryId,
            match.matchSeed);

        // ── Run simulation ──
        var result = ArenaBattleSimulator.Simulate(
            leftEntry.teamSnapshot,
            rightEntry.teamSnapshot,
            seed);

        // ── Package result ──
        bool leftWon = result.winningSide == 0;
        match.winnerEntryId = leftWon ? leftEntry.entryId : rightEntry.entryId;
        match.loserEntryId = leftWon ? rightEntry.entryId : leftEntry.entryId;
        match.turnCount = result.turnCount;
        match.battleLog = result.battleLog ?? new List<ArenaBattleLogEvent>();
        match.processedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return true;
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API — Full round resolution
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves all matches for a given round index within a tournament record.
    /// Returns the number of matches successfully resolved.
    /// </summary>
    /// <param name="record">Tournament record containing entries and matches.</param>
    /// <param name="roundIndex">Zero-based round index to resolve.</param>
    public static int ResolveRound(ArenaTournamentRecord record, int roundIndex)
    {
        if (record == null || record.matches == null) return 0;

        int resolved = 0;
        for (int i = 0; i < record.matches.Count; i++)
        {
            var match = record.matches[i];
            if (match == null) continue;
            if (match.roundIndex != roundIndex) continue;
            if (!string.IsNullOrEmpty(match.winnerEntryId)) continue; // already resolved

            if (ResolveMatch(match, record))
                resolved++;
        }

        // ── Update elimination state for losers ──
        for (int i = 0; i < record.matches.Count; i++)
        {
            var match = record.matches[i];
            if (match == null || match.roundIndex != roundIndex) continue;
            if (string.IsNullOrEmpty(match.loserEntryId)) continue;

            var loser = FindEntry(record, match.loserEntryId);
            if (loser != null && loser.eliminatedRoundIndex < 0)
                loser.eliminatedRoundIndex = roundIndex;
        }

        return resolved;
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API — Generate next round matches
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// After resolving a round, generates match stubs for the next round
    /// by pairing winners of consecutive matches.
    /// </summary>
    /// <param name="record">Tournament record (matches list will be appended to).</param>
    /// <param name="resolvedRoundIndex">The round that was just resolved.</param>
    /// <param name="rng">Optional seeded RNG for match seeds. If null, seeds are derived from tournament id.</param>
    /// <returns>Number of new matches created.</returns>
    public static int GenerateNextRoundMatches(
        ArenaTournamentRecord record,
        int resolvedRoundIndex,
        System.Random rng = null)
    {
        if (record == null || record.matches == null) return 0;

        int nextRound = resolvedRoundIndex + 1;
        if (nextRound >= ArenaConstants.TotalRounds) return 0;

        // Collect winners from the resolved round, in match order.
        var roundMatches = new List<ArenaTournamentMatch>();
        for (int i = 0; i < record.matches.Count; i++)
        {
            var m = record.matches[i];
            if (m != null && m.roundIndex == resolvedRoundIndex && !string.IsNullOrEmpty(m.winnerEntryId))
                roundMatches.Add(m);
        }

        if (roundMatches.Count < 2) return 0;

        if (rng == null)
        {
            int seed = BuildDeterministicSeed(record.tournamentId, nextRound, "", "", 0);
            rng = new System.Random(seed);
        }

        int created = 0;
        for (int i = 0; i + 1 < roundMatches.Count; i += 2)
        {
            string leftWinner = roundMatches[i].winnerEntryId;
            string rightWinner = roundMatches[i + 1].winnerEntryId;
            int matchIndex = i / 2;

            var nextMatch = new ArenaTournamentMatch
            {
                matchId = $"{record.tournamentId}_R{nextRound}_M{matchIndex}",
                tournamentId = record.tournamentId,
                roundIndex = nextRound,
                leftEntryId = leftWinner,
                rightEntryId = rightWinner,
                matchSeed = rng.Next()
            };

            record.matches.Add(nextMatch);
            created++;
        }

        return created;
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API — Full tournament resolution
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves an entire tournament from current state to completion.
    /// Processes each round sequentially: resolve matches → generate next round → repeat.
    /// Sets final placements and marks the tournament as <see cref="ArenaTournamentState.Completed"/>.
    /// </summary>
    /// <param name="record">Tournament record with at least round 0 match stubs.</param>
    public static void ResolveFullTournament(ArenaTournamentRecord record)
    {
        if (record == null) return;

        int seed = BuildDeterministicSeed(record.tournamentId, 0, "", "", 0);
        var rng = new System.Random(seed);

        for (int round = 0; round < ArenaConstants.TotalRounds; round++)
        {
            int resolved = ResolveRound(record, round);
            if (resolved == 0) break;

            if (round < ArenaConstants.TotalRounds - 1)
                GenerateNextRoundMatches(record, round, rng);
        }

        // ── Assign final placements (delegated to ArenaPlacementService) ──
        ArenaPlacementService.AssignFinalPlacements(record);

        // ── Build reward results for every entry ──
        ArenaRewardService.BuildAllRewards(record);

        record.state = ArenaTournamentState.Completed;
        record.resultsPublishedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    // ═════════════════════════════════════════════════════════════
    //  Public API — Summary generation
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a one-line match summary from the perspective of a specific entry.
    /// Examples: "Won in 7 turns", "Lost after 11 turns", "Won by final knockout on turn 9".
    /// </summary>
    /// <param name="match">A resolved match.</param>
    /// <param name="perspectiveEntryId">The entry id whose perspective to use.</param>
    /// <returns>Short summary string suitable for history list display.</returns>
    public static string GenerateMatchSummary(ArenaTournamentMatch match, string perspectiveEntryId)
    {
        if (match == null || string.IsNullOrEmpty(match.winnerEntryId))
            return "Match pending";

        bool isWinner = string.Equals(match.winnerEntryId, perspectiveEntryId, StringComparison.Ordinal);
        int turns = Mathf.Max(1, match.turnCount);

        // Check for final knockout (last event before Victory is a Knockout).
        bool finalKnockout = false;
        int knockoutTurn = turns;
        if (match.battleLog != null && match.battleLog.Count >= 2)
        {
            for (int i = match.battleLog.Count - 1; i >= 0; i--)
            {
                var evt = match.battleLog[i];
                if (evt.eventType == ArenaBattleLogEventType.Victory) continue;
                if (evt.eventType == ArenaBattleLogEventType.Knockout)
                {
                    finalKnockout = true;
                    knockoutTurn = evt.turn + 1; // display as 1-based
                    break;
                }
                break; // only check the event immediately before Victory
            }
        }

        if (isWinner)
        {
            if (finalKnockout)
                return $"Won by final knockout on turn {knockoutTurn}";
            return $"Won in {turns} turn{(turns != 1 ? "s" : "")}";
        }
        else
        {
            return $"Lost after {turns} turn{(turns != 1 ? "s" : "")}";
        }
    }

    /// <summary>
    /// Generates a one-line tournament result summary for a specific entry.
    /// Examples: "Champion!", "Eliminated in round 3", "Placed 5th".
    /// </summary>
    public static string GenerateTournamentSummary(ArenaTournamentRecord record, string entryId)
    {
        if (record == null || string.IsNullOrEmpty(entryId))
            return "";

        var entry = FindEntry(record, entryId);
        if (entry == null)
            return "";

        int placement = entry.finalPlacement;
        if (placement <= 0)
            return "In progress";

        if (placement == 1) return "Champion!";
        if (placement == 2) return "Runner-up";
        if (placement == 3) return "3rd place";

        return $"Placed {GetOrdinal(placement)}";
    }

    // ═════════════════════════════════════════════════════════════
    //  Deterministic seed construction
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a stable deterministic seed from tournament/match identifiers.
    /// Ensures identical outcomes given the same bracket data.
    /// </summary>
    public static int BuildDeterministicSeed(
        string tournamentId, int roundIndex,
        string leftEntryId, string rightEntryId,
        int baseSeed)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (tournamentId != null ? tournamentId.GetHashCode() : 0);
            hash = hash * 31 + roundIndex;
            hash = hash * 31 + (leftEntryId != null ? leftEntryId.GetHashCode() : 0);
            hash = hash * 31 + (rightEntryId != null ? rightEntryId.GetHashCode() : 0);
            hash = hash * 31 + baseSeed;
            return hash;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Entry lookup
    // ═════════════════════════════════════════════════════════════

    private static ArenaTournamentEntry FindEntry(ArenaTournamentRecord record, string entryId)
    {
        if (record.entries == null || string.IsNullOrEmpty(entryId)) return null;
        for (int i = 0; i < record.entries.Count; i++)
        {
            if (string.Equals(record.entries[i].entryId, entryId, StringComparison.Ordinal))
                return record.entries[i];
        }
        return null;
    }

    // ═════════════════════════════════════════════════════════════
    //  Ordinal helper
    // ═════════════════════════════════════════════════════════════

    private static string GetOrdinal(int n)
    {
        if (n <= 0) return n.ToString();
        int rem100 = n % 100;
        if (rem100 >= 11 && rem100 <= 13) return $"{n}th";
        switch (n % 10)
        {
            case 1: return $"{n}st";
            case 2: return $"{n}nd";
            case 3: return $"{n}rd";
            default: return $"{n}th";
        }
    }
}
