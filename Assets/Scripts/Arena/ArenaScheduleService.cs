// Assets/Scripts/Arena/ArenaScheduleService.cs
// BRN Arena v1 — Tournament week timing utility.
// All schedule logic runs in Eastern Time to match ArenaConstants.

using System;

/// <summary>
/// Static utility that answers schedule questions for the weekly tournament cycle:
///   Mon 00:00 ET      — Registration opens
///   Tue 23:59 ET      — Registration closes
///   Wed 00:00 ET      — Brackets locked / assigned
///   Wed 17:00 ET      — Round 1 results available
///   Thu 17:00 ET      — Round 2
///   Fri 17:00 ET      — Round 3
///   Sat 17:00 ET      — Round 4
///   Sun 17:00 ET      — Round 5 (Finals)
/// </summary>
public static class ArenaScheduleService
{
    // ═════════════════════════════════════════════════════════════
    //  Week identity
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a stable identifier for the current tournament week.
    /// Format: "WYYYYMMDD" where the date is the Monday of this week in ET.
    /// </summary>
    public static string GetCurrentWeekId()
    {
        var monday = GetWeekMonday(GetCurrentET());
        return $"W{monday:yyyyMMdd}";
    }

    /// <summary>UTC epoch of the Monday 00:00 ET that starts the current tournament week.</summary>
    public static long GetCurrentWeekStartUtc()
    {
        var monday = GetWeekMonday(GetCurrentET());
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(monday, DateTimeKind.Unspecified),
            ArenaConstants.EasternTimeZone);
        return new DateTimeOffset(utc).ToUnixTimeSeconds();
    }

    /// <summary>UTC epoch of the following Sunday 23:59:59 ET (end of the tournament week).</summary>
    public static long GetCurrentWeekEndUtc()
    {
        return GetCurrentWeekStartUtc() + (7 * 24 * 60 * 60) - 1;
    }

    // ═════════════════════════════════════════════════════════════
    //  Registration window
    // ═════════════════════════════════════════════════════════════

    /// <summary>True if the current ET time falls within Mon 00:00 – Tue 23:59 ET.</summary>
    public static bool IsRegistrationOpen()
    {
        var et = GetCurrentET();
        var dow = et.DayOfWeek;

        if (dow == DayOfWeek.Monday) return true;

        if (dow == DayOfWeek.Tuesday)
        {
            return et.Hour < ArenaConstants.RegistrationCloseHourET
                || (et.Hour == ArenaConstants.RegistrationCloseHourET
                    && et.Minute <= ArenaConstants.RegistrationCloseMinuteET);
        }

        return false;
    }

    /// <summary>UTC epoch of the registration close time (Tue 23:59 ET) for the current week.</summary>
    public static long GetRegistrationCloseUtc()
    {
        var monday = GetWeekMonday(GetCurrentET());
        var close = monday.AddDays(1) // Tuesday
                         .AddHours(ArenaConstants.RegistrationCloseHourET)
                         .AddMinutes(ArenaConstants.RegistrationCloseMinuteET);
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(close, DateTimeKind.Unspecified),
            ArenaConstants.EasternTimeZone);
        return new DateTimeOffset(utc).ToUnixTimeSeconds();
    }

    // ═════════════════════════════════════════════════════════════
    //  Bracket lock
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// True if the current ET time is at or past the bracket lock point (Wed 00:00 ET).
    /// Once locked, no new registrations are accepted and brackets can be built.
    /// </summary>
    public static bool AreBracketsLocked()
    {
        var et = GetCurrentET();
        int monBased = MondayBasedDay(et.DayOfWeek);
        // Monday = 0, Tuesday = 1, Wednesday = 2, … Sunday = 6
        return monBased >= 2; // Wednesday or later
    }

    /// <summary>UTC epoch of the bracket lock time (Wed 00:00 ET) for the current week.</summary>
    public static long GetBracketLockUtc()
    {
        var monday = GetWeekMonday(GetCurrentET());
        var lockTime = monday.AddDays(2); // Wednesday 00:00
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(lockTime, DateTimeKind.Unspecified),
            ArenaConstants.EasternTimeZone);
        return new DateTimeOffset(utc).ToUnixTimeSeconds();
    }

    // ═════════════════════════════════════════════════════════════
    //  Round availability
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// True if the given round's results should be visible.
    /// Round 0 → Wed at BattleResolveHourET, Round 1 → Thu, … Round 4 → Sun.
    /// </summary>
    public static bool IsRoundAvailable(int roundIndex)
    {
        if (roundIndex < 0 || roundIndex >= ArenaConstants.TotalRounds) return false;

        var et = GetCurrentET();
        // Target day: Wednesday + roundIndex (Mon-based: 2 + roundIndex)
        int targetMonBased = 2 + roundIndex; // 2=Wed, 3=Thu, 4=Fri, 5=Sat, 6=Sun
        int currentMonBased = MondayBasedDay(et.DayOfWeek);

        if (currentMonBased > targetMonBased) return true;
        if (currentMonBased == targetMonBased)
            return et.Hour >= ArenaConstants.BattleResolveHourET;

        return false;
    }

    /// <summary>UTC epoch when round results become available for the current week.</summary>
    public static long GetRoundAvailableUtc(int roundIndex)
    {
        if (roundIndex < 0 || roundIndex >= ArenaConstants.TotalRounds) return long.MaxValue;

        var monday = GetWeekMonday(GetCurrentET());
        // Wednesday = +2 days, each subsequent round +1 day
        var roundDay = monday.AddDays(2 + roundIndex)
                             .AddHours(ArenaConstants.BattleResolveHourET);
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(roundDay, DateTimeKind.Unspecified),
            ArenaConstants.EasternTimeZone);
        return new DateTimeOffset(utc).ToUnixTimeSeconds();
    }

    /// <summary>
    /// Returns the number of rounds whose results are currently available (0–5).
    /// </summary>
    public static int GetAvailableRoundCount()
    {
        for (int i = ArenaConstants.TotalRounds - 1; i >= 0; i--)
        {
            if (IsRoundAvailable(i)) return i + 1;
        }
        return 0;
    }

    // ═════════════════════════════════════════════════════════════
    //  Internal helpers
    // ═════════════════════════════════════════════════════════════

    private static DateTime GetCurrentET()
    {
        return TimeZoneInfo.ConvertTime(DateTime.UtcNow, ArenaConstants.EasternTimeZone);
    }

    /// <summary>
    /// Returns Monday 00:00 of the week containing the given ET date.
    /// </summary>
    private static DateTime GetWeekMonday(DateTime et)
    {
        int daysFromMonday = MondayBasedDay(et.DayOfWeek);
        return et.Date.AddDays(-daysFromMonday);
    }

    /// <summary>
    /// Converts DayOfWeek to a Monday-based index: Mon=0, Tue=1, … Sun=6.
    /// </summary>
    private static int MondayBasedDay(DayOfWeek d)
    {
        return d == DayOfWeek.Sunday ? 6 : (int)d - 1;
    }
}
