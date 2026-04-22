// ArenaModule — Schedule / calendar helpers
// Mirrors the ET-based schedule logic from the JS Cloud Code scripts.

using System.Globalization;

namespace ArenaModule.Helpers;

public static class ScheduleHelper
{
    private static readonly Lazy<TimeZoneInfo> EasternTz = new Lazy<TimeZoneInfo>(ResolveEasternTimeZone);

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
        var et = TimeZoneInfo.ConvertTimeFromUtc(utcNow, EasternTz.Value);
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
        var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTz.Value);
        return et.DayOfWeek == DayOfWeek.Monday || et.DayOfWeek == DayOfWeek.Tuesday;
    }

    /// <summary>
    /// Returns true if the current ET day is Wednesday or later in the week
    /// (brackets should be locked by now).
    /// </summary>
    public static bool IsPastLockTime()
    {
        var et = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTz.Value);
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
        if (string.IsNullOrWhiteSpace(weekId) || weekId.Length != 9 || weekId[0] != 'W')
            throw new ArgumentException("Week ID must be in WyyyyMMdd format.", nameof(weekId));

        var mondayEt = DateTime.ParseExact(
            weekId[1..],
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None);

        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(mondayEt, DateTimeKind.Unspecified),
            EasternTz.Value);

        return new DateTimeOffset(utc).ToUnixTimeSeconds();
    }

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        string[] candidateIds = new[] { "Eastern Standard Time", "America/New_York", "US/Eastern" };

        foreach (var timeZoneId in candidateIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        // Runtime fallback for minimal Linux containers that do not include tzdata.
        return CreateUsEasternFallbackTimeZone();
    }

    private static TimeZoneInfo CreateUsEasternFallbackTimeZone()
    {
        var daylightDelta = TimeSpan.FromHours(1);

        // US DST: starts 2:00 AM on second Sunday in March.
        var dstStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            3,
            2,
            DayOfWeek.Sunday);

        // US DST: ends 2:00 AM on first Sunday in November.
        var dstEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            11,
            1,
            DayOfWeek.Sunday);

        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            daylightDelta,
            dstStart,
            dstEnd);

        return TimeZoneInfo.CreateCustomTimeZone(
            id: "Custom/Eastern",
            baseUtcOffset: TimeSpan.FromHours(-5),
            displayName: "(UTC-05:00) Eastern Time",
            standardDisplayName: "Eastern Standard Time",
            daylightDisplayName: "Eastern Daylight Time",
            adjustmentRules: new[] { rule },
            disableDaylightSavingTime: false);
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
