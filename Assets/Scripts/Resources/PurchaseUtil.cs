using UnityEngine;
using System;

public static class PurchaseUtil
{
    [Obsolete("Use TrySpendCreditsOnly for upgrades; materials are now for job-site leveling UI only.")]
    public static bool TrySpendMaterialsThenCredits(int materialRequired)
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
            const int creditsPerMaterial = 5;
            int creditsNeeded = remaining * creditsPerMaterial;

            if (!ResourceBank.TrySpend(ResourceType.Credits, creditsNeeded))
            {
                if (matsToSpend > 0) ResourceBank.Add(ResourceType.Material, matsToSpend);
                return false;
            }
        }

        GameEvents.OnResourcesChanged?.Invoke();
        return true;
    }

    [Obsolete("Unused for upgrades; kept for legacy UI previews.")]
    public static int CreditsRemainderIfPayingWithMats(int materialRequired)
    {
        const int creditsPerMaterial = 5;
        int haveMats  = ResourceBank.Get(ResourceType.Material);
        int remaining = Mathf.Max(0, materialRequired - Mathf.Clamp(haveMats, 0, materialRequired));
        return remaining * creditsPerMaterial;
    }

    public static bool TrySpendCreditsOnly(int creditCost)
    {
        if (creditCost <= 0) return true;
        if (!ResourceBank.TrySpend(ResourceType.Credits, creditCost)) return false;
        GameEvents.OnResourcesChanged?.Invoke();
        return true;
    }
}
