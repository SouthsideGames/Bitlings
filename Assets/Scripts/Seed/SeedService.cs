using System;
using UnityEngine;

public static class SeedService
{
    public static int ActiveSeed { get; private set; }

    // Optional: lets UI show what mode is active (CUSTOM/DAILY/SESSION/NONE)
    public enum SeedMode { None, Session, Daily, Custom }
    public static SeedMode ActiveMode { get; private set; } = SeedMode.None;

    private const string DailySeedPrefsKey = "DailySeed_JSON";

    private const string SessionSeedPrefsKey = "SessionSeed_Int";

    [Serializable]
    private class DailySeedSave
    {
        public int dayIndex;
        public string seed;
        public int lastRerollDayIndex;
    }

    private static bool _seedApplied;

    public static void ApplyGlobalSeedForSession()
    {
        if (_seedApplied) return;

        var fu = FeatureUnlockManager.I;
        var sm = SettingsManager.I;
        if (fu == null || sm == null)
        {
            Debug.Log("[SeedService] FeatureUnlockManager or SettingsManager not ready; skipping seed.");
            return;
        }

        var settings = sm.S;
        if (settings == null)
            return;

        // ─────────────────────────────────────────────────────
        // 1) CUSTOM SEED (highest priority)
        // ─────────────────────────────────────────────────────
        if (fu.IsUnlocked(FeatureId.Seeds_CustomInput) &&
            settings.useCustomSeed &&
            !string.IsNullOrWhiteSpace(settings.customSeed))
        {
            int seed = BuildHashedSeed(settings.customSeed, includePlayerId: true);

            ActiveSeed = seed;
            ActiveMode = SeedMode.Custom;

            UnityEngine.Random.InitState(seed);
            _seedApplied = true;

            Debug.Log($"[SeedService] Applied CUSTOM seed (hash={seed}).");
            return;
        }

        // ─────────────────────────────────────────────────────
        // 2) DAILY SEED (fallback if custom not active)
        // ─────────────────────────────────────────────────────
        if (fu.IsUnlocked(FeatureId.Seeds_DailyBasic))
        {
            var ds = LoadDailySeed() ?? new DailySeedSave
            {
                dayIndex = -1,
                seed = string.Empty,
                lastRerollDayIndex = -1
            };

            int today = SaveManager.TodayDayIndexUTC();
            if (ds.dayIndex != today || string.IsNullOrEmpty(ds.seed))
            {
                ds.dayIndex = today;
                ds.seed = GenerateNewSeedString();

                if (ds.lastRerollDayIndex <= 0) ds.lastRerollDayIndex = -1;
                SaveDailySeed(ds);
            }

            int seed = BuildHashedSeed(ds.seed, includePlayerId: true);

            ActiveSeed = seed;
            ActiveMode = SeedMode.Daily;

            UnityEngine.Random.InitState(seed);
            _seedApplied = true;

            Debug.Log($"[SeedService] Applied DAILY seed for day={today} (hash={seed}).");
            return;
        }

        // ─────────────────────────────────────────────────────
        // 3) NO SEED FEATURES → apply a random SESSION seed (displayable)
        // ─────────────────────────────────────────────────────
        // This keeps the "random each time you open the game" behavior,
        // but makes it *observable* and repeatable for debugging.
        int sessionSeed = LoadOrCreateSessionSeed();

        ActiveSeed = sessionSeed;
        ActiveMode = SeedMode.Session;

        UnityEngine.Random.InitState(sessionSeed);
        _seedApplied = true;

        Debug.Log($"[SeedService] Applied SESSION seed (hash={sessionSeed}).");
    }

    public static string GetCurrentDailySeedString()
    {
        var ds = LoadDailySeed();
        if (ds == null) return string.Empty;

        int today = SaveManager.TodayDayIndexUTC();
        if (ds.dayIndex != today) return string.Empty;

        return ds.seed ?? string.Empty;
    }

    public static bool TryRerollDailySeed(out string newSeed)
    {
        newSeed = null;

        var fu = FeatureUnlockManager.I;
        var sm = SettingsManager.I;
        if (fu == null || sm == null) return false;

        // Must have daily + reroll features
        if (!fu.IsUnlocked(FeatureId.Seeds_DailyBasic) ||
            !fu.IsUnlocked(FeatureId.Seeds_RerollDailyOnce))
        {
            return false;
        }

        var settings = sm.S;
        bool customActive = fu.IsUnlocked(FeatureId.Seeds_CustomInput) &&
                            settings != null &&
                            settings.useCustomSeed &&
                            !string.IsNullOrWhiteSpace(settings.customSeed);

        var ds = LoadDailySeed() ?? new DailySeedSave
        {
            dayIndex = -1,
            seed = string.Empty,
            lastRerollDayIndex = -1
        };

        int today = SaveManager.TodayDayIndexUTC();

        if (ds.lastRerollDayIndex == today)
        {
            Debug.Log("[SeedService] Daily seed reroll already used today.");
            return false;
        }

        ds.dayIndex = today;
        ds.seed = GenerateNewSeedString();
        ds.lastRerollDayIndex = today;
        SaveDailySeed(ds);

        newSeed = ds.seed;

        if (_seedApplied && !customActive)
        {
            int seed = BuildHashedSeed(ds.seed, includePlayerId: true);

            ActiveSeed = seed;
            ActiveMode = SeedMode.Daily;

            UnityEngine.Random.InitState(seed);
            Debug.Log($"[SeedService] Re-applied RNG with NEW daily seed (hash={seed}).");
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────
    // Internals: Daily seed persistence via PlayerPrefs
    // ─────────────────────────────────────────────────────────

    private static DailySeedSave LoadDailySeed()
    {
        if (!PlayerPrefs.HasKey(DailySeedPrefsKey))
            return null;

        string json = PlayerPrefs.GetString(DailySeedPrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            return JsonUtility.FromJson<DailySeedSave>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveDailySeed(DailySeedSave ds)
    {
        if (ds == null) return;
        string json = JsonUtility.ToJson(ds);
        PlayerPrefs.SetString(DailySeedPrefsKey, json);
        PlayerPrefs.Save();
    }

    private static string GenerateNewSeedString()
    {
        return Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
    }

    private static int BuildHashedSeed(string seedString, bool includePlayerId)
    {
        if (seedString == null) seedString = string.Empty;

        string combined = seedString;

        if (includePlayerId && SaveManager.Data != null)
        {
            string pid = SaveManager.Data.playerId ?? string.Empty;
            combined = seedString + "|" + pid;
        }

        return StableHash(combined);
    }

    private static int LoadOrCreateSessionSeed()
    {
        if (PlayerPrefs.HasKey(SessionSeedPrefsKey))
        {
            int existing = PlayerPrefs.GetInt(SessionSeedPrefsKey, 0);
            if (existing != 0) return existing;
        }

        int created = CreateNewSessionSeedInt();
        PlayerPrefs.SetInt(SessionSeedPrefsKey, created);
        PlayerPrefs.Save();
        return created;
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

    public static void ClearSessionSeed()
    {
        if (PlayerPrefs.HasKey(SessionSeedPrefsKey))
        {
            PlayerPrefs.DeleteKey(SessionSeedPrefsKey);
            PlayerPrefs.Save();
        }
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


}
