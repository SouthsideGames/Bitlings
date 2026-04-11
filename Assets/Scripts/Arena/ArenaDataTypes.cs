// Assets/Scripts/Arena/ArenaDataTypes.cs
// BRN Arena v1 — Plain serializable data classes.
// These are data-only containers; behaviour lives in future manager scripts.

using System;
using System.Collections.Generic;

// ═══════════════════════════════════════════════════════════════════
//  SAVE DATA — Top-level section persisted inside SaveData
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Root arena section stored in the player's save file.
/// Mirrors the pattern used by IdleSaveSection, ExchangeSystemSaveSection, etc.
/// </summary>
[Serializable]
public sealed class ArenaSaveData
{
    /// <summary>Whether the arena feature has been unlocked for this player.</summary>
    public bool arenaUnlocked;

    /// <summary>Whether the one-time unlock reward ticket has been claimed.</summary>
    public bool unlockRewardClaimed;

    /// <summary>Whether the first-open intro tutorial has been completed.</summary>
    public bool introCompleted;

    /// <summary>Whether the username creation step of onboarding has been completed.</summary>
    public bool usernameCreated;

    /// <summary>Persistent player id used for arena matchmaking (may differ from local playerId).</summary>
    public string arenaPlayerId;

    /// <summary>Display name shown to opponents in brackets.</summary>
    public string arenaUsername;

    /// <summary>Current ticket balance (0–MaxTickets). Tickets are spent to enter a tournament.</summary>
    public int arenaTickets;

    /// <summary>Number of extra tickets purchased this week (reset each Monday).</summary>
    public int weeklyTicketsPurchased;

    /// <summary>UTC epoch of the last weekly-ticket reset, used to detect week rollover.</summary>
    public long lastTicketResetUtc;

    /// <summary>The player's current battle-team configuration.</summary>
    public ArenaBattleTeamData battleTeamData = new ArenaBattleTeamData();

    /// <summary>Cumulative career statistics.</summary>
    public ArenaLifetimeStats lifetimeStats = new ArenaLifetimeStats();

    /// <summary>Cached state of the tournament the player is currently participating in (null/empty when idle).</summary>
    public ArenaCurrentTournamentCache currentTournamentCache = new ArenaCurrentTournamentCache();

    /// <summary>
    /// Rolling window of recent tournament results (newest first).
    /// Oldest entries are pruned when count exceeds <see cref="ArenaConstants.TournamentHistoryRetention"/>.
    /// </summary>
    public List<ArenaTournamentHistoryEntry> recentTournamentHistory = new List<ArenaTournamentHistoryEntry>();
}

// ═══════════════════════════════════════════════════════════════════
//  BATTLE TEAM
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// The 3-Bitling team a player registers for arena battles.
/// Managed through the Directory panel. Locks on tournament entry.
/// </summary>
[Serializable]
public sealed class ArenaBattleTeamData
{
    /// <summary>OwnedMonsterData.ownedUID for slot 1 (empty string = unset).</summary>
    public string slot1OwnedBitlingId = "";

    /// <summary>OwnedMonsterData.ownedUID for slot 2.</summary>
    public string slot2OwnedBitlingId = "";

    /// <summary>OwnedMonsterData.ownedUID for slot 3.</summary>
    public string slot3OwnedBitlingId = "";

    /// <summary>How much of this team opponents can preview before the match.</summary>
    public ArenaVisibilityMode visibilityMode = ArenaVisibilityMode.FullReveal;

    /// <summary>True while the player is in an active tournament — team slots cannot be swapped.</summary>
    public bool isLocked;

    /// <summary>The tournament id that caused the lock (cleared on elimination or completion).</summary>
    public string lockedTournamentId = "";
}

// ═══════════════════════════════════════════════════════════════════
//  LIFETIME STATS
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Cumulative career statistics tracked for achievement/progression purposes.
/// </summary>
[Serializable]
public sealed class ArenaLifetimeStats
{
    public int tournamentsEntered;
    public int championshipsWon;

    /// <summary>Top-3 finishes (1st, 2nd, or 3rd place).</summary>
    public int podiumFinishes;

    /// <summary>Best placement across all tournaments ever (1 = champion). 0 = never placed.</summary>
    public int bestPlacementAllTime;

    /// <summary>Best placement achieved this calendar month.</summary>
    public int highestRankThisMonth;

    /// <summary>Sum of all final placements (used for average calculation).</summary>
    public int totalPlacementSum;

    /// <summary>Number of tournaments entered this calendar month (reset monthly).</summary>
    public int currentMonthTournamentsEntered;
}

// ═══════════════════════════════════════════════════════════════════
//  CURRENT TOURNAMENT CACHE
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Lightweight client-side cache of the player's active tournament progress.
/// Refreshed from server/sim results each day at publish time.
/// </summary>
[Serializable]
public sealed class ArenaCurrentTournamentCache
{
    public string tournamentId = "";

    /// <summary>UTC epoch of the Monday that started this tournament week.</summary>
    public long weekStartUtc;

    /// <summary>UTC epoch of the following Sunday midnight (end of review day).</summary>
    public long weekEndUtc;

    /// <summary>This player's entry id within the bracket.</summary>
    public string playerEntryId = "";

    public ArenaPlayerTournamentStatus playerStatus = ArenaPlayerTournamentStatus.NotEntered;

    /// <summary>Zero-based index of the current or most-recently-resolved round.</summary>
    public int currentRoundIndex;

    /// <summary>Match id of the player's last resolved match (for quick lookup).</summary>
    public string lastMatchId = "";

    /// <summary>Final bracket placement (1 = champion). 0 while still active.</summary>
    public int finalPlacement;

    /// <summary>UTC epoch of the last time results were pulled / refreshed.</summary>
    public long resultsLastUpdatedUtc;
}

// ═══════════════════════════════════════════════════════════════════
//  TOURNAMENT HISTORY (per-player summary kept in save)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Compact summary of a completed tournament stored in save data.
/// Full bracket/match detail lives in ArenaTournamentRecord (server-side or cached file).
/// </summary>
[Serializable]
public sealed class ArenaTournamentHistoryEntry
{
    public string tournamentId = "";
    public long weekStartUtc;
    public int finalPlacement;
    public int totalEntrants;
    public ArenaScoreBand scoreBand;

    /// <summary>Snapshot of the team the player used.</summary>
    public ArenaTeamSnapshot teamSnapshot;

    /// <summary>Rewards received for this tournament.</summary>
    public ArenaRewardResult rewardResult;
}

// ═══════════════════════════════════════════════════════════════════
//  MATCH HISTORY (per-player, per-match summary)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// A single match result from the player's perspective.
/// Stored temporarily for UI review; not part of long-term save.
/// </summary>
[Serializable]
public sealed class ArenaMatchHistoryEntry
{
    public string matchId = "";
    public string tournamentId = "";
    public int roundIndex;

    public string opponentDisplayName = "";
    public bool opponentIsBot;
    public int opponentArenaScore;

    public bool playerWon;
    public int turnCount;
    public long processedUtc;

    /// <summary>Team snapshot for the player side (optional, for detail display).</summary>
    public ArenaTeamSnapshot playerSnapshot;

    /// <summary>Team snapshot for the opponent side (optional, for detail display).</summary>
    public ArenaTeamSnapshot opponentSnapshot;

    /// <summary>Serialized battle log events for replay/summary.</summary>
    public List<ArenaBattleLogEvent> battleLog = new List<ArenaBattleLogEvent>();
}

// ═══════════════════════════════════════════════════════════════════
//  BATTLE LOG EVENT
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// A single discrete event inside an arena battle log.
/// Designed for serialisation and post-match replay, not real-time rendering.
/// </summary>
[Serializable]
public sealed class ArenaBattleLogEvent
{
    public ArenaBattleLogEventType eventType;

    /// <summary>Zero-based turn when this event occurred.</summary>
    public int turn;

    /// <summary>Which side initiated or was affected (left = 0, right = 1).</summary>
    public int side;

    /// <summary>Human-readable summary line (e.g. "Emberpaw used Tackle for 24 damage").</summary>
    public string description = "";

    /// <summary>Numeric payload (damage amount, heal amount, status id, etc.).</summary>
    public int value;

    /// <summary>Optional secondary key (e.g. monsterId, titleId, statusType ordinal).</summary>
    public string referenceId = "";
}

// ═══════════════════════════════════════════════════════════════════
//  TEAM / BITLING SNAPSHOTS
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Frozen snapshot of a 3-Bitling team at the moment of tournament entry.
/// Used to display opponent info and resolve async battles deterministically.
/// </summary>
[Serializable]
public sealed class ArenaTeamSnapshot
{
    public string snapshotId = "";

    /// <summary>Arena player id of the team owner.</summary>
    public string ownerPlayerId = "";

    public string ownerDisplayName = "";

    /// <summary>True if this team belongs to a generated bot, not a real player.</summary>
    public bool isBot;

    public ArenaVisibilityMode visibilityMode = ArenaVisibilityMode.FullReveal;

    /// <summary>Aggregate arena score of the full team (sum of all slot contributions).</summary>
    public int arenaScore;

    /// <summary>UTC epoch when this snapshot was created.</summary>
    public long createdUtc;

    /// <summary>Per-slot Bitling snapshots (always length 3).</summary>
    public List<ArenaBitlingSnapshot> slotSnapshots = new List<ArenaBitlingSnapshot>();
}

/// <summary>
/// Frozen snapshot of a single Bitling (real or bot) at tournament entry time.
/// Contains everything needed to resolve an async battle without the live OwnedMonsterData.
/// </summary>
[Serializable]
public sealed class ArenaBitlingSnapshot
{
    // ── Identity ─────────────────────────────────────────────
    /// <summary>OwnedMonsterData.ownedUID for real players; generated id for bots.</summary>
    public string instanceId = "";

    /// <summary>MonsterDataSO.id — the species.</summary>
    public string monsterId = "";

    public string monsterName = "";
    public MonsterType monsterType;

    /// <summary>TitleSO.titleId of the equipped title (empty = no title).</summary>
    public string titleId = "";

    public string titleName = "";

    // ── Scoring ──────────────────────────────────────────────
    /// <summary>Arena score contributed by the species definition.</summary>
    public int monsterArenaScore;

    /// <summary>Arena score contributed by the equipped title.</summary>
    public int titleArenaScore;

    /// <summary>Final combined contribution including synergy bonuses.</summary>
    public int finalArenaContributionScore;

    // ── Visibility ───────────────────────────────────────────
    /// <summary>
    /// Info visible to all opponents regardless of visibility mode
    /// (e.g. monster type silhouette, rarity tier).
    /// </summary>
    public string publicInfo = "";

    /// <summary>
    /// Info hidden under LimitedReveal mode, revealed after match resolves
    /// (e.g. exact title, stat breakdown).
    /// </summary>
    public string privateInfo = "";
}

// ═══════════════════════════════════════════════════════════════════
//  FULL TOURNAMENT RECORD (server / cache — not in main save)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Complete bracket data for one weekly tournament.
/// Stored server-side or in a separate local cache file — too large for the main save blob.
/// </summary>
[Serializable]
public sealed class ArenaTournamentRecord
{
    public string tournamentId = "";
    public long weekStartUtc;
    public long weekEndUtc;

    public ArenaTournamentState state = ArenaTournamentState.Registering;

    /// <summary>Bracket size (always 32 for v1).</summary>
    public int bracketSize = ArenaConstants.BracketSize;

    /// <summary>Score band this bracket was seeded into.</summary>
    public ArenaScoreBand scoreBand;

    public List<ArenaTournamentEntry> entries = new List<ArenaTournamentEntry>();
    public List<ArenaTournamentMatch> matches = new List<ArenaTournamentMatch>();

    /// <summary>Final standings sorted by placement (index 0 = champion).</summary>
    public ArenaTournamentStandings standings;

    /// <summary>UTC epoch when final results were published.</summary>
    public long resultsPublishedUtc;
}

/// <summary>
/// One player's (or bot's) entry in a tournament bracket.
/// </summary>
[Serializable]
public sealed class ArenaTournamentEntry
{
    public string entryId = "";
    public string tournamentId = "";
    public string playerId = "";
    public string displayNameSnapshot = "";

    public bool isBot;

    /// <summary>Seed position in the bracket (1-based). Lower = higher seed.</summary>
    public int seedOrder;

    /// <summary>Combined arena score at time of entry.</summary>
    public int arenaScore;

    /// <summary>Frozen team used for all matches in this tournament.</summary>
    public ArenaTeamSnapshot teamSnapshot;

    /// <summary>Round index at which this entry was eliminated (-1 = still active or champion).</summary>
    public int eliminatedRoundIndex = -1;

    /// <summary>Final placement in the bracket (1 = champion).</summary>
    public int finalPlacement;

    /// <summary>Rewards granted for this placement.</summary>
    public ArenaRewardResult rewardResult;
}

/// <summary>
/// A single match within a tournament round.
/// </summary>
[Serializable]
public sealed class ArenaTournamentMatch
{
    public string matchId = "";
    public string tournamentId = "";

    /// <summary>Zero-based round index (0 = round of 32, 4 = finals).</summary>
    public int roundIndex;

    public string leftEntryId = "";
    public string rightEntryId = "";
    public string winnerEntryId = "";
    public string loserEntryId = "";

    /// <summary>Deterministic seed used to replay the battle simulation.</summary>
    public int matchSeed;

    /// <summary>Total turns the battle lasted.</summary>
    public int turnCount;

    /// <summary>Serialized event log of the battle.</summary>
    public List<ArenaBattleLogEvent> battleLog = new List<ArenaBattleLogEvent>();

    /// <summary>UTC epoch when this match was processed by the sim.</summary>
    public long processedUtc;
}

/// <summary>
/// Final standings for a completed tournament.
/// </summary>
[Serializable]
public sealed class ArenaTournamentStandings
{
    /// <summary>Entry ids ordered by placement (index 0 = 1st place).</summary>
    public List<string> placementOrder = new List<string>();

    /// <summary>Total number of real (non-bot) participants.</summary>
    public int realPlayerCount;

    /// <summary>Total number of bot participants.</summary>
    public int botCount;
}

// ═══════════════════════════════════════════════════════════════════
//  REWARDS
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Reward package granted to a player for their tournament placement.
/// </summary>
[Serializable]
public sealed class ArenaRewardResult
{
    /// <summary>Bracket placement that earned this reward (1 = champion).</summary>
    public int placement;

    public int creditsAwarded;

    /// <summary>Primary resource type awarded (Credits, GrowthCore, etc.).</summary>
    public ResourceType featuredResourceType;
    public int featuredResourceAmount;

    /// <summary>Additional random resource drops (type → amount pairs).</summary>
    public List<ArenaResourceRewardEntry> randomResourceRewards = new List<ArenaResourceRewardEntry>();

    public int packVoucherAmount;
    public int arenaTicketAmount;

    /// <summary>True once the reward has been collected / applied to save data.</summary>
    public bool wasGranted;
}

/// <summary>
/// A single resource type + amount pair inside a reward result.
/// </summary>
[Serializable]
public sealed class ArenaResourceRewardEntry
{
    public ResourceType resourceType;
    public int amount;
}

// ═══════════════════════════════════════════════════════════════════
//  BOT GENERATION
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Designer-authored template describing how to generate a bot team for a given archetype and score band.
/// Stored as ScriptableObject data or JSON config; runtime bot generation reads these.
/// </summary>
[Serializable]
public sealed class ArenaBotTeamTemplate
{
    public string templateId = "";

    /// <summary>Pool of display names randomly assigned to bots using this template.</summary>
    public List<string> displayNamePool = new List<string>();

    public ArenaBotArchetype archetype;
    public ArenaScoreBand scoreBand;

    // Per-slot species options (MonsterDataSO.id values)
    public List<string> slot1MonsterOptions = new List<string>();
    public List<string> slot2MonsterOptions = new List<string>();
    public List<string> slot3MonsterOptions = new List<string>();

    // Per-slot title options (TitleSO.titleId values)
    public List<string> slot1TitleOptions = new List<string>();
    public List<string> slot2TitleOptions = new List<string>();
    public List<string> slot3TitleOptions = new List<string>();

    /// <summary>Visibility modes the bot may randomly pick from.</summary>
    public List<ArenaVisibilityMode> visibilityModeOptions = new List<ArenaVisibilityMode>();

    /// <summary>Designer hint for how strong the type synergy on this template should be.</summary>
    public int expectedSynergyTier;

    /// <summary>If false, the generator must pick three distinct species.</summary>
    public bool allowDuplicateSpecies;
}

/// <summary>
/// Runtime info captured when a bot team is actually generated from a template, for debugging/logging.
/// </summary>
[Serializable]
public sealed class ArenaBotGenerationInfo
{
    public string templateId = "";
    public string generatedDisplayName = "";
    public ArenaBotArchetype archetype;
    public ArenaScoreBand scoreBand;

    /// <summary>The final team snapshot created for the bot.</summary>
    public ArenaTeamSnapshot generatedTeamSnapshot;

    /// <summary>Seed used during generation for reproducibility.</summary>
    public int generationSeed;

    /// <summary>UTC epoch of generation.</summary>
    public long generatedUtc;
}
