// Assets/Scripts/Arena/ArenaEnums.cs
// BRN Arena v1 — Foundation enums used across all arena systems.

/// <summary>
/// Controls how much of a player's battle team is visible to opponents.
/// FullReveal shows all Bitlings and titles; LimitedReveal hides details until the match resolves.
/// </summary>
public enum ArenaVisibilityMode
{
    FullReveal = 0,
    LimitedReveal = 1
}

/// <summary>
/// Tracks an individual player's progression through a single weekly tournament.
/// </summary>
public enum ArenaPlayerTournamentStatus
{
    /// <summary>Player has not entered this week's tournament.</summary>
    NotEntered = 0,
    /// <summary>Registered but tournament battles have not started yet.</summary>
    Entered = 1,
    /// <summary>Tournament is in progress and this player has not been eliminated.</summary>
    Active = 2,
    /// <summary>Player lost their single-elimination match and is out.</summary>
    Eliminated = 3,
    /// <summary>Player survived the bracket — placed in the tournament.</summary>
    Completed = 4
}

/// <summary>
/// Lifecycle state of an entire weekly tournament instance.
/// </summary>
public enum ArenaTournamentState
{
    /// <summary>Monday registration window is open.</summary>
    Registering = 0,
    /// <summary>Registration closed, bracket seeded, waiting for first battle day.</summary>
    Locked = 1,
    /// <summary>Battles are being resolved (Tue–Sat).</summary>
    Active = 2,
    /// <summary>All rounds complete, final standings published.</summary>
    Completed = 3
}

/// <summary>
/// Archetype templates used when generating bot teams to fill empty bracket slots.
/// Influences stat distribution, move selection, and synergy emphasis.
/// </summary>
public enum ArenaBotArchetype
{
    Balanced = 0,
    Aggressive = 1,
    Defensive = 2,
    TypeSynergy = 3,
    Wildcard = 4
}

/// <summary>
/// Discrete event types stored in a serialized arena battle log.
/// Used to replay or summarize match outcomes after async resolution.
/// </summary>
public enum ArenaBattleLogEventType
{
    TurnStart = 0,
    ActionUsed = 1,
    TitleTriggered = 2,
    Damage = 3,
    Heal = 4,
    StatusApplied = 5,
    StatusRemoved = 6,
    Knockout = 7,
    Victory = 8
}

/// <summary>
/// Hidden score bands used for matchmaking.
/// Players never see their band directly — it controls bracket seeding fairness.
/// </summary>
public enum ArenaScoreBand
{
    Low = 0,
    Standard = 1,
    High = 2,
    Elite = 3
}
