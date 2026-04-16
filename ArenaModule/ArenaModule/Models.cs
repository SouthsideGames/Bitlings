// ArenaModule — Data models for handler parameters, results, and Cloud Save data.
// These match the JSON shapes used by the JS scripts.

using System.Text.Json.Serialization;

namespace ArenaModule;

// ═══════════════════════════════════════════════════════════════
//  REGISTRATION
// ═══════════════════════════════════════════════════════════════

public class RegistrationData
{
    [JsonPropertyName("playerId")]
    public string PlayerId { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("teamSnapshotJson")]
    public string TeamSnapshotJson { get; set; } = "";

    [JsonPropertyName("arenaScore")]
    public double ArenaScore { get; set; }

    [JsonPropertyName("scoreBand")]
    public int ScoreBand { get; set; }

    [JsonPropertyName("registeredUtc")]
    public long RegisteredUtc { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  BRACKET / TOURNAMENT STRUCTURES
// ═══════════════════════════════════════════════════════════════

public class BracketEntry
{
    [JsonPropertyName("entryId")]
    public string EntryId { get; set; } = "";

    [JsonPropertyName("playerId")]
    public string PlayerId { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("teamSnapshotJson")]
    public string TeamSnapshotJson { get; set; } = "";

    [JsonPropertyName("arenaScore")]
    public double ArenaScore { get; set; }

    [JsonPropertyName("isBot")]
    public bool IsBot { get; set; }
}

public class BracketData
{
    [JsonPropertyName("tournamentId")]
    public string TournamentId { get; set; } = "";

    [JsonPropertyName("weekStartUtc")]
    public long WeekStartUtc { get; set; }

    [JsonPropertyName("weekEndUtc")]
    public long WeekEndUtc { get; set; }

    [JsonPropertyName("scoreBand")]
    public int ScoreBand { get; set; }

    [JsonPropertyName("bracketSeed")]
    public int BracketSeed { get; set; }

    [JsonPropertyName("realEntries")]
    public List<BracketEntry> RealEntries { get; set; } = new();

    [JsonPropertyName("realPlayerCount")]
    public int RealPlayerCount { get; set; }

    [JsonPropertyName("botsNeeded")]
    public int BotsNeeded { get; set; }
}

public class PlayerMapping
{
    [JsonPropertyName("tournamentId")]
    public string TournamentId { get; set; } = "";

    [JsonPropertyName("entryId")]
    public string EntryId { get; set; } = "";

    [JsonPropertyName("scoreBand")]
    public int ScoreBand { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  ARENA SAVE DATA (subset relevant to server)
// ═══════════════════════════════════════════════════════════════

public class ArenaSaveDataServer
{
    [JsonPropertyName("arenaUsername")]
    public string? ArenaUsername { get; set; }

    [JsonPropertyName("usernameCreated")]
    public bool UsernameCreated { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  TEAM SNAPSHOT (for validation)
// ═══════════════════════════════════════════════════════════════

public class TeamSnapshotServer
{
    [JsonPropertyName("snapshotId")]
    public string SnapshotId { get; set; } = "";

    [JsonPropertyName("ownerPlayerId")]
    public string OwnerPlayerId { get; set; } = "";

    [JsonPropertyName("ownerDisplayName")]
    public string OwnerDisplayName { get; set; } = "";

    [JsonPropertyName("isBot")]
    public bool IsBot { get; set; }

    [JsonPropertyName("arenaScore")]
    public int ArenaScore { get; set; }

    [JsonPropertyName("createdUtc")]
    public long CreatedUtc { get; set; }

    [JsonPropertyName("slotSnapshots")]
    public List<SlotSnapshotServer>? SlotSnapshots { get; set; }
}

public class SlotSnapshotServer
{
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; set; } = "";

    [JsonPropertyName("monsterId")]
    public string MonsterId { get; set; } = "";

    [JsonPropertyName("monsterType")]
    public int MonsterType { get; set; }

    [JsonPropertyName("titleId")]
    public string TitleId { get; set; } = "";

    [JsonPropertyName("monsterArenaScore")]
    public int MonsterArenaScore { get; set; }

    [JsonPropertyName("titleArenaScore")]
    public int TitleArenaScore { get; set; }

    [JsonPropertyName("finalArenaContributionScore")]
    public int FinalArenaContributionScore { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  CATALOG DATA (for server-side score validation)
// ═══════════════════════════════════════════════════════════════

public class CatalogMonster
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("arenaScore")]
    public int ArenaScore { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }
}

public class CatalogTitle
{
    [JsonPropertyName("titleId")]
    public string TitleId { get; set; } = "";

    [JsonPropertyName("arenaScore")]
    public int ArenaScore { get; set; }
}

public class MonsterCatalogServer
{
    [JsonPropertyName("monsters")]
    public List<CatalogMonster> Monsters { get; set; } = new();
}

public class TitleCatalogServer
{
    [JsonPropertyName("titles")]
    public List<CatalogTitle> Titles { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════
//  RESULT TYPES
// ═══════════════════════════════════════════════════════════════

public class UsernameResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class RegisterResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("weekId")]
    public string? WeekId { get; set; }
}

public class LockResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("bracketCount")]
    public int BracketCount { get; set; }

    [JsonPropertyName("playerCount")]
    public int PlayerCount { get; set; }

    [JsonPropertyName("alreadyLocked")]
    public bool AlreadyLocked { get; set; }
}

public class GetBracketResult
{
    [JsonPropertyName("assigned")]
    public bool Assigned { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("entryId")]
    public string? EntryId { get; set; }

    [JsonPropertyName("bracket")]
    public BracketData? Bracket { get; set; }
}
