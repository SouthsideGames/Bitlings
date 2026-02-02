using System;
using UnityEngine;

public static class SeedService
{
    public static int ActiveSeed { get; private set; }

    public enum SeedMode { None, Session, Daily, Custom }
    public static SeedMode ActiveMode { get; private set; } = SeedMode.None;

    private static bool _seedApplied;

    public static void ApplyGlobalSeedForSession()
    {
        if (_seedApplied) return;

        if (SaveManager.Data == null)
            return;

        var fu = FeatureUnlockManager.I;
        var sm = SettingsManager.I;

        if (fu == null)
        {
            ApplySessionSeed();
            return;
        }

        bool customUnlocked = fu.IsUnlocked(FeatureId.Seeds_CustomInput);
        bool dailyUnlocked = fu.IsUnlocked(FeatureId.Seeds_DailyBasic);

        var settings = (sm != null) ? sm.S : null;
        if (customUnlocked && settings == null)
            return;

        if (customUnlocked &&
            settings != null &&
            settings.useCustomSeed &&
            !string.IsNullOrWhiteSpace(settings.customSeed))
        {
            string token = NormalizeSeedToken(settings.customSeed);
            int seed = BuildHashedSeed(token);

            ActiveSeed = seed;
            ActiveMode = SeedMode.Custom;

            UnityEngine.Random.InitState(seed);
            _seedApplied = true;
            return;
        }

        if (dailyUnlocked)
        {
            EnsureDailySeedForToday();
            var ss = SaveManager.Data.seedState ?? (SaveManager.Data.seedState = new SeedState());

            string token = NormalizeSeedToken(ss.dailySeed);
            int seed = BuildHashedSeed(token);

            ActiveSeed = seed;
            ActiveMode = SeedMode.Daily;

            UnityEngine.Random.InitState(seed);
            _seedApplied = true;
            return;
        }

        ApplySessionSeed();
    }

    private static void ApplySessionSeed()
    {
        int sessionSeed = CreateNewSessionSeedInt();

        ActiveSeed = sessionSeed;
        ActiveMode = SeedMode.Session;

        UnityEngine.Random.InitState(sessionSeed);
        _seedApplied = true;
    }

    public static string GetCurrentDailySeedString()
    {
        if (SaveManager.Data == null)
            return string.Empty;

        var ss = SaveManager.Data.seedState;
        if (ss == null)
            return string.Empty;

        int today = SaveManager.TodayDayIndexUTC();
        if (ss.dayIndex != today)
            return string.Empty;

        return ss.dailySeed ?? string.Empty;
    }

    public static string GetCurrentCustomSeedString()
    {
        var fu = FeatureUnlockManager.I;
        var sm = SettingsManager.I;
        if (fu == null || sm == null || sm.S == null)
            return string.Empty;

        if (!fu.IsUnlocked(FeatureId.Seeds_CustomInput))
            return string.Empty;

        if (!sm.S.useCustomSeed)
            return string.Empty;

        return sm.S.customSeed ?? string.Empty;
    }

    public static string GetDisplaySeedToken()
    {
        ApplyGlobalSeedForSession();

        if (ActiveMode == SeedMode.Custom)
            return NormalizeSeedToken(GetCurrentCustomSeedString());

        if (ActiveMode == SeedMode.Daily)
            return NormalizeSeedToken(GetCurrentDailySeedString());

        return ActiveSeed != 0 ? ActiveSeed.ToString() : string.Empty;
    }

    public static string GetDisplaySeedPrefix()
    {
        ApplyGlobalSeedForSession();

        if (ActiveMode == SeedMode.Custom) return "CUSTOM:";
        if (ActiveMode == SeedMode.Daily) return "DAILY:";
        return "SEED:";
    }

    public static bool TryRerollDailySeed(out string newSeed)
    {
        newSeed = null;

        if (SaveManager.Data == null)
            return false;

        var fu = FeatureUnlockManager.I;
        var sm = SettingsManager.I;
        if (fu == null)
            return false;

        if (!fu.IsUnlocked(FeatureId.Seeds_DailyBasic) ||
            !fu.IsUnlocked(FeatureId.Seeds_RerollDailyOnce))
        {
            return false;
        }

        var settings = (sm != null) ? sm.S : null;
        bool customActive = fu.IsUnlocked(FeatureId.Seeds_CustomInput) &&
                            settings != null &&
                            settings.useCustomSeed &&
                            !string.IsNullOrWhiteSpace(settings.customSeed);

        var ss = SaveManager.Data.seedState ?? (SaveManager.Data.seedState = new SeedState());
        int today = SaveManager.TodayDayIndexUTC();

        if (ss.lastRerollDayIndex == today)
            return false;

        ss.dayIndex = today;
        ss.dailySeed = GenerateNewSeedString();
        ss.lastRerollDayIndex = today;

        if (!SaveManager.IsHardWiping)
            SaveManager.Save();

        newSeed = ss.dailySeed;

        if (_seedApplied && !customActive)
        {
            string token = NormalizeSeedToken(ss.dailySeed);
            int seed = BuildHashedSeed(token);

            ActiveSeed = seed;
            ActiveMode = SeedMode.Daily;

            UnityEngine.Random.InitState(seed);
        }

        return true;
    }

    private static void EnsureDailySeedForToday()
    {
        var ss = SaveManager.Data.seedState ?? (SaveManager.Data.seedState = new SeedState());
        int today = SaveManager.TodayDayIndexUTC();

        if (ss.dayIndex != today || string.IsNullOrEmpty(ss.dailySeed))
        {
            ss.dayIndex = today;
            ss.dailySeed = GenerateNewSeedString();

            if (ss.lastRerollDayIndex <= 0) ss.lastRerollDayIndex = -1;

            if (!SaveManager.IsHardWiping)
                SaveManager.Save();
        }
    }

    private static string GenerateNewSeedString()
    {
        return Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
    }

    public static string NormalizeSeedToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string s = raw.Trim();

        if (s.StartsWith("DAILY:", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(6).Trim();

        if (s.StartsWith("CUSTOM:", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(7).Trim();

        if (s.StartsWith("SEED:", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(5).Trim();

        return s;
    }

    private static int BuildHashedSeed(string seedToken)
    {
        return StableHash(seedToken ?? string.Empty);
    }

    private static int CreateNewSessionSeedInt()
    {
        string raw =
            Guid.NewGuid().ToString("N") + "|" +
            DateTime.UtcNow.Ticks.ToString() + "|" +
            SystemInfo.deviceUniqueIdentifier;

        int h = StableHash(raw);
        if (h == 0) h = 1;
        return h;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < s.Length; i++)
                hash = hash * 31 + s[i];
            return hash;
        }
    }

    public static void ClearSessionSeed()
    {
        ActiveSeed = 0;
        ActiveMode = SeedMode.None;
        _seedApplied = false;
    }
}
