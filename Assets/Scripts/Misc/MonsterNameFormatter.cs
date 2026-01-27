using UnityEngine;

public static class MonsterNameFormatter
{
    public static string GetDisplayName(MonsterDataSO def)
    {
        if (!def) return string.Empty;
        if (!string.IsNullOrEmpty(def.displayName)) return def.displayName;
        return def.name ?? string.Empty;
    }

    public static string Format(string baseName, bool isShiny)
    {
        if (string.IsNullOrEmpty(baseName)) return string.Empty;
        return isShiny ? $"*<i>{baseName}</i>*" : baseName;
    }

    public static string Format(MonsterDataSO def, bool isShiny)
    {
        return Format(GetDisplayName(def), isShiny);
    }

    public static bool IsShiny(MonsterDataSO def)
    {
        if (!def) return false;

        try
        {
            var t = def.GetType();

            var f = t.GetField("isShiny", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null)
            {
                var v = f.GetValue(def);
                if (v is bool b) return b;
            }

            var p2 = t.GetProperty("isShiny", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (p2 != null && p2.CanRead)
            {
                var v = p2.GetValue(def, null);
                if (v is bool b) return b;
            }
        }
        catch { }

        return false;
    }

    public static Sprite GetIcon(MonsterDataSO def, bool isShiny, bool backIcon)
    {
        if (!def) return null;

        if (isShiny)
        {
            if (backIcon)
            {
                if (def.shinyBackIcon) return def.shinyBackIcon;
                if (def.backIcon) return def.backIcon;
                if (def.shinyIcon) return def.shinyIcon;
                return def.icon;
            }

            if (def.shinyIcon) return def.shinyIcon;
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