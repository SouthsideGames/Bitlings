using System;
using System.Reflection;
using UnityEngine;
using System.Collections.Generic;

/// <summary> Flat/percent stat mods exposed by titles during battle. </summary>
public struct TitleStatMods
{
    public float hpPct;   // +% Max HP (e.g., 0.10 = +10%)
    public float atkPct;  // +% ATK
    public float defPct;  // +% DEF
    public float spdPct;  // +% SPD
    public int   atkFlat; // +flat ATK
    public int   defFlat; // +flat DEF
    public int   spdFlat; // +flat SPD
}

/// <summary> Defender-side incoming damage filters. </summary>
public struct TitleDamageFilter
{
    public bool  cannotBeCrit;   // true = incoming attacks cannot crit
    public float percentReduce;  // 0.15 = reduce 15% of incoming damage (POST-DEF)
    public int   flatReduce;     // flat soak (POST % reduce)
}

/// <summary>
/// Thin reflection bridge between battle/gameplay code and your Title runtime.
/// Looks for one of: TitleRuntime, TitleManager, TitlesManager.
/// Never constructs MonoBehaviours — relies on exposed singletons or scene search.
/// </summary>
public static class TitlesAdapter
{
    // Try these in order so you can rename your runtime later without touching callsites.
    private static readonly string[] CandidateTypes =
    {
        "TitleRuntime",
        "TitleManager",
        "TitlesManager"
    };

    private static Type   _titleType;
    private static object _titleSingleton; // cached instance (MonoBehaviour in scene OR static singleton)

    // Simple per-name MethodInfo cache to avoid repeated reflection lookups.
    private static readonly Dictionary<string, MethodInfo> _miCache = new Dictionary<string, MethodInfo>(32);
    private static bool _warnedMissingType = false;

    // ─────────────────────────────────────────────────────────────────────────────
    // Bootstrap
    // ─────────────────────────────────────────────────────────────────────────────

    static TitlesAdapter()
    {
        TryResolveType();
        // don’t resolve instance here; some singletons come alive later in boot.
    }

    private static void TryResolveType()
    {
        if (_titleType != null) return;

        foreach (var name in CandidateTypes)
        {
            _titleType = Type.GetType(name) ?? FindInAllAssemblies(name);
            if (_titleType != null) break;
        }

        if (_titleType == null && !_warnedMissingType)
        {
            _warnedMissingType = true;
            Debug.LogWarning("[TitlesAdapter] No Title runtime type found. Expected one of: TitleRuntime / TitleManager / TitlesManager. Calls will default.");
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
            var fI  = t.GetField("I",        BindingFlags.Public | BindingFlags.Static);
            var pI  = t.GetProperty("I",     BindingFlags.Public | BindingFlags.Static);
            var pIn = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

            return (object)(fI?.GetValue(null) ??
                            pI?.GetValue(null, null) ??
                            pIn?.GetValue(null, null));
        }
        catch { return null; }
    }

    /// <summary> Call this if you want to explicitly inject your title runtime instance at startup. </summary>
    public static void SetRuntime(object runtimeInstance)
    {
        if (runtimeInstance == null) return;
        _titleType = runtimeInstance.GetType();
        _titleSingleton = runtimeInstance;
        _miCache.Clear();
    }

    /// <summary> Ensure we have a scene instance (or a static singleton). Never constructs a MonoBehaviour. </summary>
    private static object ResolveSceneSingleton()
    {
        if (_titleType == null) { TryResolveType(); if (_titleType == null) return null; }

        // 1) Try static singletons again (late init)
        var inst = GetStaticSingleton(_titleType);
        if (inst != null) { _titleSingleton = inst; return inst; }

        // 2) Scene search
        try
        {
#if UNITY_2022_3_OR_NEWER
            var found = UnityEngine.Object.FindObjectsByType(_titleType, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (found != null && found.Length > 0) { _titleSingleton = found[0]; return _titleSingleton; }
#else
            var found = UnityEngine.Object.FindObjectsOfType(_titleType);
            if (found != null && found.Length > 0) { _titleSingleton = found[0]; return _titleSingleton; }
#endif
        }
        catch { /* ignore */ }

        // 3) One-time helpful warning
        Debug.LogWarning($"[TitlesAdapter] Could not find a '{_titleType?.Name}' instance in the scene, and no static singleton was exposed. Calls will default.");
        return null;
    }

    private static bool TryInvoke(string method, object[] args, out object result)
    {
        result = null;
        if (_titleType == null) { TryResolveType(); if (_titleType == null) return false; }

        if (!_miCache.TryGetValue(method, out var mi) || mi == null)
        {
            mi = _titleType.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            _miCache[method] = mi; // cache even null to avoid repeated lookups
        }
        if (mi == null) return false;

        object target = null;
        if (!mi.IsStatic)
        {
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
            Debug.LogWarning($"[TitlesAdapter] Invoke '{method}' failed: {e.Message}");
            return false;
        }
    }

    private static void WarnDefault(string apiName, string hint = null)
    {
        Debug.LogWarning($"[TitlesAdapter] {apiName} not implemented on title runtime — returning default. {hint ?? ""}");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Lifecycle / Events
    // ─────────────────────────────────────────────────────────────────────────────

    public static void OnBattleStart(string activeMonsterId, MonsterDataSO wild, int wildLevel)
    {
        if (!TryInvoke("OnBattleStart", new object[] { activeMonsterId, wild, wildLevel }, out _)){}
    }

    public static void OnBattleEnd(string activeMonsterId, bool victory, MonsterDataSO wild, int wildLevel)
    {
        if (!TryInvoke("OnBattleEnd", new object[] { activeMonsterId, victory, wild, wildLevel }, out _)){}
    }

    public static void OnMonsterLeveled(string monsterId, int newLevel)
    {
        if (!TryInvoke("OnMonsterLeveled", new object[] { monsterId, newLevel }, out _)){}
    }

    public static void OnMonsterCaptured(string monsterId, MonsterType type, int level, bool isShiny)
    {
        if (!TryInvoke("OnMonsterCaptured", new object[] { monsterId, type, level, isShiny }, out _)){}
    }

    public static void OnMonsterEvolved(string newMonsterId)
    {
        if (!TryInvoke("OnMonsterEvolved", new object[] { newMonsterId }, out _)){}
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Battle-time stat mods
    // ─────────────────────────────────────────────────────────────────────────────

    public static TitleStatMods GetBattleStatMods(string monsterId)
    {
        if (TryInvoke("GetBattleStatMods", new object[] { monsterId }, out var res) && res is TitleStatMods tsm)
            return tsm;

        return default;
    }

    public static float GetStatValue(string ownedId, MonsterDataSO def, int level, string statKind, TitleContext ctx, float baseValue)
    {
        if (TryInvoke("GetStatValueRouter", new object[] { ownedId, def, level, statKind, ctx, baseValue }, out var res) && res is float f)
            return f;

        WarnDefault("GetStatValueRouter", "Implement GetStatValueRouter(ownedId, def, level, kind, ctx, baseValue).");
        return baseValue;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Multipliers (victory/capture/jobs)
    // ─────────────────────────────────────────────────────────────────────────────

    public static float GetCoinMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        if (TryInvoke("GetCoinMultOnVictory", new object[] { monsterId, wild, wildLevel }, out var res) && res is float f)
            return Mathf.Max(0f, f);

        WarnDefault("GetCoinMultOnVictory", "Provide coin victory multiplier or return 1.");
        return 1f;
    }

    public static float GetGrowthCoreMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        if (TryInvoke("GetGrowthCoreMultOnVictory", new object[] { monsterId, wild, wildLevel }, out var res) && res is float f)
            return Mathf.Max(0f, f);

        WarnDefault("GetGrowthCoreMultOnVictory", "Provide Growth Core victory multiplier or return 1.");
        return 1f;
    }

    public static float GetCaptureChanceMult(string leadMonsterId)
    {
        if (TryInvoke("GetCaptureChanceMult", new object[] { leadMonsterId }, out var res) && res is float f)
            return Mathf.Max(0f, f);

        return 1f;
    }

    public static float GetJobRateMult(string workerOwnedOrDefId, JobType site)
    {
        if (TryInvoke("GetJobRateMult", new object[] { workerOwnedOrDefId, site }, out var res) && res is float f)
            return Mathf.Max(0f, f);

        return 1f;
    }

    public static float GetJobFatigueMult(string ownedId, MonsterDataSO def, int level, JobType site)
    {
        if (TryInvoke("GetJobFatigueMultiplier", new object[] { ownedId, def, level, site }, out var res) && res is float f)
            return Mathf.Max(0f, f);

        return 1f;
    }

    public static float GetJobAuraPercent(string ownedId, MonsterDataSO def, int level, JobType site)
    {
        if (TryInvoke("GetJobAuraPercent", new object[] { ownedId, def, level, site }, out var res) && res is float f)
            return Mathf.Max(0f, f);

        return 0f;
    }

    public static int GetJobCapacityFlat(string ownedId, MonsterDataSO def, int level, JobType site)
    {
        if (TryInvoke("GetJobCapacityBonusFlat", new object[] { ownedId, def, level, site }, out var res) && res is int i)
            return Mathf.Max(0, i);

        return 0;
    }

    /// <summary> Build team-wide auras (sum % per site). </summary>
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

    /// <summary> Sum of flat capacity bonuses across the active team for a specific job site. </summary>
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
                id    = (string)(et.GetField("monsterId")?.GetValue(entry) ?? et.GetProperty("monsterId")?.GetValue(entry, null));
                level = Convert.ToInt32(et.GetField("level")?.GetValue(entry) ?? et.GetProperty("level")?.GetValue(entry, null) ?? 1);
            }
            catch { id = null; level = 1; }

            if (string.IsNullOrEmpty(id)) continue;

            var def = MonsterLibraryLocator.GetById(id);
            if (!def) continue;

            try { total += Mathf.Max(0, GetJobCapacityFlat(id, def, level, site)); } catch { }
        }

        return Mathf.Max(0, total);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Effectiveness (attacker & defender)
    // ─────────────────────────────────────────────────────────────────────────────

    public static float GetEffectivenessMult(string ownedId, MonsterDataSO def, int level)
    {
        if (TryInvoke("GetEffectivenessMultiplier", new object[] { ownedId, def, level }, out var res) && res is float f)
            return Mathf.Max(0f, f);

        // optional feature; default neutral
        return 1f;
    }

    public static float GetEffectivenessAdd(string ownedId, MonsterDataSO def, int level)
    {
        if (TryInvoke("GetEffectivenessAdd", new object[] { ownedId, def, level }, out var res) && res is float f)
            return f;

        return 0f;
    }

    public static float GetIncomingEffectivenessMult(string ownedId, MonsterDataSO def, int level)
    {
        if (TryInvoke("GetIncomingEffectivenessMultiplier", new object[] { ownedId, def, level }, out var res) && res is float f)
            return Mathf.Max(0f, f);

        return 1f;
    }

    /// <summary> Defender-side damage filter: cannotBeCrit / % reduce / flat reduce. </summary>
    public static TitleDamageFilter GetDamageFilter(string ownedId, MonsterDataSO def, int level)
    {
        if (TryInvoke("GetDamageFilter", new object[] { ownedId, def, level }, out var res))
        {
            if (res is TitleDamageFilter typed) return typed;

            try
            {
                var t  = res.GetType();
                bool  cbc = false;
                float pr  = 0f;
                int   fr  = 0;

                var f1 = t.GetField("cannotBeCrit"); var p1 = t.GetProperty("cannotBeCrit");
                var f2 = t.GetField("percentReduce"); var p2 = t.GetProperty("percentReduce");
                var f3 = t.GetField("flatReduce"); var p3 = t.GetProperty("flatReduce");

                if (f1 != null) cbc = (bool)(f1.GetValue(res) ?? false);
                else if (p1 != null) cbc = (bool)(p1.GetValue(res, null) ?? false);

                if (f2 != null) pr = Convert.ToSingle(f2.GetValue(res) ?? 0f);
                else if (p2 != null) pr = Convert.ToSingle(p2.GetValue(res, null) ?? 0f);

                if (f3 != null) fr = Convert.ToInt32(f3.GetValue(res) ?? 0);
                else if (p3 != null) fr = Convert.ToInt32(p3.GetValue(res, null) ?? 0);

                return new TitleDamageFilter
                {
                    cannotBeCrit  = cbc,
                    percentReduce = Mathf.Clamp01(pr),
                    flatReduce    = Mathf.Max(0, fr)
                };
            }
            catch { /* fall through */ }
        }

        return default;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Conditional mods (direct)
    // ─────────────────────────────────────────────────────────────────────────────

    public static TitleStatMods GetConditionalBattleMods(string id, float hpPct, int alliesAlive, int winStreak)
    {
        var ctx = new TitleContext(id, hpPct, alliesAlive, winStreak);

        if (TryInvoke("GetConditionalBattleMods", new object[] { ctx }, out var res) && res is TitleStatMods tsm)
            return tsm;

        if (TryInvoke("GetConditionalBattleModsRouter", new object[] { id, hpPct, alliesAlive, winStreak }, out res) && res is TitleStatMods tsm2)
            return tsm2;

        return default;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Global victory multipliers (optional)
    // ─────────────────────────────────────────────────────────────────────────────

    public static float GetVictoryCoinMult()
    {
        if (TryInvoke("GetVictoryCoinMultiplier", Array.Empty<object>(), out var res) && res is float f)
            return Mathf.Max(0f, f);
        return 1f;
    }

    public static float GetVictoryXPMult()
    {
        if (TryInvoke("GetVictoryXPMultiplier", Array.Empty<object>(), out var res) && res is float f)
            return Mathf.Max(0f, f);
        return 1f;
    }
}
