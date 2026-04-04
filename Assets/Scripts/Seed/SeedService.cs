using System;
using UnityEngine;

public static class SeedService
{
    private const int MinCustomSeedLength = 3;
    private const int MaxCustomSeedLength = 32;

    public static int ActiveSeed { get; private set; }

    public enum SeedMode { None, Session, Daily, Custom }
    public static SeedMode ActiveMode { get; private set; } = SeedMode.None;

    private static bool _seedApplied;

    public static void ApplyGlobalSeedForSession()
    {
        if (_seedApplied) return;

        // Launch-safe: always ensure a save root exists before attempting to read seed state.
        // If SaveManager is not ready for any reason, fall back to a session seed.
        try { SaveManager.LoadOrCreate(); } catch { /* ignore */ }

        if (SaveManager.Data == null)
        {
            ApplySessionSeed();
            return;
        }

        var fu = FeatureUnlockManager.I;
        var sm = SettingsManager.I;

        if (fu == null)
        {
            ApplySessionSeed();
            return;
        }

        bool customUnlocked = fu.IsUnlocked(FeatureId.Seeds_CustomInput);
        bool dailyUnlocked = fu.IsUnlocked(FeatureId.Seeds_DailyBasic);

        var settings = (sm != null) ? sm.settingsState : null;
        // If settings aren't available yet (boot order), simply skip custom-seed evaluation
        // rather than aborting seed application entirely.

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
        try { SaveManager.LoadOrCreate(); } catch { /* ignore */ }
        if (SaveManager.Data == null) return string.Empty;

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
        if (fu == null || sm == null || sm.settingsState == null)
            return string.Empty;

        if (!fu.IsUnlocked(FeatureId.Seeds_CustomInput))
            return string.Empty;

        if (!sm.settingsState.useCustomSeed)
            return string.Empty;

        return sm.settingsState.customSeed ?? string.Empty;
    }

    public static bool TryNormalizeAndValidateCustomSeed(string raw, out string normalizedToken, out string errorMessage)
    {
        normalizedToken = NormalizeSeedToken(raw);

        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            errorMessage = "Enter a seed first.";
            return false;
        }

        if (normalizedToken.Length < MinCustomSeedLength)
        {
            errorMessage = $"Seed must be at least {MinCustomSeedLength} characters.";
            return false;
        }

        if (normalizedToken.Length > MaxCustomSeedLength)
        {
            errorMessage = $"Seed must be {MaxCustomSeedLength} characters or fewer.";
            return false;
        }

        for (int i = 0; i < normalizedToken.Length; i++)
        {
            char c = normalizedToken[i];
            bool allowed = char.IsLetterOrDigit(c) || c == '-' || c == '_';
            if (!allowed)
            {
                errorMessage = "Seed can only use letters, numbers, '-' or '_'.";
                return false;
            }
        }

        errorMessage = string.Empty;
        return true;
    }

    public static string GetDailySeedTokenForToday()
    {
        try { SaveManager.LoadOrCreate(); } catch { /* ignore */ }
        if (SaveManager.Data == null) return string.Empty;

        EnsureDailySeedForToday();

        var ss = SaveManager.Data.seedState;
        if (ss == null)
            return string.Empty;

        return NormalizeSeedToken(ss.dailySeed);
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

        try { SaveManager.LoadOrCreate(); } catch { /* ignore */ }
        if (SaveManager.Data == null) return false;

        var fu = FeatureUnlockManager.I;
        var sm = SettingsManager.I;
        if (fu == null)
            return false;

        if (!CanRerollDailySeedNow())
            return false;

        var settings = (sm != null) ? sm.settingsState : null;
        bool customActive = fu.IsUnlocked(FeatureId.Seeds_CustomInput) &&
                            settings != null &&
                            settings.useCustomSeed &&
                            !string.IsNullOrWhiteSpace(settings.customSeed);

        var ss = SaveManager.Data.seedState ?? (SaveManager.Data.seedState = new SeedState());
        int today = SaveManager.TodayDayIndexUTC();

        if (ss.lastRerollDayIndex == today)
            return false;

        ss.dayIndex = today;
        string previousToken = NormalizeSeedToken(ss.dailySeed);
        string nextDailySeed = GenerateNewSeedString();
        for (int i = 0; i < 5 &&
             string.Equals(NormalizeSeedToken(nextDailySeed), previousToken, StringComparison.OrdinalIgnoreCase);
             i++)
        {
            nextDailySeed = GenerateNewSeedString();
        }

        ss.dailySeed = nextDailySeed;
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

    public static bool CanRerollDailySeedNow()
    {
        return CanRerollDailySeedNow(out _);
    }

    public static bool CanRerollDailySeedNow(out string reason)
    {
        reason = string.Empty;

        try { SaveManager.LoadOrCreate(); } catch { /* ignore */ }
        if (SaveManager.Data == null)
        {
            reason = "Save is not ready.";
            return false;
        }

        var fu = FeatureUnlockManager.I;
        if (fu == null)
        {
            reason = "Seed features are unavailable.";
            return false;
        }

        if (!fu.IsUnlocked(FeatureId.Seeds_DailyBasic))
        {
            reason = "Daily Seeds are locked.";
            return false;
        }

        if (!fu.IsUnlocked(FeatureId.Seeds_RerollDailyOnce))
        {
            reason = "Daily reroll is locked.";
            return false;
        }

        var ss = SaveManager.Data.seedState ?? (SaveManager.Data.seedState = new SeedState());
        int today = SaveManager.TodayDayIndexUTC();

        if (ss.lastRerollDayIndex == today)
        {
            reason = "Daily seed reroll already used today.";
            return false;
        }

        return true;
    }

    private static void EnsureDailySeedForToday()
    {
        if (SaveManager.Data == null) return;
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
