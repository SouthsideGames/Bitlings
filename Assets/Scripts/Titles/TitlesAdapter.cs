using System;
using System.Reflection;
using UnityEngine;
using System.Collections.Generic;

public struct TitleStatMods
{
    public float hpPct;   // +% Max HP (e.g., 0.10 = +10%)
    public float atkPct;  // +% ATK
    public float defPct;  // +% DEF
    public float spdPct;  // +% SPD
    public int atkFlat;   // +flat ATK (optional)
    public int defFlat;   // +flat DEF (optional)
    public int spdFlat;   // +flat SPD (optional)
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
    static object _titleSingleton; // cached scene instance (MonoBehaviour) or runtime singleton

    static TitlesAdapter()
    {
        foreach (var name in CandidateTypes)
        {
            _titleType = Type.GetType(name) ?? FindInAllAssemblies(name);
            if (_titleType != null)
            {
                // Try common singleton patterns: public static I / Instance
                _titleSingleton = GetStaticSingleton(_titleType);
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
                var t = asms[i].GetType(typeName, throwOnError: false);
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    private static object GetStaticSingleton(Type t)
    {
        try
        {
            var fI = t.GetField("I", BindingFlags.Public | BindingFlags.Static);
            var pI = t.GetProperty("I", BindingFlags.Public | BindingFlags.Static);
            var pIn = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

            return (object)(fI?.GetValue(null) ??
                            pI?.GetValue(null, null) ??
                            pIn?.GetValue(null, null));
        }
        catch { return null; }
    }

    /// <summary>
    /// Ensure we have a reference to the **scene** instance of the title runtime.
    /// Never constructs a MonoBehaviour. Returns null and logs if not found.
    /// </summary>
    private static object ResolveSceneSingleton()
    {
        // 1) Re-check static singleton fields/properties in case they were set after static ctor
        var inst = GetStaticSingleton(_titleType);
        if (inst != null) { _titleSingleton = inst; return inst; }

        // 2) Search scene for a component of that type
        try
        {
            // Non-generic overload returns UnityEngine.Object[]
#if UNITY_2022_3_OR_NEWER
            var found = UnityEngine.Object.FindObjectsByType(_titleType, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (found != null && found.Length > 0) { _titleSingleton = found[0]; return _titleSingleton; }
#else
            var found = UnityEngine.Object.FindObjectsOfType(_titleType);
            if (found != null && found.Length > 0) { _titleSingleton = found[0]; return _titleSingleton; }
#endif
        }
        catch { /* ignore and fall through */ }

        Debug.LogError($"[TitlesAdapter] Could not locate a scene instance of '{_titleType?.Name}'. " +
                       $"Add a GameObject with '{_titleType?.Name}' attached, or expose a public static singleton (I/Instance).");
        return null;
    }

    private static bool TryInvoke(string method, object[] args, out object result)
    {
        result = null;
        if (_titleType == null) return false;

        var mi = _titleType.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        if (mi == null) return false;

        object target = null;

        if (!mi.IsStatic)
        {
            // IMPORTANT: Never new/Activator.CreateInstance a MonoBehaviour!
            // Use cached singleton if any; else resolve from scene.
            target = _titleSingleton ?? ResolveSceneSingleton();
            if (target == null) return false;
        }

        try
        {
            result = mi.Invoke(target, args);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TitlesAdapter] {method} failed: {e.Message}");
            return false;
        }
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
        foreach (JobType jt in Enum.GetValues(typeof(JobType)))
            if (!result.ContainsKey(jt)) result[jt] = 0f;

        if (teamEnumerable == null) return result;

        foreach (var entry in teamEnumerable)
        {
            string id = ReadString(entry, "monsterId");
            if (string.IsNullOrEmpty(id)) continue;

            int level = ReadInt(entry, "level", 1);
            var def = MonsterLibraryLocator.GetById(id);
            if (!def) continue;

            foreach (JobType jt in Enum.GetValues(typeof(JobType)))
            {
                float aura = 0f;
                try { aura = Mathf.Max(0f, GetJobAuraPercent(id, def, level, jt)); } catch { aura = 0f; }
                if (aura > 0f) result[jt] += aura;
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
        TitleContext ctx = new TitleContext(id, hpPct, alliesAlive, winStreak);

        if (TryInvoke("GetConditionalBattleMods", new object[] { ctx }, out var res) && res is TitleStatMods tsm)
            return tsm;

        if (TryInvoke("GetConditionalBattleModsRouter", new object[] { id, hpPct, alliesAlive, winStreak }, out res) && res is TitleStatMods tsm2)
            return tsm2;

        return default;
    }

    public static TitleDamageFilter GetDamageFilter(string ownedId, MonsterDataSO def, int level)
    {
        if (TryInvoke("GetDamageFilter", new object[] { ownedId, def, level }, out var res))
        {
            if (res is TitleDamageFilter typed) return typed;

            try
            {
                var t = res.GetType();
                var cbc = t.GetField("cannotBeCrit")?.GetValue(res) as bool? ?? false;
                var pr = t.GetField("percentReduce")?.GetValue(res) as float? ?? 0f;
                var fr = t.GetField("flatReduce")?.GetValue(res) as int? ?? 0;
                return new TitleDamageFilter { cannotBeCrit = cbc, percentReduce = Mathf.Max(0f, pr), flatReduce = Mathf.Max(0, fr) };
            }
            catch { /* fall through */ }
        }

        return default;
    }
    
    // Defender-side type effectiveness multiplier
    public static float GetIncomingEffectivenessMult(string ownedId, MonsterDataSO def, int level)
    {
        if (TryInvoke("GetIncomingEffectivenessMultiplier", new object[] { ownedId, def, level }, out var res) && res is float f)
            return Mathf.Max(0f, f);
        return 1f;
    }
}
