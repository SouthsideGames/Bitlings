using System;
using System.Collections.Generic;

public static class MonsterVariantPreference
{
    public static bool PlayerHasBothVariants(string monsterId, out OwnedMonsterData shiny, out OwnedMonsterData nonShiny)
    {
        shiny = null;
        nonShiny = null;

        var owned = SaveManager.Data?.owned;
        if (owned == null || string.IsNullOrEmpty(monsterId)) return false;

        for (int i = 0; i < owned.Count; i++)
        {
            var om = owned[i];
            if (om == null || om.monsterId != monsterId) continue;

            bool s = om.isShiny || om.shinyTier > 0;
            if (s)
            {
                if (shiny == null || Better(om, shiny)) shiny = om;
            }
            else
            {
                if (nonShiny == null || Better(om, nonShiny)) nonShiny = om;
            }
        }

        return shiny != null && nonShiny != null;
    }

    public static OwnedMonsterData GetPreferredOwned(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return null;

        var data = SaveManager.Data;
        var owned = data?.owned;
        if (owned == null) return null;

        PlayerHasBothVariants(monsterId, out var shiny, out var nonShiny);

        // If only one variant exists, return it.
        if (shiny != null && nonShiny == null) return shiny;
        if (nonShiny != null && shiny == null) return nonShiny;

        // If both exist, use saved preference.
        string prefUid = GetPreferredUid(monsterId);
        if (!string.IsNullOrEmpty(prefUid))
        {
            var match = owned.Find(o => o != null && o.ownedUID == prefUid);
            if (match != null) return match;
        }

        // Default fallback when both exist:
        // prefer non-shiny unless you want shiny-first.
        return nonShiny ?? shiny;
    }

    public static OwnedMonsterData GetOtherVariant(string monsterId, OwnedMonsterData currentPreferred)
    {
        if (string.IsNullOrEmpty(monsterId)) return null;

        PlayerHasBothVariants(monsterId, out var shiny, out var nonShiny);
        if (shiny == null || nonShiny == null) return null;

        bool curIsShiny = currentPreferred != null && (currentPreferred.isShiny || currentPreferred.shinyTier > 0);
        return curIsShiny ? nonShiny : shiny;
    }

    public static bool IsPreferredShiny(string monsterId)
    {
        var pref = GetPreferredOwned(monsterId);
        return pref != null && (pref.isShiny || pref.shinyTier > 0);
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
        if (a.shinyTier != b.shinyTier) return a.shinyTier > b.shinyTier;
        if (a.level != b.level) return a.level > b.level;
        return a.currentXP > b.currentXP;
    }
}
