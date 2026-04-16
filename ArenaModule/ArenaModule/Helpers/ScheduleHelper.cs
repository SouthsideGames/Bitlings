// ArenaModule — Schedule / calendar helpers
// Mirrors the ET-based schedule logic from the JS Cloud Code scripts.

using System.Globalization;

namespace ArenaModule.Helpers;

public static class ScheduleHelper
{
    private static readonly TimeZoneInfo EasternTz =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    // ── Week ID ──────────────────────────────────────────────

    /// <summary>
    /// Returns the week ID for the current moment (e.g. "W20260413").
    /// The week starts on Monday 00:00 ET.
    /// </summary>
    public static string GetCurrentWeekId()
    {
        return GetWeekIdForUtc(DateTime.UtcNow);
    }

    public static string GetWeekIdForUtc(DateTime utcNow)
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(utcNow, EasternTz);
        int dow = (int)et.DayOfWeek; // 0=Sun
        int mondayOffset = dow == 0 ? -6 : 1 - dow;
        var monday = et.Date.AddDays(mondayOffset);
        return $"W{monday:yyyyMMdd}";
    }

    // ── Registration window ──────────────────────────────────

    /// <summary>
    /// Registration is open on Monday and Tuesday (ET).
    /// </summary>
    public static bool IsRegistrationOpen()
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTz);
        return et.DayOfWeek == DayOfWeek.Monday || et.DayOfWeek == DayOfWeek.Tuesday;
    }

    /// <summary>
    /// Returns true if the current ET day is Wednesday or later in the week
    /// (brackets should be locked by now).
    /// </summary>
    public static bool IsPastLockTime()
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTz);
        // Mon-based: Mon=0, Tue=1, Wed=2, Thu=3, Fri=4, Sat=5, Sun=6
        int monBased = et.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)et.DayOfWeek - 1;
        return monBased >= 2;
    }

    // ── Week ID → epoch ──────────────────────────────────────

    /// <summary>
    /// Converts a week ID (e.g. "W20260413") to a UTC epoch representing
    /// approximately Monday 00:00 ET (= 05:00 UTC for EST).
    /// </summary>
    public static long WeekIdToEpoch(string weekId)
    {
        var dateStr = weekId.AsSpan(1); // skip 'W'
        int y = int.Parse(dateStr.Slice(0, 4));
        int m = int.Parse(dateStr.Slice(4, 2));
        int d = int.Parse(dateStr.Slice(6, 2));
        var utc = new DateTime(y, m, d, 5, 0, 0, DateTimeKind.Utc); // 05:00 UTC ≈ 00:00 EST
        return new DateTimeOffset(utc).ToUnixTimeSeconds();
    }

    // ── Hashing ──────────────────────────────────────────────

    /// <summary>Java-style hashCode (matches the JS version).</summary>
    public static int HashCode(string s)
    {
        int hash = 0;
        foreach (char c in s)
            hash = unchecked((hash * 31) + c);
        return hash;
    }
}
