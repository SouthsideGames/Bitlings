using System;
using System.Collections.Generic;
using UnityEngine;

public enum ResourceType {
    TrainingXP, Coins, Energy, Medkits, Materials,
    Sigils, Lures, CaptureBands, Luck,
    AttackBoosters, HPBoosters, SpeedBoosters, ShinyOrbs, BlessingTokens, RestCharge, Gems, GrowthCores
}


public class ResourceManager : MonoBehaviour
{
    public static ResourceManager I { get; private set; }

    [Header("Startup")]
    [Tooltip("If true, copy legacy Data.currency into enum-backed Coins once at boot.")]
    [SerializeField] private bool mirrorLegacyCurrencyOnBoot = true;

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        // Make sure Save is loaded and resource list is sized.
        SaveManager.LoadOrCreate();
        ResourceBank.EnsureSize();

        if (mirrorLegacyCurrencyOnBoot) MirrorLegacyCurrencyIntoBank();
        else MirrorBankCoinsIntoLegacy();
    }

    // --------------------------------------------------------------------
    // Basic API
    // --------------------------------------------------------------------

    public int Get(ResourceType type) => ResourceBank.Get(type);

    /// <summary> Sets a resource to an exact value by computing a delta and calling Add(). </summary>
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

        if (type == ResourceType.Coins)
            MirrorBankCoinsIntoLegacy();

        GameEvents.OnResourcesChanged?.Invoke();
        GameEvents.ResourceAdded?.Invoke(type, amount);
    }

    /// <summary> Attempts to spend; returns false if insufficient. Mirrors coins on success. </summary>
    public bool TrySpend(ResourceType type, int amount)
    {
        if (amount <= 0) return true;

        bool ok = ResourceBank.TrySpend(type, amount); // emits OnResourcesChanged on success
        if (ok && type == ResourceType.Coins)
            MirrorBankCoinsIntoLegacy();

        GameEvents.OnResourcesChanged?.Invoke();

        return ok;
    }

    // --------------------------------------------------------------------
    // Bundles / utilities
    // --------------------------------------------------------------------

    [Serializable]
    public struct ResourceAmount
    {
        public ResourceType type;
        public int amount;
    }

    /// <summary> Grants several resources at once. </summary>
    public void AddMany(IEnumerable<ResourceAmount> amounts)
    {
        bool touchedCoins = false;
        foreach (var ra in amounts)
        {
            if (ra.amount == 0) continue;
            ResourceBank.Add(ra.type, ra.amount);
            if (ra.type == ResourceType.Coins) touchedCoins = true;
        }
        if (touchedCoins) MirrorBankCoinsIntoLegacy();
        // If you prefer a single emit, add a batch API to ResourceBank and emit once.
    }

    /// <summary> Spends several resources; all-or-nothing (no partial spend). </summary>
    public bool TrySpendMany(IEnumerable<ResourceAmount> costs)
    {
        // 1) Affordability check
        foreach (var c in costs)
        {
            if (c.amount <= 0) continue;
            if (ResourceBank.Get(c.type) < c.amount) return false;
        }

        // 2) Apply
        bool touchedCoins = false;
        foreach (var c in costs)
        {
            if (c.amount <= 0) continue;
            ResourceBank.TrySpend(c.type, c.amount); // emits on success
            if (c.type == ResourceType.Coins) touchedCoins = true;
        }
        if (touchedCoins) MirrorBankCoinsIntoLegacy();
        return true;
    }

    // --------------------------------------------------------------------
    // Legacy currency mirroring
    // --------------------------------------------------------------------

    /// <summary> Copy legacy SaveManager.Data.currency into enum-backed bank (one-shot migration). </summary>
    public void MirrorLegacyCurrencyIntoBank()
    {
        if (SaveManager.Data == null) return;

        int legacy = Mathf.Max(0, SaveManager.Data.coins);
        int bankCoin = ResourceBank.Get(ResourceType.Coins);

        if (legacy != bankCoin)
        {
            int delta = legacy - bankCoin;
            if (delta != 0) ResourceBank.Add(ResourceType.Coins, delta);
        }

        MirrorBankCoinsIntoLegacy(); // keep both sides consistent
    }

    /// <summary> Set legacy SaveManager.Data.currency to whatever the bank has for Coins. </summary>
    public void MirrorBankCoinsIntoLegacy()
    {
        if (SaveManager.Data == null) return;

        int coins = ResourceBank.Get(ResourceType.Coins);
        if (SaveManager.Data.coins != coins)
        {
            SaveManager.Data.coins = coins;
            SaveManager.Save(); // small write; maintains legacy consumers
        }
    }

    public int AddCoins(int baseCoins)
    {
        int amt = Mathf.Max(0, baseCoins);
        if (amt == 0) return 0;
        Add(ResourceType.Coins, amt); // mirrors legacy via Add()
        return amt;
    }

    // --- Titles-aware coin award (manual/idle battles) ---
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

    // --- Titles-aware coin award (contextless, e.g. after capture) ---
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


}
