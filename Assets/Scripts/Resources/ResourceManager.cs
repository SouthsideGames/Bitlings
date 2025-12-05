using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

public enum ResourceType
{
    None = 0, Coin = 1, Energy = 2, Medkit = 3, Material = 4,
    TypeResBooster = 5, Lure = 6, CaptureBand = 7, Luck = 8,
    AttackBooster = 9, HPBooster = 10, SpeedBooster = 11, ShinyOrb = 12, BlessingScale = 13, RestCharge = 14, GrowthCore = 16, PackShard = 17
}

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager I { get; private set; }

    [Header("Migration")]
    [Tooltip("If true, performs a one-time migration from SaveManager.Data.coins to ResourceBank[Coins] using a JSON sidecar flag.")]
    [SerializeField] private bool runOneShotLegacyCoinMigration = true;

    // ─────────────────────────────────────────────────────────────────────────────
    // JSON migration sidecar
    // ─────────────────────────────────────────────────────────────────────────────
    [Serializable]
    private class MigrationFlags
    {
        public bool coinMigratedV2;
        public long savedAtUnix;
    }

    private static string MigrationsPath =>
        Path.Combine(Application.persistentDataPath, "idle_migrations.json");

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        SaveManager.LoadOrCreate();
        ResourceBank.EnsureSize();

        if (runOneShotLegacyCoinMigration)
            TryMigrateLegacyCoinsOnce_WithJson();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Basic API
    // ─────────────────────────────────────────────────────────────────────────────

    public int Get(ResourceType type) => ResourceBank.Get(type);

    public void Set(ResourceType type, int value)
    {
        int cur = ResourceBank.Get(type);
        int delta = value - cur;
        if (delta != 0) Add(type, delta);
    }

    public void Add(ResourceType type, int amount)
    {
        if (amount == 0) return;

        ResourceBank.Add(type, amount);

        GameEvents.OnResourcesChanged?.Invoke();
        GameEvents.ResourceAdded?.Invoke(type, amount);
    }

    public bool TrySpend(ResourceType type, int amount)
    {
        if (amount <= 0) return true;

        bool ok = ResourceBank.TrySpend(type, amount);
        if (ok)
            GameEvents.OnResourcesChanged?.Invoke();

        return ok;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Bundles / utilities
    // ─────────────────────────────────────────────────────────────────────────────

    [Serializable]
    public struct ResourceAmount
    {
        public ResourceType type;
        public int amount;
    }

    public void AddMany(IEnumerable<ResourceAmount> amounts)
    {
        bool any = false;
        foreach (var ra in amounts)
        {
            if (ra.amount == 0) continue;
            ResourceBank.Add(ra.type, ra.amount);
            any = true;
        }
        if (any)
        {
            GameEvents.OnResourcesChanged?.Invoke();
            // Note: fire ResourceAdded per-type if specific listeners depend on it.
        }
    }

    public bool TrySpendMany(IEnumerable<ResourceAmount> costs)
    {
        // 1) Affordability check
        foreach (var c in costs)
        {
            if (c.amount <= 0) continue;
            if (ResourceBank.Get(c.type) < c.amount) return false;
        }

        // 2) Apply
        bool any = false;
        foreach (var c in costs)
        {
            if (c.amount <= 0) continue;
            ResourceBank.TrySpend(c.type, c.amount);
            any = true;
        }
        if (any) GameEvents.OnResourcesChanged?.Invoke();
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Coins helpers (titles-aware)
    // ─────────────────────────────────────────────────────────────────────────────

    public int AddCoins(int baseCoins)
    {
        int amt = Mathf.Max(0, baseCoins);
        if (amt == 0) return 0;
        Add(ResourceType.Coin, amt);
        return amt;
    }

    // Titles-aware coin award (manual/idle battles with context)
    public int AddCoinsWithTitles(int baseCoins, string leadMonsterId, MonsterDataSO wild, int wildLevel)
    {
        int scaled = Mathf.Max(0, baseCoins);
        if (!string.IsNullOrEmpty(leadMonsterId))
        {
            float cm = TitlesAdapter.GetCoinMultOnVictory(leadMonsterId, wild, wildLevel);
            if (cm > 0f) scaled = Mathf.RoundToInt(scaled * cm);
        }
        return AddCoins(scaled);
    }

    // Titles-aware coin award (contextless, e.g., after capture)
    public int AddCoinsWithTitles(int baseCoins, string leadMonsterId)
    {
        int scaled = Mathf.Max(0, baseCoins);
        if (!string.IsNullOrEmpty(leadMonsterId))
        {
            float cm = TitlesAdapter.GetCoinMultOnVictory(leadMonsterId, null, 0);
            if (cm > 0f) scaled = Mathf.RoundToInt(scaled * cm);
        }
        return AddCoins(scaled);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // One-shot legacy migration (JSON sidecar)
    // ─────────────────────────────────────────────────────────────────────────────

    private void TryMigrateLegacyCoinsOnce_WithJson()
    {
        var flags = LoadMigrationFlags();

        if (flags.coinMigratedV2)
            return;

        // Authoritative source for migration is the legacy field.
        var data = SaveManager.Data;
        int legacyCoins = (data != null) ? Mathf.Max(0, data.coins) : 0;
        int bankCoins   = ResourceBank.Get(ResourceType.Coin);

        if (legacyCoins != bankCoins)
        {
            int delta = legacyCoins - bankCoins;
            if (delta != 0)
                ResourceBank.Add(ResourceType.Coin, delta);
        }

        // Snap legacy to bank once, then save the main JSON.
        if (data != null)
        {
            int finalCoins = ResourceBank.Get(ResourceType.Coin);
            if (data.coins != finalCoins)
                data.coins = finalCoins;

            SaveManager.Save();
        }

        // Mark migrated in the sidecar JSON.
        flags.coinMigratedV2 = true;
        flags.savedAtUnix = NowUnix();
        SaveMigrationFlags(flags);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Migration JSON helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static MigrationFlags LoadMigrationFlags()
    {
        try
        {
            if (!File.Exists(MigrationsPath)) return new MigrationFlags();
            var json = File.ReadAllText(MigrationsPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return new MigrationFlags();

            var flags = JsonUtility.FromJson<MigrationFlags>(json);
            return flags ?? new MigrationFlags();
        }
        catch
        {
            return new MigrationFlags();
        }
    }

    private static void SaveMigrationFlags(MigrationFlags flags)
    {
        try
        {
            string json = JsonUtility.ToJson(flags ?? new MigrationFlags(), prettyPrint: true);
            AtomicWrite(MigrationsPath, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveMigrationFlags failed: {e.Message}");
        }
    }

    private static void AtomicWrite(string path, string contents)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, contents, Encoding.UTF8);

        try
        {
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch
        {
            try { if (!File.Exists(path)) File.Copy(tmp, path); } catch { }
            try { File.Delete(tmp); } catch { }
        }
    }

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
