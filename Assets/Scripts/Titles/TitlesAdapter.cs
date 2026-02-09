// Assets/Scripts/Titles/TitlesAdapter.cs
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
/// 
/// Also supports local, battle-scoped title injection (e.g., wild titles rolled per encounter)
/// without touching any save/equip pathways.
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
    // Local override layer (battle-scoped titles)
    // ─────────────────────────────────────────────────────────────────────────────

    // Key: combatant id (owned id or synthetic id like "WILD::<...>")
    // Value: active titles list to use for adapter fallback scanning.
    private static readonly Dictionary<string, List<TitleSO>> _localTitlesById =
        new Dictionary<string, List<TitleSO>>(StringComparer.Ordinal);

    /// <summary>
    /// Inject battle-scoped titles for a given id (e.g., wild combat id).
    /// These titles are used by adapter fallbacks that scan titles locally (job fatigue, type resist, etc.)
    /// and allow wild titles to function without any save/equip calls.
    /// </summary>
    public static void SetLocalTitles(string id, IEnumerable<TitleSO> titles)
    {
        if (string.IsNullOrEmpty(id))
            return;

        if (!_localTitlesById.TryGetValue(id, out var list) || list == null)
        {
            list = new List<TitleSO>(8);
            _localTitlesById[id] = list;
        }
        else
        {
            list.Clear();
        }

        if (titles == null) return;

        foreach (var t in titles)
        {
            if (t == null) continue;
            list.Add(t);
        }
    }

    /// <summary> Remove any injected titles for a specific id. </summary>
    public static void ClearLocalTitles(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _localTitlesById.Remove(id);
    }

    /// <summary> Clears all injected titles (safe to call at end of battle). </summary>
    public static void ClearAllLocalTitles()
    {
        _localTitlesById.Clear();
    }

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

    public static void OnTurnAdvanced(int turnIndex)
    {
        if (!TryInvoke("OnTurnAdvanced", new object[] { turnIndex }, out _)) { }
    }

    public static void OnAttackLanded(string attackerId, bool wasCrit)
    {
        if (!TryInvoke("OnAttackLanded", new object[] { attackerId, wasCrit }, out _)) { }
    }

    public static void OnHitTaken(string defenderId, int damage, bool wasCrit)
    {
        if (!TryInvoke("OnHitTaken", new object[] { defenderId, damage, wasCrit }, out _)) { }
    }

    
    public static float GetBattleStartShieldRemaining(string monsterId)
    {
        if (!TryInvoke("GetBattleStartShieldRemaining", new object[] { monsterId }, out object ret))
            return 0f;

        try
        {
            if (ret is float f) return f;
            if (ret is int i) return i;
            return Convert.ToSingle(ret);
        }
        catch { return 0f; }
    }

public static void OnBattleStart(string activeMonsterId, MonsterDataSO wild, int wildLevel)
    {
        if (!TryInvoke("OnBattleStart", new object[] { activeMonsterId, wild, wildLevel }, out _)) { }
    }

    public static void OnBattleEnd(string activeMonsterId, bool victory, MonsterDataSO wild, int wildLevel)
    {
        if (!TryInvoke("OnBattleEnd", new object[] { activeMonsterId, victory, wild, wildLevel }, out _)) { }
    }

    public static void OnMonsterLeveled(string monsterId, int newLevel)
    {
        if (!TryInvoke("OnMonsterLeveled", new object[] { monsterId, newLevel }, out _)) { }
    }

    public static void OnMonsterCaptured(string monsterId, MonsterType type, int level, bool isShiny)
    {
        if (!TryInvoke("OnMonsterCaptured", new object[] { monsterId, type, level, isShiny }, out _)) { }
    }

    public static void OnMonsterEvolved(string newMonsterId)
    {
        if (!TryInvoke("OnMonsterEvolved", new object[] { newMonsterId }, out _)) { }
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

    public static float GetcreditMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        if (TryInvoke("GetcreditMultOnVictory", new object[] { monsterId, wild, wildLevel }, out var res) && res is float f)
            return Mathf.Max(0f, f);

        WarnDefault("GetcreditMultOnVictory", "Provide credit victory multiplier or return 1.");
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

    /// <summary>
    /// Job fatigue multiplier while assigned to a specific site.
    /// Prefers runtime’s GetJobFatigueMultiplier(ownedId, def, level, site); falls back to local per-title scan.
    /// </summary>
    public static float GetJobFatigueMult(string ownedId, MonsterDataSO def, int level, JobType site)
    {
        // Preferred: ask the runtime directly if it implements a site-aware API.
        if (TryInvoke("GetJobFatigueMultiplier", new object[] { ownedId, def, level, site }, out var res) && res is float f)
            return Mathf.Max(0f, f);

        // Fallback: local logic using JobFatigueBoosterTitleSO + AppliesTo(site)
        float mult = 1f;

        var titles = GetTitles(ownedId);
        if (titles == null) return mult;

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (t is JobFatigueBoosterTitleSO ft && ft.AppliesTo(site))
            {
                mult *= Mathf.Max(0f, ft.fatigueMultiplier);
            }
        }

        return float.IsFinite(mult) ? Mathf.Max(0f, mult) : 1f;
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
        // Initialize result with all job types present (0% default)
        var result = new Dictionary<JobType, float>(16);
        foreach (JobType jt in Enum.GetValues(typeof(JobType)))
            if (!result.ContainsKey(jt)) result[jt] = 0f;

        // Prefer authoritative source of "who is working where" → JobManager assignments.
        var jm = JobManager.I;
        if (jm != null && jm.States != null && jm.States.Count > 0)
        {
            // Quick helper: get a monster's level from team (if present) else 1
            int GetLevelFromTeam(string ownedId)
            {
                if (string.IsNullOrEmpty(ownedId) || teamEnumerable == null) return 1;

                foreach (var entry in teamEnumerable)
                {
                    var et = entry.GetType();
                    string mid = null;
                    int lvl = 1;
                    try
                    {
                        mid = (string)(et.GetField("monsterId")?.GetValue(entry) ??
                                       et.GetProperty("monsterId")?.GetValue(entry, null));
                        var raw = et.GetField("level")?.GetValue(entry) ??
                                  et.GetProperty("level")?.GetValue(entry, null) ?? 1;
                        lvl = Convert.ToInt32(raw);
                    }
                    catch { /* keep defaults */ }

                    if (!string.IsNullOrEmpty(mid) && mid == ownedId) return Mathf.Max(1, lvl);
                }
                return 1;
            }

            foreach (var st in jm.States)
            {
                if (st?.config == null || st.workers == null) continue;

                var job = st.config.jobType;
                for (int i = 0; i < st.workers.Count; i++)
                {
                    var w = st.workers[i];
                    if (w == null) continue;

                    // Prefer owned-instance id; fallback to base def id
                    string id = !string.IsNullOrEmpty(w.monsterId) ? w.monsterId : (w.def ? w.def.id : null);
                    if (string.IsNullOrEmpty(id)) continue;

                    var def = w.def ?? MonsterLibraryLocator.GetById(id);
                    if (!def) continue;

                    int level = GetLevelFromTeam(id);

                    float aura = 0f;
                    try { aura = Mathf.Max(0f, GetJobAuraPercent(id, def, level, job)); } catch { aura = 0f; }
                    if (aura > 0f) result[job] += aura;
                }
            }

            return result; // done — assignment-aware path
        }

        // Fallback: if JobManager not ready, keep previous behavior (scan team for all sites).
        if (teamEnumerable == null) return result;

        foreach (var entry in teamEnumerable)
        {
            string id = null; int level = 1;
            try
            {
                var et = entry.GetType();
                id    = (string)(et.GetField("monsterId")?.GetValue(entry) ??
                                 et.GetProperty("monsterId")?.GetValue(entry, null));
                var raw = et.GetField("level")?.GetValue(entry) ??
                          et.GetProperty("level")?.GetValue(entry, null) ?? 1;
                level = Convert.ToInt32(raw);
            }
            catch { id = null; level = 1; }
            if (string.IsNullOrEmpty(id)) continue;

            var def = MonsterLibraryLocator.GetById(id);
            if (!def) continue;

            // Note: this path adds aura to every site (legacy behavior) until JobManager is available.
            foreach (JobType jt in Enum.GetValues(typeof(JobType)))
            {
                float aura = 0f;
                try { aura = Mathf.Max(0f, GetJobAuraPercent(id, def, level, jt)); } catch { aura = 0f; }
                if (aura > 0f) result[jt] += aura;
            }
        }

        return result;
    }

    /// <summary> Sum of flat capacity bonuses across the active team for a specific job site. </summary>
    public static int GetJobCapacityBonus(JobType site)
    {
        int bonus = 0;

        var jm = JobManager.I;
        if (jm == null || jm.States == null) return 0;

        // Find the site and sum bonuses from workers actually assigned there
        for (int si = 0; si < jm.States.Count; si++)
        {
            var st = jm.States[si];
            if (st?.config == null || st.config.jobType != site) continue;

            var workers = st.workers;
            if (workers == null) break;

            for (int wi = 0; wi < workers.Count; wi++)
            {
                var w = workers[wi];
                if (w == null) continue;

                string id = !string.IsNullOrEmpty(w.monsterId) ? w.monsterId : (w.def ? w.def.id : null);
                if (string.IsNullOrEmpty(id)) continue;

                var titles = GetTitles(id); // ← reflection-runtime accessor OR local override
                if (titles == null) continue;

                for (int ti = 0; ti < titles.Count; ti++)
                {
                    if (titles[ti] is JobCapacityBoosterTitleSO cap && cap.AppliesTo(site))
                    {
                        bonus += Mathf.Max(0, cap.capacityBonusFlat);
                    }
                }
            }

            break; // site found/processed
        }

        return Mathf.Max(0, bonus);
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

    /// <summary>
    /// Incoming effectiveness multiplier, typed (defender perspective).
    /// Prefers runtime’s GetIncomingEffectivenessMult(ownedId, def, level, incomingType).
    /// Falls back to legacy behavior using TitleManager if needed.
    /// </summary>
    public static float GetIncomingEffectivenessMult(string ownedId, MonsterDataSO def, int level, MonsterType incomingType)
    {
        // Preferred: ask the runtime directly if the new API exists.
        if (TryInvoke("GetIncomingEffectivenessMult", new object[] { ownedId, def, level, incomingType }, out var res) && res is float f)
            return Mathf.Max(0f, f);

        // Fallback: legacy manual path (generic defensive multiplier + TypeResistTitleSO)
        float mul = 1f;

        // 1) Generic defensive effectiveness titles (nullifiers/resistors)
        if (TitleManager.I != null)
            mul *= Mathf.Max(0f, TitleManager.I.GetIncomingEffectivenessMultiplier(ownedId, def, level));

        // If no type passed, we’re done.
        if (incomingType == MonsterType.None)
            return Mathf.Max(0f, mul);

        // 2) Per-type resist titles that match the incoming type
        // NOTE: This path uses the equip list. If you want wild combatants to support this without
        // touching equip/save logic, ensure your runtime implements GetIncomingEffectivenessMult
        // OR ensure callers pass ids that are satisfied via local override + runtime route.
        if (TitleManager.I != null)
        {
            var list = TitleManager.I.GetEquippedList(ownedId, def, level);
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is TypeResistTitleSO tr && tr.resistTypes != null && tr.resistTypes.Length > 0)
                    {
                        for (int k = 0; k < tr.resistTypes.Length; k++)
                        {
                            if (tr.resistTypes[k] == incomingType)
                            {
                                mul *= Mathf.Max(0f, tr.incomingMultiplier);
                                break; // avoid double-counting same asset
                            }
                        }
                    }
                }
            }
        }

        return Mathf.Max(0f, mul);
    }

    /// <summary>
    /// Backward-compatible alias for any old callsites that didn’t pass a type.
    /// Prefers runtime’s GetIncomingEffectivenessMultiplier(ownedId, def, level) if available.
    /// </summary>
    public static float GetIncomingEffectivenessMult(string ownedId, MonsterDataSO def, int level)
    {
        // Preferred: ask via reflection
        if (TryInvoke("GetIncomingEffectivenessMultiplier", new object[] { ownedId, def, level }, out var res) && res is float f)
            return Mathf.Max(0f, f);

        // Fallback: if TitleManager singleton exists, use its generic defensive multiplier.
        if (TitleManager.I != null)
            return Mathf.Max(0f, TitleManager.I.GetIncomingEffectivenessMultiplier(ownedId, def, level));

        // No implementation — neutral
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

    public static float GetVictorycreditMult()
    {
        if (TryInvoke("GetVictorycreditMultiplier", Array.Empty<object>(), out var res) && res is float f)
            return Mathf.Max(0f, f);
        return 1f;
    }

    public static float GetVictoryXPMult()
    {
        if (TryInvoke("GetVictoryXPMultiplier", Array.Empty<object>(), out var res) && res is float f)
            return Mathf.Max(0f, f);
        return 1f;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helper: pull titles for a monster via runtime bridge OR local override
    // ─────────────────────────────────────────────────────────────────────────────
    private static List<TitleSO> GetTitles(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId))
            return new List<TitleSO>();

        // 0) Local override (battle-scoped injection, e.g., wild titles)
        if (_localTitlesById.TryGetValue(monsterId, out var local) && local != null)
            return local;

        // 1) Try to call runtime method (reflection bridge)
        if (TryInvoke("GetTitlesForMonster", new object[] { monsterId }, out var res))
        {
            if (res is List<TitleSO> list)
                return list;
            if (res is IEnumerable<TitleSO> enumerable)
                return new List<TitleSO>(enumerable);
        }

        // Nothing returned — safe default
        return new List<TitleSO>();
    }
}
