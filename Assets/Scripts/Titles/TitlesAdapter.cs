using System;
using System.Reflection;
using UnityEngine;
using System.Collections.Generic;

public struct TitleStatMods
{
    public float hpPct;   // +% Max HP (e.g., 0.10 = +10%)
    public float atkPct;  // +% ATK
    public float defPct;  // +% DEF (post-curve, pre-mitigation helper)
    public float spdPct;  // +% SPD (applied to derived calc)
    public int atkFlat; // +flat ATK (optional)
    public int defFlat; // +flat DEF (optional)
    public int spdFlat; // +flat SPD (optional)
}

public struct TitleDamageFilter
{
    public bool  cannotBeCrit;   // true = incoming attacks cannot crit
    public float percentReduce;  // 0.15 = reduce 15% of incoming damage (after DEF)
    public int   flatReduce;     // flat damage soak after % reduce
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
            }
            catch { }
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

    // Battle stat with context (conditional boosters)
    public static float GetStatValue(string ownedId, MonsterDataSO def, int level, string statKind, TitleContext ctx, float baseValue)
    {
        if (TryInvoke("GetStatValueRouter", new object[] { ownedId, def, level, statKind, ctx, baseValue }, out var res) && res is float f)
            return f;
        return baseValue;
    }

    // Effectiveness multiplier for attacker
    public static float GetEffectivenessMult(string ownedId, MonsterDataSO def, int level)
    {
        if (TryInvoke("GetEffectivenessMultiplier", new object[] { ownedId, def, level }, out var res) && res is float f)
            return Mathf.Max(0f, f);
        return 1f;
    }

    // Job fatigue multiplier (per worker)
    public static float GetJobFatigueMult(string ownedId, MonsterDataSO def, int level, JobType site)
    {
        if (TryInvoke("GetJobFatigueMultiplier", new object[] { ownedId, def, level, site }, out var res) && res is float f)
            return Mathf.Max(0f, f);
        return 1f;
    }

    // Job aura percent (sum across workers, as 0.10 = +10%)
    public static float GetJobAuraPercent(string ownedId, MonsterDataSO def, int level, JobType site)
    {
        if (TryInvoke("GetJobAuraPercent", new object[] { ownedId, def, level, site }, out var res) && res is float f)
            return Mathf.Max(0f, f);
        return 0f;
    }

    // Job capacity flat bonus (storage)
    public static int GetJobCapacityFlat(string ownedId, MonsterDataSO def, int level, JobType site)
    {
        if (TryInvoke("GetJobCapacityBonusFlat", new object[] { ownedId, def, level, site }, out var res) && res is int i)
            return Mathf.Max(0, i);
        return 0;
    }

    public static Dictionary<JobType, float> BuildJobAuras(System.Collections.IEnumerable teamEnumerable)
    {
        var result = new Dictionary<JobType, float>(16);
        // init to 0 for all jobs so TryGetValue always works
        foreach (JobType jt in Enum.GetValues(typeof(JobType)))
            if (!result.ContainsKey(jt)) result[jt] = 0f;

        if (teamEnumerable == null) return result;

        foreach (var entry in teamEnumerable)
        {
            // Try to read entry.monsterId and entry.level via reflection (robust to your save type)
            string id = ReadString(entry, "monsterId");
            if (string.IsNullOrEmpty(id)) continue;

            int level = ReadInt(entry, "level", 1);
            var def = MonsterLibraryLocator.GetById(id);
            if (!def) continue;

            foreach (JobType jt in Enum.GetValues(typeof(JobType)))
            {
                float aura = 0f;
                try { aura = Mathf.Max(0f, GetJobAuraPercent(id, def, level, jt)); } catch { aura = 0f; }
                if (aura > 0f) result[jt] += aura; // sum percent bonuses
            }
        }

        return result;

        // local helpers
        static string ReadString(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            var f = t.GetField(name);
            if (f != null && f.FieldType == typeof(string)) return (string)f.GetValue(obj);
            var p = t.GetProperty(name);
            if (p != null && p.PropertyType == typeof(string)) return (string)p.GetValue(obj, null);
            return null;
        }
        static int ReadInt(object obj, string name, int fallback)
        {
            if (obj == null) return fallback;
            var t = obj.GetType();
            var f = t.GetField(name);
            if (f != null) { try { return Convert.ToInt32(f.GetValue(obj)); } catch { } }
            var p = t.GetProperty(name);
            if (p != null) { try { return Convert.ToInt32(p.GetValue(obj, null)); } catch { } }
            return fallback;
        }
    }

    // Flat storage capacity bonus across the *team* for a specific site.
    public static int GetJobCapacityBonus(JobType site)
    {
        var team = SaveManager.Data?.team;
        if (team == null) return 0;

        int total = 0;
        foreach (var entry in team)
        {
            if (entry == null) continue;
            string id = null; int level = 1;
            try
            {
                var et = entry.GetType();
                id = (string)(et.GetField("monsterId")?.GetValue(entry) ?? et.GetProperty("monsterId")?.GetValue(entry, null));
                level = Convert.ToInt32(et.GetField("level")?.GetValue(entry) ?? et.GetProperty("level")?.GetValue(entry, null) ?? 1);
            }
            catch { id = null; level = 1; }
            if (string.IsNullOrEmpty(id)) continue;

            var def = MonsterLibraryLocator.GetById(id);
            if (!def) continue;

            try { total += Mathf.Max(0, GetJobCapacityFlat(id, def, level, site)); } catch { /* no-op */ }
        }
        return Mathf.Max(0, total);
    }

    public static TitleStatMods GetConditionalBattleMods(string id, float hpPct, int alliesAlive, int winStreak)
    {
        // Prefer a strongly-typed TitleContext ctor if you have it
        TitleContext ctx = new TitleContext(id, hpPct, alliesAlive, winStreak);

        // If your runtime exposes a direct conditional evaluator, call it:
        if (TryInvoke("GetConditionalBattleMods", new object[] { ctx }, out var res) && res is TitleStatMods tsm)
            return tsm;

        // Fallback: some runtimes may expose a generic router
        if (TryInvoke("GetConditionalBattleModsRouter", new object[] { id, hpPct, alliesAlive, winStreak }, out res) && res is TitleStatMods tsm2)
            return tsm2;

        // If no runtime handler, no-op
        return default;
    }

    // ----- Effectiveness (attacker-side); already present, keep as-is -----

    // ----- DamageFilter (typed) -----
    public static TitleDamageFilter GetDamageFilter(string ownedId, MonsterDataSO def, int level)
    {
        // Preferred: runtime returns a typed TitleDamageFilter
        if (TryInvoke("GetDamageFilter", new object[] { ownedId, def, level }, out var res))
        {
            if (res is TitleDamageFilter typed) return typed;

            // Graceful unbox if runtime returns an anonymous/boxed object with fields
            // (cannotBeCrit, percentReduce, flatReduce)
            try
            {
                var t    = res.GetType();
                var cbc  = t.GetField("cannotBeCrit")  ?.GetValue(res) as bool?  ?? false;
                var pr   = t.GetField("percentReduce")?.GetValue(res) as float? ?? 0f;
                var fr   = t.GetField("flatReduce")    ?.GetValue(res) as int?   ?? 0;
                return new TitleDamageFilter { cannotBeCrit = cbc, percentReduce = Mathf.Max(0f, pr), flatReduce = Mathf.Max(0, fr) };
            }
            catch { /* fall through */ }
        }

        return default;
    }



}
