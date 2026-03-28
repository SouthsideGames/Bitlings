using UnityEngine;

public static class MonsterNameFormatter
{
    public static string GetDisplayName(MonsterDataSO def)
    {
        if (!def) return string.Empty;
        if (!string.IsNullOrEmpty(def.displayName)) return def.displayName;
        return def.name ?? string.Empty;
    }

    public static string Format(string baseName, bool isPremium)
    {
        if (string.IsNullOrEmpty(baseName)) return string.Empty;
        return isPremium ? $"*<i>{baseName}</i>*" : baseName;
    }

    public static string Format(MonsterDataSO def, bool isPremium)
    {
        return Format(GetDisplayName(def), isPremium);
    }

    public static bool IsPremium(MonsterDataSO def)
    {
        if (!def) return false;

        try
        {
            var t = def.GetType();

            var f = t.GetField("isPremium", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null)
            {
                var v = f.GetValue(def);
                if (v is bool b) return b;
            }

            var p2 = t.GetProperty("isPremium", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (p2 != null && p2.CanRead)
            {
                var v = p2.GetValue(def, null);
                if (v is bool b) return b;
            }
        }
        catch { }

        return false;
    }

    public static Sprite GetIcon(MonsterDataSO def, bool isPremium, bool backIcon)
    {
        if (!def) return null;

        if (isPremium)
        {
            if (backIcon)
            {
                if (def.premiumBackIcon) return def.premiumBackIcon;
                if (def.backIcon) return def.backIcon;
                if (def.premiumIcon) return def.premiumIcon;
                return def.icon;
            }

            if (def.premiumIcon) return def.premiumIcon;
            return def.icon;
        }

        if (backIcon)
        {
            if (def.backIcon) return def.backIcon;
            return def.icon;
        }

        return def.icon;
    }
}