// Assets/Scripts/Arena/ArenaConstants.cs
// BRN Arena v1 — Centralised constants and tuning knobs for the arena system.

using System;

/// <summary>
/// Read-only constants and configurable defaults for the BRN Arena.
/// Gameplay-critical values live here so designers can tweak without hunting through code.
/// </summary>
public static class ArenaConstants
{
    // ── Bracket ──────────────────────────────────────────────
    /// <summary>Total slots in a single-elimination bracket. Bots fill any unfilled seats.</summary>
    public const int BracketSize = 32;

    /// <summary>Number of Bitlings on an arena battle team.</summary>
    public const int BattleTeamSize = 3;

    // ── Tickets ──────────────────────────────────────────────
    /// <summary>Maximum arena tickets a player can hold at once.</summary>
    public const int MaxTickets = 3;

    /// <summary>How many extra tickets a player may purchase per week.</summary>
    public const int WeeklyTicketPurchaseLimit = 1;

    /// <summary>Credit cost to purchase one arena ticket (placeholder — balance later).</summary>
    public const int TicketCreditCost = 500;

    // ── Unlock ───────────────────────────────────────────────
    /// <summary>Battle XP level required to unlock the arena feature.</summary>
    public const int ArenaUnlockLevel = 25;

    // ── Schedule (all times Eastern) ─────────────────────────
    /// <summary>Hour (ET 24h) when daily match results are published (Tue–Sat).</summary>
    public const int ResultsPublishHourET = 18; // 6:00 PM ET

    /// <summary>Hour (ET 24h) when daily battles are resolved server-side.</summary>
    public const int BattleResolveHourET = 17; // 5:00 PM ET

    /// <summary>Registration closes Monday at this hour (ET 24h). 23 = 11:00 PM.</summary>
    public const int RegistrationCloseHourET = 23;

    /// <summary>Registration closes Monday at this minute (ET). 59 = 11:59 PM.</summary>
    public const int RegistrationCloseMinuteET = 59;

    // ── History ──────────────────────────────────────────────
    /// <summary>Number of completed tournament records kept in save data before oldest is pruned.</summary>
    public const int TournamentHistoryRetention = 4;

    // ── Scoring ──────────────────────────────────────────────
    /// <summary>
    /// Minimum combined arena score for a team to be placed in Standard band.
    /// Teams below this land in Low. Exact thresholds may move to a ScriptableObject later.
    /// </summary>
    public const int ScoreBandStandardThreshold = 50;
    public const int ScoreBandHighThreshold = 100;
    public const int ScoreBandEliteThreshold = 175;

    // ── Online Schedule ─────────────────────────────────────
    /// <summary>Hour (ET 24h) when brackets lock on Wednesday. 0 = midnight.</summary>
    public const int BracketLockHourET = 0;

    /// <summary>Minimum real entrants per score band before merging into adjacent band.</summary>
    public const int MinRealEntrantsForBandMerge = 8;

    // ── Rounds ───────────────────────────────────────────────
    /// <summary>Total rounds in a 32-player single-elimination bracket (log2 32 = 5).</summary>
    public const int TotalRounds = 5;

    // ── Time ─────────────────────────────────────────────────
    /// <summary>Windows Eastern Time zone identifier.</summary>
    public const string EasternTimeZoneId = "Eastern Standard Time";

    /// <summary>IANA Eastern Time zone identifier used by Linux/macOS.</summary>
    public const string EasternTimeZoneIanaId = "America/New_York";

    private static readonly TimeZoneInfo _easternTimeZone = ResolveEasternTimeZone();

    /// <summary>
    /// Cached Eastern timezone lookup that supports Windows and IANA IDs.
    /// Useful for <see cref="TimeZoneInfo.FindSystemTimeZoneById"/>.
    /// </summary>
    public static TimeZoneInfo EasternTimeZone => _easternTimeZone;

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        // Try both Windows and IANA identifiers so this works on all target OSes.
        string[] candidateIds =
        {
            EasternTimeZoneId,
            EasternTimeZoneIanaId,
            "US/Eastern"
        };

        for (int i = 0; i < candidateIds.Length; i++)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidateIds[i]);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try next ID.
            }
            catch (InvalidTimeZoneException)
            {
                // Try next ID.
            }
        }

        // Last-resort fallback prevents hard crashes in runtime-critical flows.
        return TimeZoneInfo.Utc;
    }
}
