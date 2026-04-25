// Assets/Scripts/Arena/ArenaPlacementService.cs
// BRN Arena v1 — Final placement ordering within elimination rounds.
// Entries that survive longer always place higher. Within the same elimination
// round, tiebreaking uses: (1) opponent arena score, (2) match performance,
// (3) deterministic fallback. Players never see the internal tiebreak.

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static service that assigns final placement numbers (1–32) to every entry
/// in a completed tournament and builds the <see cref="ArenaTournamentStandings"/>.
/// </summary>
public static class ArenaPlacementService
{
    // ═════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Assigns <see cref="ArenaTournamentEntry.finalPlacement"/> for every entry
    /// and populates <see cref="ArenaTournamentRecord.standings"/>.
    /// Must be called after all rounds are resolved and elimination indices set.
    /// </summary>
    public static void AssignFinalPlacements(ArenaTournamentRecord record)
    {
        if (record == null || record.entries == null) return;

        // ── Group entries by elimination round ──
        // eliminatedRoundIndex = -1 → champion (never eliminated)
        var byRound = new Dictionary<int, List<ArenaTournamentEntry>>();

        for (int i = 0; i < record.entries.Count; i++)
        {
            var e = record.entries[i];
            if (e == null) continue;
            int key = e.eliminatedRoundIndex;
            if (!byRound.ContainsKey(key))
                byRound[key] = new List<ArenaTournamentEntry>();
            byRound[key].Add(e);
        }

        int currentPlacement = 1;

        // ── Champion(s) — eliminatedRoundIndex = -1 ──
        if (byRound.TryGetValue(-1, out var champions))
        {
            for (int i = 0; i < champions.Count; i++)
            {
                champions[i].finalPlacement = currentPlacement;
                currentPlacement++;
            }
        }

        // ── Then by elimination round descending (finals → round of 32) ──
        for (int round = ArenaConstants.TotalRounds - 1; round >= 0; round--)
        {
            if (!byRound.TryGetValue(round, out var group)) continue;

            OrderEntriesWithinRound(group, record, round);

            for (int i = 0; i < group.Count; i++)
            {
                group[i].finalPlacement = currentPlacement;
                currentPlacement++;
            }
        }

        // ── Build standings ──
        BuildStandings(record);
    }

    // ═════════════════════════════════════════════════════════════
    //  Within-round ordering
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Orders entries eliminated in the same round using the locked tiebreak chain:
    ///   1. Opponent arena score — lost to a stronger opponent → better placement (descending).
    ///   2. Match performance — survived more turns → better placement (descending).
    ///   3. Deterministic fallback — stable hash of entry id (ascending).
    /// </summary>
    private static void OrderEntriesWithinRound(
        List<ArenaTournamentEntry> group,
        ArenaTournamentRecord record,
        int roundIndex)
    {
        if (group == null || group.Count <= 1) return;

        // Pre-compute tiebreak data for each entry in the group.
        var meta = new Dictionary<string, PlacementMeta>(group.Count);
        for (int i = 0; i < group.Count; i++)
        {
            var entry = group[i];
            meta[entry.entryId] = BuildPlacementMeta(entry, record, roundIndex);
        }

        group.Sort((a, b) =>
        {
            var ma = meta[a.entryId];
            var mb = meta[b.entryId];

            // 1. Opponent arena score — higher is better (lost to a stronger foe).
            int cmp = mb.opponentArenaScore.CompareTo(ma.opponentArenaScore);
            if (cmp != 0) return cmp;

            // 2. Match performance — survived more turns → better.
            cmp = mb.turnsSurvived.CompareTo(ma.turnsSurvived);
            if (cmp != 0) return cmp;

            // 3. Deterministic fallback — stable hash ascending.
            return ma.deterministicHash.CompareTo(mb.deterministicHash);
        });
    }

    // ═════════════════════════════════════════════════════════════
    //  Tiebreak metadata
    // ═════════════════════════════════════════════════════════════

    private struct PlacementMeta
    {
        public int opponentArenaScore;
        public int turnsSurvived;
        public int deterministicHash;
    }

    /// <summary>
    /// Extracts tiebreak metadata for an entry from its elimination match.
    /// </summary>
    private static PlacementMeta BuildPlacementMeta(
        ArenaTournamentEntry entry,
        ArenaTournamentRecord record,
        int roundIndex)
    {
        var pm = new PlacementMeta
        {
            opponentArenaScore = 0,
            turnsSurvived = 0,
            deterministicHash = StableHash(entry.entryId)
        };

        // Find the match where this entry was eliminated.
        var match = FindEliminationMatch(entry.entryId, record, roundIndex);
        if (match == null) return pm;

        // Opponent = the winner of that match.
        string opponentId = string.Equals(match.winnerEntryId, entry.entryId, StringComparison.Ordinal)
            ? match.loserEntryId
            : match.winnerEntryId;

        var opponent = FindEntry(record, opponentId);
        if (opponent != null)
            pm.opponentArenaScore = opponent.arenaScore;

        pm.turnsSurvived = Mathf.Max(1, match.turnCount);

        return pm;
    }

    /// <summary>
    /// Finds the match in which the given entry was eliminated (lost) during a specific round.
    /// </summary>
    private static ArenaTournamentMatch FindEliminationMatch(
        string entryId, ArenaTournamentRecord record, int roundIndex)
    {
        if (record.matches == null) return null;

        for (int i = 0; i < record.matches.Count; i++)
        {
            var m = record.matches[i];
            if (m == null || m.roundIndex != roundIndex) continue;
            if (string.Equals(m.loserEntryId, entryId, StringComparison.Ordinal))
                return m;
        }
        return null;
    }

    // ═════════════════════════════════════════════════════════════
    //  Standings builder
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Populates <see cref="ArenaTournamentRecord.standings"/> with the sorted
    /// placement order and participant counts.
    /// </summary>
    private static void BuildStandings(ArenaTournamentRecord record)
    {
        if (record.standings == null)
            record.standings = new ArenaTournamentStandings();

        var sorted = new List<ArenaTournamentEntry>(record.entries);
        sorted.Sort((a, b) => a.finalPlacement.CompareTo(b.finalPlacement));

        record.standings.placementOrder = new List<string>(sorted.Count);
        int realCount = 0;
        int botCount = 0;

        for (int i = 0; i < sorted.Count; i++)
        {
            record.standings.placementOrder.Add(sorted[i].entryId);
            if (sorted[i].isBot) botCount++;
            else realCount++;
        }

        record.standings.realPlayerCount = realCount;
        record.standings.botCount = botCount;
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers
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

    /// <summary>
    /// Produces a stable, deterministic hash from a string.
    /// Unlike <see cref="string.GetHashCode"/>, this is identical across app domains.
    /// </summary>
    private static int StableHash(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < s.Length; i++)
                hash = hash * 31 + s[i];
            return hash;
        }
    }
}
