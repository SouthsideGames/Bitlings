using System;
using System.Reflection;
using UnityEngine;

public struct TitleStatMods
{
    public float hpPct;   // +% Max HP (e.g., 0.10 = +10%)
    public float atkPct;  // +% ATK
    public float defPct;  // +% DEF (post-curve, pre-mitigation helper)
    public float spdPct;  // +% SPD (applied to derived calc)
    public int   atkFlat; // +flat ATK (optional)
    public int   defFlat; // +flat DEF (optional)
    public int   spdFlat; // +flat SPD (optional)
}

public static class TitlesAdapter
{
    // We’ll try multiple class names so we don’t lock you in.
    private static readonly string[] CandidateTypes =
    {
        "TitleRuntime",
        "TitleManager",
        "TitlesManager"
    };

    static Type _titleType;
    static object _titleSingleton;

    static TitlesAdapter()
    {
        foreach (var name in CandidateTypes)
        {
            _titleType = Type.GetType(name) ?? FindInAllAssemblies(name);
            if (_titleType != null)
            {
                _titleSingleton = _titleType.GetField("I", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                                  ?? _titleType.GetProperty("I", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null)
                                  ?? _titleType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                break;
            }
        }
    }

    private static Type FindInAllAssemblies(string typeName)
    {
        var asms = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < asms.Length; i++)
        {
            try
            {
                var t = asms[i].GetType(typeName);
                if (t != null) return t;
            } catch { }
        }
        return null;
    }

    private static bool TryInvoke(string method, object[] args, out object result)
    {
        result = null;
        if (_titleType == null) return false;

        var mi = _titleType.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        if (mi == null) return false;

        var target = mi.IsStatic ? null : (_titleSingleton ?? Activator.CreateInstance(_titleType));
        try { result = mi.Invoke(target, args); return true; }
        catch (Exception e) { Debug.LogWarning($"[TitlesAdapter] {method} failed: {e.Message}"); return false; }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Lifecycle / Events
    // ─────────────────────────────────────────────────────────────────────────────

    public static void OnBattleStart(string activeMonsterId, MonsterDataSO wild, int wildLevel)
        => TryInvoke("OnBattleStart", new object[] { activeMonsterId, wild, wildLevel }, out _);

    public static void OnBattleEnd(string activeMonsterId, bool victory, MonsterDataSO wild, int wildLevel)
        => TryInvoke("OnBattleEnd", new object[] { activeMonsterId, victory, wild, wildLevel }, out _);

    public static void OnMonsterLeveled(string monsterId, int newLevel)
        => TryInvoke("OnMonsterLeveled", new object[] { monsterId, newLevel }, out _);

    public static void OnMonsterCaptured(string monsterId, MonsterType type, int level, bool isShiny)
        => TryInvoke("OnMonsterCaptured", new object[] { monsterId, type, level, isShiny }, out _);

    public static void OnMonsterEvolved(string newMonsterId)
        => TryInvoke("OnMonsterEvolved", new object[] { newMonsterId }, out _);

    // ─────────────────────────────────────────────────────────────────────────────
    // Battle-time stat mods
    // ─────────────────────────────────────────────────────────────────────────────

    public static TitleStatMods GetBattleStatMods(string monsterId)
    {
        if (TryInvoke("GetBattleStatMods", new object[] { monsterId }, out var res) && res is TitleStatMods tsm)
            return tsm;
        return default;
    }

    // If your title system exposes specific getters (e.g., GetHpPct, etc.), we’ll still be fine with the above.
    // Otherwise you can map your return type to TitleStatMods via your reflection method.

    // ─────────────────────────────────────────────────────────────────────────────
    // Multipliers
    // ─────────────────────────────────────────────────────────────────────────────

    public static float GetCoinMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        if (TryInvoke("GetCoinMultOnVictory", new object[] { monsterId, wild, wildLevel }, out var res) && res is float f) return Mathf.Max(0f, f);
        return 1f;
    }

    public static float GetXPMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        if (TryInvoke("GetXPMultOnVictory", new object[] { monsterId, wild, wildLevel }, out var res) && res is float f) return Mathf.Max(0f, f);
        return 1f;
    }

    public static float GetCaptureChanceMult(string leadMonsterId)
    {
        if (TryInvoke("GetCaptureChanceMult", new object[] { leadMonsterId }, out var res) && res is float f) return Mathf.Max(0f, f);
        return 1f;
    }

    public static float GetJobRateMult(string workerOwnedOrDefId, JobType site)
    {
        if (TryInvoke("GetJobRateMult", new object[] { workerOwnedOrDefId, site }, out var res) && res is float f) return Mathf.Max(0f, f);
        return 1f;
    }
}
