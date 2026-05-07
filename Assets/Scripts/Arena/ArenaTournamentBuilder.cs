// Assets/Scripts/Arena/ArenaTournamentBuilder.cs
// BRN Arena v1 — Tournament creation pipeline: score-band grouping, pool merging,
// bracket assembly, bot filling, and snapshot freezing.
// Called once per week after registration closes (Monday 11:59 PM ET).

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static utility that transforms a list of registered player entries into one or more
/// fully-formed <see cref="ArenaTournamentRecord"/> brackets, complete with bot backfill.
/// No live match resolution — this is bracket *creation* only.
/// </summary>
public static class ArenaTournamentBuilder
{
    /// <summary>Minimum real entrants per score band before it must merge into an adjacent band.</summary>
    private const int MinRealEntrantsPerBand = 8;

    // ═════════════════════════════════════════════════════════════
    //  Public pipeline
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Master entry point.  Takes all valid registered entries for the week and produces
    /// a list of tournament records (one per 32-player bracket).
    /// <para>Flow: score → band → merge small pools → split into bracket-sized chunks →
    /// backfill bots → create records.</para>
    /// </summary>
    /// <param name="registeredEntries">Pre-validated player entries (team snapshots already frozen).</param>
    /// <param name="weekStartUtc">UTC epoch of Monday 00:00 ET for this tournament week.</param>
    /// <param name="botTemplates">Available bot templates (may be null/empty — fallback bots used).</param>
    /// <param name="rng">Seeded RNG for deterministic shuffling / bot generation.
    /// If null a new System.Random with a time-based seed is created.</param>
    public static List<ArenaTournamentRecord> BuildWeeklyArenaTournaments(
        List<ArenaTournamentEntry> registeredEntries,
        long weekStartUtc,
        List<ArenaBotTeamTemplate> botTemplates = null,
        System.Random rng = null)
    {
        if (rng == null)
            rng = new System.Random((int)(weekStartUtc ^ DateTimeOffset.UtcNow.Ticks));

        registeredEntries ??= new List<ArenaTournamentEntry>();

        // 1. Score each entry and assign to band.
        ScoreAllEntries(registeredEntries);
        var pools = GroupEntriesByScoreBand(registeredEntries);

        // 2. Merge undersized pools.
        var mergedPools = MergeSmallScorePools(pools);

        // 3. Build brackets from each final pool.
        var results = new List<ArenaTournamentRecord>();
        foreach (var kv in mergedPools)
        {
            var band = kv.Key;
            var pool = kv.Value;
            var brackets = CreateBracketsFromPool(pool, band, weekStartUtc, botTemplates, rng);
            results.AddRange(brackets);
        }

        return results;
    }

    // ═════════════════════════════════════════════════════════════
    //  Step 1 — Score each entry
    // ═════════════════════════════════════════════════════════════

    private static void ScoreAllEntries(List<ArenaTournamentEntry> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;

            int score = 0;
            if (e.teamSnapshot != null)
                score = ArenaScoreCalculator.CalculateArenaTeamScore(e.teamSnapshot);

            e.arenaScore = score;
            if (e.teamSnapshot != null)
                e.teamSnapshot.arenaScore = score;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Step 2 — Group by score band
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Buckets entries into the four hidden score bands based on their arena score.
    /// Returns a dictionary keyed by <see cref="ArenaScoreBand"/> with real player lists.
    /// </summary>
    public static Dictionary<ArenaScoreBand, List<ArenaTournamentEntry>> GroupEntriesByScoreBand(
        List<ArenaTournamentEntry> entries)
    {
        var pools = new Dictionary<ArenaScoreBand, List<ArenaTournamentEntry>>
        {
            [ArenaScoreBand.Low] = new List<ArenaTournamentEntry>(),
            [ArenaScoreBand.Standard] = new List<ArenaTournamentEntry>(),
            [ArenaScoreBand.High] = new List<ArenaTournamentEntry>(),
            [ArenaScoreBand.Elite] = new List<ArenaTournamentEntry>()
        };

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null || e.isBot) continue;

            var band = ArenaScoreCalculator.GetBattleTeamScoreBand(e.arenaScore);
            pools[band].Add(e);
        }

        return pools;
    }

    // ═════════════════════════════════════════════════════════════
    //  Step 3 — Merge undersized pools
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// If any band has fewer than <see cref="MinRealEntrantsPerBand"/> real entrants,
    /// its members merge into the nearest adjacent band (preferring the band with more entrants
    /// if equidistant).
    /// </summary>
    public static Dictionary<ArenaScoreBand, List<ArenaTournamentEntry>> MergeSmallScorePools(
        Dictionary<ArenaScoreBand, List<ArenaTournamentEntry>> pools)
    {
        // Ordered band list for adjacency.
        var orderedBands = new ArenaScoreBand[]
        {
            ArenaScoreBand.Low,
            ArenaScoreBand.Standard,
            ArenaScoreBand.High,
            ArenaScoreBand.Elite
        };

        // Ensure all bands present.
        foreach (var b in orderedBands)
            if (!pools.ContainsKey(b))
                pools[b] = new List<ArenaTournamentEntry>();

        // Iterate until no undersized non-empty bands remain.
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < orderedBands.Length; i++)
            {
                var band = orderedBands[i];
                var list = pools[band];
                if (list.Count == 0 || list.Count >= MinRealEntrantsPerBand)
                    continue;

                // Find nearest non-empty adjacent band.
                ArenaScoreBand? target = FindNearestAdjacentBand(pools, orderedBands, i);
                if (target == null)
                    continue; // only band with entries — keep it

                pools[target.Value].AddRange(list);
                list.Clear();
                changed = true;
            }
        }

        // Remove empty bands.
        var result = new Dictionary<ArenaScoreBand, List<ArenaTournamentEntry>>();
        foreach (var b in orderedBands)
            if (pools[b].Count > 0)
                result[b] = pools[b];

        return result;
    }

    /// <summary>
    /// Finds the nearest non-empty adjacent band for merging.
    /// When two equidistant candidates exist, picks the one with more entrants.
    /// </summary>
    private static ArenaScoreBand? FindNearestAdjacentBand(
        Dictionary<ArenaScoreBand, List<ArenaTournamentEntry>> pools,
        ArenaScoreBand[] ordered,
        int sourceIndex)
    {
        ArenaScoreBand? best = null;
        int bestDist = int.MaxValue;
        int bestCount = -1;

        for (int j = 0; j < ordered.Length; j++)
        {
            if (j == sourceIndex) continue;
            var candidate = ordered[j];
            int count = pools[candidate].Count;
            if (count == 0) continue;

            int dist = Mathf.Abs(j - sourceIndex);
            if (dist < bestDist || (dist == bestDist && count > bestCount))
            {
                best = candidate;
                bestDist = dist;
                bestCount = count;
            }
        }

        return best;
    }

    // ═════════════════════════════════════════════════════════════
    //  Step 4 — Create brackets from a pool
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Shuffles the pool, splits into 32-player bracket chunks, and backfills
    /// each bracket with bots. Returns one <see cref="ArenaTournamentRecord"/> per bracket.
    /// </summary>
    public static List<ArenaTournamentRecord> CreateBracketsFromPool(
        List<ArenaTournamentEntry> pool,
        ArenaScoreBand band,
        long weekStartUtc,
        List<ArenaBotTeamTemplate> botTemplates,
        System.Random rng)
    {
        // Shuffle pool.
        Shuffle(pool, rng);

        int bracketSize = ArenaConstants.BracketSize;
        int bracketCount = Mathf.Max(1, Mathf.CeilToInt((float)pool.Count / bracketSize));

        var records = new List<ArenaTournamentRecord>(bracketCount);

        for (int b = 0; b < bracketCount; b++)
        {
            int start = b * bracketSize;
            int take = Mathf.Min(bracketSize, pool.Count - start);
            var bracketEntries = new List<ArenaTournamentEntry>(bracketSize);

            for (int i = start; i < start + take; i++)
                bracketEntries.Add(pool[i]);

            // Compute week end (Sunday 11:59:59 PM ET → next Monday 00:00 ET - 1 sec)
            long weekEndUtc = weekStartUtc + 7 * 86400L - 1;

            string tournamentId = GenerateTournamentId(weekStartUtc, band, b);

            // Backfill with bots.
            int botsNeeded = bracketSize - bracketEntries.Count;
            if (botsNeeded > 0)
            {
                var bots = GenerateBotEntries(botsNeeded, band, tournamentId, botTemplates, rng);
                bracketEntries.AddRange(bots);
            }

            // Assign seed order and tournament id to all entries.
            for (int i = 0; i < bracketEntries.Count; i++)
            {
                bracketEntries[i].seedOrder = i + 1;
                bracketEntries[i].tournamentId = tournamentId;
            }

            // Build the record.
            int realCount = 0;
            int botCount = 0;
            for (int i = 0; i < bracketEntries.Count; i++)
            {
                if (bracketEntries[i].isBot) botCount++;
                else realCount++;
            }

            var record = new ArenaTournamentRecord
            {
                tournamentId = tournamentId,
                weekStartUtc = weekStartUtc,
                weekEndUtc = weekEndUtc,
                state = ArenaTournamentState.Locked,
                bracketSize = bracketSize,
                scoreBand = band,
                entries = bracketEntries,
                matches = GenerateRound1Matches(bracketEntries, tournamentId, rng),
                standings = new ArenaTournamentStandings
                {
                    realPlayerCount = realCount,
                    botCount = botCount
                }
            };

            records.Add(record);
        }

        return records;
    }

    // ═════════════════════════════════════════════════════════════
    //  Round 1 match scaffold
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates round-of-32 match stubs pairing entry[0] vs entry[1], entry[2] vs entry[3], etc.
    /// Winners, logs, and turn counts are left empty — match resolution is a separate phase.
    /// </summary>
    private static List<ArenaTournamentMatch> GenerateRound1Matches(
        List<ArenaTournamentEntry> entries,
        string tournamentId,
        System.Random rng)
    {
        int matchCount = entries.Count / 2;
        var matches = new List<ArenaTournamentMatch>(matchCount);

        for (int i = 0; i < matchCount; i++)
        {
            var left = entries[i * 2];
            var right = entries[i * 2 + 1];

            matches.Add(new ArenaTournamentMatch
            {
                matchId = $"{tournamentId}_R0_M{i}",
                tournamentId = tournamentId,
                roundIndex = 0,
                leftEntryId = left.entryId,
                rightEntryId = right.entryId,
                matchSeed = rng.Next()
            });
        }

        return matches;
    }

    // ═════════════════════════════════════════════════════════════
    //  Bot generation (delegated to ArenaBotGenerator)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates <paramref name="count"/> bot entries for the given score band.
    /// Delegates to <see cref="ArenaBotGenerator"/> which merges curated library
    /// templates with any external overrides, applies variation, and creates
    /// frozen snapshots.
    /// </summary>
    public static List<ArenaTournamentEntry> GenerateBotEntries(
        int count,
        ArenaScoreBand band,
        string tournamentId,
        List<ArenaBotTeamTemplate> templates,
        System.Random rng)
    {
        return ArenaBotGenerator.GenerateBotEntries(count, band, tournamentId, templates, rng);
    }

    // ═════════════════════════════════════════════════════════════
    //  Snapshot creation (player)
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Freezes the player's current arena battle team into an <see cref="ArenaTeamSnapshot"/> +
    /// per-slot <see cref="ArenaBitlingSnapshot"/>s.
    /// The snapshot captures species, title, arena scores, type, and visibility at this instant.
    /// </summary>
    public static ArenaTeamSnapshot CreateTournamentEntrySnapshot(
        string arenaPlayerId,
        string displayName)
    {
        var members = ArenaLoadoutManager.GetArenaTeamMembers();
        var uids = ArenaLoadoutManager.GetArenaTeamOwnedUids();
        var vis = ArenaLoadoutManager.GetVisibilityMode();
        long nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var slots = new List<ArenaBitlingSnapshot>();
        var types = new List<MonsterType>();

        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            if (m == null || string.IsNullOrEmpty(m.monsterId)) continue;

            var def = MonsterLibraryLocator.GetById(m.monsterId);
            if (def == null) continue;

            TitleSO titleDef = null;
            string titleId = "";
            string titleName = "";
            int titleScore = 0;

            if (TitleManager.I != null)
            {
                titleId = TitleManager.I.GetEquippedTitleId(m.monsterId);
                if (!string.IsNullOrEmpty(titleId))
                {
                    titleDef = TitleManager.I.GetTitleById(titleId);
                    if (titleDef != null)
                    {
                        titleName = titleDef.DisplayOrId;
                        titleScore = Mathf.Max(0, titleDef.arenaScore);
                    }
                }
            }

            int monsterScore = Mathf.Max(0, def.arenaScore);
            ReadOwnedStatusSnapshot(m, out var statusType, out var statusTurns, out var statusMagnitude, out var statusPersistent);

            slots.Add(new ArenaBitlingSnapshot
            {
                instanceId = i < uids.Count ? uids[i] : "",
                monsterId = def.id,
                monsterName = def.displayName ?? def.id,
                monsterType = def.type,
                titleId = titleId,
                titleName = titleName,
                statusType = statusType,
                statusTurns = statusTurns,
                statusMagnitude = statusMagnitude,
                statusPersistent = statusPersistent,
                monsterArenaScore = monsterScore,
                titleArenaScore = titleScore,
                finalArenaContributionScore = monsterScore + titleScore
            });

            types.Add(def.type);
        }

        int baseSum = 0;
        for (int i = 0; i < slots.Count; i++)
            baseSum += slots[i].finalArenaContributionScore;

        int synergy = ArenaScoreCalculator.CalculateTypeSynergyBonus(types);

        var snapshot = new ArenaTeamSnapshot
        {
            snapshotId = Guid.NewGuid().ToString("N"),
            ownerPlayerId = arenaPlayerId ?? "",
            ownerDisplayName = displayName ?? "",
            isBot = false,
            visibilityMode = vis,
            arenaScore = baseSum + synergy,
            createdUtc = nowUtc,
            slotSnapshots = slots
        };

        return snapshot;
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers — ID generation
    // ═════════════════════════════════════════════════════════════

    private static string GenerateTournamentId(long weekStartUtc, ArenaScoreBand band, int bracketIndex)
    {
        return $"T{weekStartUtc}_{band}_{bracketIndex}";
    }

    private static void ReadOwnedStatusSnapshot(
        OwnedMonsterData owned,
        out StatusType statusType,
        out int statusTurns,
        out float statusMagnitude,
        out bool statusPersistent)
    {
        statusType = ReadOwnedField(owned, "statusType", StatusType.None);
        statusTurns = Mathf.Max(0, ReadOwnedField(owned, "statusTurns", 0));
        statusMagnitude = ReadOwnedField(owned, "statusMagnitude", 0f);
        statusPersistent = ReadOwnedField(owned, "statusPersistent", false);

        if (statusType == StatusType.None)
        {
            statusTurns = 0;
            statusMagnitude = 0f;
            statusPersistent = false;
        }
    }

    private static T ReadOwnedField<T>(OwnedMonsterData owned, string fieldName, T fallback)
    {
        if (owned == null || string.IsNullOrEmpty(fieldName))
            return fallback;

        var field = typeof(OwnedMonsterData).GetField(fieldName);
        if (field == null)
            return fallback;

        object value = field.GetValue(owned);
        if (value == null)
            return fallback;

        if (value is T typed)
            return typed;

        try
        {
            if (typeof(T).IsEnum)
            {
                if (value is string enumString)
                    return (T)Enum.Parse(typeof(T), enumString, ignoreCase: true);

                var asInt = Convert.ToInt32(value);
                return (T)Enum.ToObject(typeof(T), asInt);
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return fallback;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers — Fisher-Yates shuffle
    // ═════════════════════════════════════════════════════════════

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
