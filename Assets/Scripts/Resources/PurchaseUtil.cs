using UnityEngine;
using System;

public static class PurchaseUtil
{
    [Obsolete("Use TrySpendCoinsOnly for upgrades; materials are now for job-site leveling UI only.")]
    public static bool TrySpendMaterialsThenCoins(int materialRequired)
    {
        if (materialRequired <= 0) return true;

        int haveMats    = ResourceBank.Get(ResourceType.Material);
        int matsToSpend = Mathf.Clamp(haveMats, 0, materialRequired);
        int remaining   = materialRequired - matsToSpend;

        if (matsToSpend > 0)
        {
            if (!ResourceBank.TrySpend(ResourceType.Material, matsToSpend))
                return false;
        }

        if (remaining > 0)
        {
            const int CoinsPerMaterial = 5;
            int coinsNeeded = remaining * CoinsPerMaterial;

            if (!ResourceBank.TrySpend(ResourceType.Coin, coinsNeeded))
            {
                if (matsToSpend > 0) ResourceBank.Add(ResourceType.Material, matsToSpend);
                return false;
            }
        }

        GameEvents.OnResourcesChanged?.Invoke();
        return true;
    }

    [Obsolete("Unused for upgrades; kept for legacy UI previews.")]
    public static int CoinsRemainderIfPayingWithMats(int materialRequired)
    {
        const int CoinsPerMaterial = 5;
        int haveMats  = ResourceBank.Get(ResourceType.Material);
        int remaining = Mathf.Max(0, materialRequired - Mathf.Clamp(haveMats, 0, materialRequired));
        return remaining * CoinsPerMaterial;
    }

    public static bool TrySpendCoinsOnly(int coinCost)
    {
        if (coinCost <= 0) return true;
        if (!ResourceBank.TrySpend(ResourceType.Coin, coinCost)) return false;
        GameEvents.OnResourcesChanged?.Invoke();
        return true;
    }
}
