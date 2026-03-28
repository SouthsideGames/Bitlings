using System;
using System.Collections.Generic;

public static class MonsterVariantPreference
{
    public static bool PlayerHasBothVariants(string monsterId, out OwnedMonsterData premium, out OwnedMonsterData nonPremium)
    {
        premium = null;
        nonPremium = null;

        var owned = SaveManager.Data?.owned;
        if (owned == null || string.IsNullOrEmpty(monsterId)) return false;

        for (int i = 0; i < owned.Count; i++)
        {
            var om = owned[i];
            if (om == null || om.monsterId != monsterId) continue;

            bool s = om.isPremium || om.premiumTier > 0;
            if (s)
            {
                if (premium == null || Better(om, premium)) premium = om;
            }
            else
            {
                if (nonPremium == null || Better(om, nonPremium)) nonPremium = om;
            }
        }

        return premium != null && nonPremium != null;
    }

    public static OwnedMonsterData GetPreferredOwned(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return null;

        var data = SaveManager.Data;
        var owned = data?.owned;
        if (owned == null) return null;

        PlayerHasBothVariants(monsterId, out var premium, out var nonPremium);

        // If only one variant exists, return it.
        if (premium != null && nonPremium == null) return premium;
        if (nonPremium != null && premium == null) return nonPremium;

        // If both exist, use saved preference.
        string prefUid = GetPreferredUid(monsterId);
        if (!string.IsNullOrEmpty(prefUid))
        {
            var match = owned.Find(o => o != null && o.ownedUID == prefUid);
            if (match != null) return match;
        }

        // Default fallback when both exist:
        // prefer non-premium unless you want premium-first.
        return nonPremium ?? premium;
    }

    public static OwnedMonsterData GetOtherVariant(string monsterId, OwnedMonsterData currentPreferred)
    {
        if (string.IsNullOrEmpty(monsterId)) return null;

        PlayerHasBothVariants(monsterId, out var premium, out var nonPremium);
        if (premium == null || nonPremium == null) return null;

        bool curIsPremium = currentPreferred != null && (currentPreferred.isPremium || currentPreferred.premiumTier > 0);
        return curIsPremium ? nonPremium : premium;
    }

    public static bool IsPreferredPremium(string monsterId)
    {
        var pref = GetPreferredOwned(monsterId);
        return pref != null && (pref.isPremium || pref.premiumTier > 0);
    }

    public static void SetPreferred(string monsterId, string preferredOwnedUid)
    {
        if (string.IsNullOrEmpty(monsterId) || string.IsNullOrEmpty(preferredOwnedUid)) return;

        var data = SaveManager.Data;
        if (data == null) return;

        data.preferredVariants ??= new List<PreferredVariantKV>();

        var kv = data.preferredVariants.Find(x => x != null && x.monsterId == monsterId);
        if (kv == null)
        {
            kv = new PreferredVariantKV { monsterId = monsterId, preferredOwnedUid = preferredOwnedUid };
            data.preferredVariants.Add(kv);
        }
        else
        {
            kv.preferredOwnedUid = preferredOwnedUid;
        }

        SaveManager.Save();
    }

    private static string GetPreferredUid(string monsterId)
    {
        var list = SaveManager.Data?.preferredVariants;
        if (list == null) return null;

        for (int i = 0; i < list.Count; i++)
        {
            var kv = list[i];
            if (kv != null && kv.monsterId == monsterId)
                return kv.preferredOwnedUid;
        }
        return null;
    }

    private static bool Better(OwnedMonsterData a, OwnedMonsterData b)
    {
        if (a == null) return false;
        if (b == null) return true;

        // choose "best copy" within a variant bucket
        if (a.premiumTier != b.premiumTier) return a.premiumTier > b.premiumTier;
        if (a.level != b.level) return a.level > b.level;
        return a.currentXP > b.currentXP;
    }
}
