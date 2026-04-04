using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

public enum ResourceType
{
    None = 0, Credits = 1, Energy = 2, Medkit = 3, Material = 4,
    PPEPermit = 5, Flyer = 6, WorkOrder = 7, Favor = 8,
    TrainingVoucher = 9, WellnessVoucher = 10, EfficiencyVoucher = 11, PremiumOrb = 12, BlessingScale = 13, Coffee = 14, GrowthCore = 16, PackVoucher = 17,
    BullToken = 18, BearToken = 19
}

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager I { get; private set; }

    [Header("Migration")]
    [Tooltip("If true, performs a one-time migration from SaveManager.Data.credits to ResourceBank[Credits] using a JSON sidecar flag.")]
    [SerializeField] private bool runOneShotLegacyCreditMigration = true;

    // ─────────────────────────────────────────────────────────────────────────────
    // JSON migration sidecar
    // ─────────────────────────────────────────────────────────────────────────────
    [Serializable]
    private class MigrationFlags
    {
        public bool creditMigratedV2;
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

        if (runOneShotLegacyCreditMigration)
            TryMigrateLegacyCreditsOnce_WithJson();

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
        int before = ResourceBank.Get(type);
        ResourceBank.Add(type, amount);
        int after = ResourceBank.Get(type);

        int gained = Mathf.Max(0, after - before);
        if (gained > 0)
            TrackLifetimeGain(type, gained);

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
        var gainedByType = new Dictionary<ResourceType, int>();

        foreach (var ra in amounts)
        {
            if (ra.amount == 0) continue;

            int before = ResourceBank.Get(ra.type);
            ResourceBank.Add(ra.type, ra.amount);
            int after = ResourceBank.Get(ra.type);

            int gained = Mathf.Max(0, after - before);
            if (gained > 0)
            {
                if (gainedByType.TryGetValue(ra.type, out int cur))
                    gainedByType[ra.type] = cur + gained;
                else
                    gainedByType.Add(ra.type, gained);
            }

            any = true;
        }

        foreach (var kv in gainedByType)
            TrackLifetimeGain(kv.Key, kv.Value);

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
    // Credits helpers (titles-aware)
    // ─────────────────────────────────────────────────────────────────────────────

    public int AddCredits(int baseCredits)
    {
        int amt = Mathf.Max(0, baseCredits);
        if (amt == 0) return 0;
        Add(ResourceType.Credits, amt);
        return amt;
    }

    // Titles-aware credit award (manual/idle battles with context)
    public int AddCreditsWithTitles(int baseCredits, string leadMonsterId, MonsterDataSO wild, int wildLevel)
    {
        int scaled = Mathf.Max(0, baseCredits);
        if (!string.IsNullOrEmpty(leadMonsterId))
        {
            float cm = TitlesAdapter.GetCreditMultOnVictory(leadMonsterId, wild, wildLevel);
            if (cm > 0f) scaled = Mathf.RoundToInt(scaled * cm);
        }
        return AddCredits(scaled);
    }

    // Titles-aware credit award (contextless, e.g., after capture)
    public int AddCreditsWithTitles(int baseCredits, string leadMonsterId)
    {
        int scaled = Mathf.Max(0, baseCredits);
        if (!string.IsNullOrEmpty(leadMonsterId))
        {
            float cm = TitlesAdapter.GetCreditMultOnVictory(leadMonsterId, null, 0);
            if (cm > 0f) scaled = Mathf.RoundToInt(scaled * cm);
        }
        return AddCredits(scaled);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // One-shot legacy migration (JSON sidecar)
    // ─────────────────────────────────────────────────────────────────────────────

    private void TryMigrateLegacyCreditsOnce_WithJson()
    {
        var flags = LoadMigrationFlags();
        var data = SaveManager.Data;

        bool saveAlreadyMigrated = data != null && data.creditsMigratedToResourceBank;

        if (saveAlreadyMigrated)
        {
            if (!flags.creditMigratedV2)
            {
                flags.creditMigratedV2 = true;
                flags.savedAtUnix = NowUnix();
                SaveMigrationFlags(flags);
            }
            return;
        }

        if (flags.creditMigratedV2)
        {
            if (data != null && !data.creditsMigratedToResourceBank)
            {
                data.creditsMigratedToResourceBank = true;
                SaveManager.Save();
            }
            return;
        }

        // Authoritative source for migration is the legacy field.
        int legacyCredits = (data != null) ? Mathf.Max(0, data.credits) : 0;
        int bankCredits   = ResourceBank.Get(ResourceType.Credits);

        if (legacyCredits != bankCredits)
        {
            int delta = legacyCredits - bankCredits;
            if (delta != 0)
                ResourceBank.Add(ResourceType.Credits, delta);
        }

        // Snap legacy to bank once, then save the main JSON.
        if (data != null)
        {
            int finalCredits = ResourceBank.Get(ResourceType.Credits);
            if (data.credits != finalCredits)
                data.credits = finalCredits;

            data.creditsMigratedToResourceBank = true;

            SaveManager.Save();
        }

        // Mark migrated in the sidecar JSON.
        flags.creditMigratedV2 = true;
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
            _ = e;
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"SaveMigrationFlags failed: {e.Message}");
            #endif
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

    private static void EnsureLifetimeLedgerSized()
    {
        SaveManager.LoadOrCreate();

        if (SaveManager.Data == null)
            return;

        SaveManager.Data.lifetimeResourceCollected ??= new List<int>();

        int need = 0;
        foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
            need = Mathf.Max(need, (int)t + 1);

        while (SaveManager.Data.lifetimeResourceCollected.Count < need)
            SaveManager.Data.lifetimeResourceCollected.Add(0);
    }

    private static void TrackLifetimeGain(ResourceType type, int amount)
    {
        if (amount <= 0) return;

        EnsureLifetimeLedgerSized();

        var data = SaveManager.Data;
        if (data == null || data.lifetimeResourceCollected == null)
            return;

        int idx = (int)type;
        if (idx < 0 || idx >= data.lifetimeResourceCollected.Count)
            return;

        long next = (long)data.lifetimeResourceCollected[idx] + amount;
        if (next > int.MaxValue) next = int.MaxValue;
        if (next < 0) next = 0;

        data.lifetimeResourceCollected[idx] = (int)next;
    }

    public void InitializeNewAccountResources()
    {
        ResourceBank.EnsureSize();

        // Hard reset all resources
        foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
        {
            ResourceBank.Set(t, 0);
        }

        ResourceBank.Set(ResourceType.Energy, 50);
        ResourceBank.Set(ResourceType.Credits, 0);
        ResourceBank.Set(ResourceType.Medkit, 0);
        ResourceBank.Set(ResourceType.PackVoucher, 0);
        ResourceBank.Set(ResourceType.TrainingVoucher, 0);
        ResourceBank.Set(ResourceType.WellnessVoucher, 0);
        ResourceBank.Set(ResourceType.EfficiencyVoucher, 0);
        ResourceBank.Set(ResourceType.PPEPermit, 0);
        ResourceBank.Set(ResourceType.Flyer, 0);
        ResourceBank.Set(ResourceType.WorkOrder, 0);
        ResourceBank.Set(ResourceType.Favor, 0);
        ResourceBank.Set(ResourceType.Material, 0);
        ResourceBank.Set(ResourceType.PremiumOrb, 0);
        ResourceBank.Set(ResourceType.BlessingScale, 0);
        ResourceBank.Set(ResourceType.Coffee, 0);
        ResourceBank.Set(ResourceType.GrowthCore, 0);


        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
    }

}
