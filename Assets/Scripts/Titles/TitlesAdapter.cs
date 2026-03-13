// Assets/Scripts/Titles/TitlesAdapter.cs
using System;
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
/// Direct bridge between battle/gameplay code and TitleManager.
/// No reflection; preserves the existing adapter API so BattleManager does not change.
/// Also supports local, battle-scoped title injection (e.g., wild titles rolled per encounter)
/// without touching any save/equip pathways.
/// </summary>
public static class TitlesAdapter
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Local override layer (battle-scoped titles)
    // ─────────────────────────────────────────────────────────────────────────────
    // Key: combatant id (owned id or synthetic id like "WILD::<...>")
    private static readonly Dictionary<string, List<TitleSO>> _localTitlesById =
        new Dictionary<string, List<TitleSO>>(StringComparer.Ordinal);

    public static void SetLocalTitles(string id, IEnumerable<TitleSO> titles)
    {
        if (string.IsNullOrEmpty(id))
            return;

        if (!_localTitlesById.TryGetValue(id, out var list) || list == null)
        {
            list = new List<TitleSO>(8);
            _localTitlesById[id] = list;
        }
        else list.Clear();

        if (titles == null) return;
        foreach (var t in titles)
        {
            if (t == null) continue;
            list.Add(t);
        }

        // Mirror into TitleManager so the main evaluation path (GetEquippedList / GetStatValue / ApplyBattleStartBonuses)
        // can see rolled wild titles when the combatant id is synthetic (e.g., WILD::...).
        var rt = Runtime;
        if (rt != null)
            rt.SetBattleOverrideTitles(id, list);
    }

    public static void ClearLocalTitles(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _localTitlesById.Remove(id);

        var rt = Runtime;
        if (rt != null)
            rt.ClearBattleOverrideTitles(id);
    }

    public static void ClearAllLocalTitles()
    {
        _localTitlesById.Clear();
        var rt = Runtime;
        if (rt != null)
            rt.ClearAllBattleOverrideTitles();
    }

    public static void RegisterBattleContext(string combatantId, MonsterDataSO def, int level)
    {
        var rt = Runtime;
        if (rt == null) return;
        rt.RegisterBattleContextPublic(combatantId, def, level);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Runtime access
    // ─────────────────────────────────────────────────────────────────────────────
    private static TitleManager Runtime => TitleManager.I;

    private static List<TitleSO> GetTitles(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId))
            return new List<TitleSO>();

        // 0) Local override (battle-scoped injection, e.g., wild titles)
        if (_localTitlesById.TryGetValue(monsterId, out var local) && local != null)
            return local;

        var rt = Runtime;
        if (rt == null)
            return new List<TitleSO>();

        return rt.GetTitlesForMonster(monsterId);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Turn hooks (per-combatant)
    // ─────────────────────────────────────────────────────────────────────────────
    public static void OnCombatantTurnEnded(string combatantId)
    {
        var rt = Runtime;
        if (rt == null || string.IsNullOrEmpty(combatantId)) return;
        rt.OnCombatantTurnEnded(combatantId);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Lifecycle / Events
    // ─────────────────────────────────────────────────────────────────────────────

    public static void OnTurnAdvanced(int turnIndex)
    {
        var rt = Runtime;
        if (rt == null) return;
        rt.OnTurnAdvanced(turnIndex);
    }

    public static void OnAttackLanded(string attackerId, bool wasCrit)
    {
        var rt = Runtime;
        if (rt == null) return;
        rt.OnAttackLanded(attackerId, wasCrit);
    }

    public static void OnHitTaken(string defenderId, int damage, bool wasCrit)
    {
        var rt = Runtime;
        if (rt == null) return;
        rt.OnHitTaken(defenderId, damage, wasCrit);
    }

    public static float GetBattleStartShieldRemaining(string monsterId)
    {
        var rt = Runtime;
        if (rt == null) return 0f;
        return Mathf.Max(0f, rt.GetBattleStartShieldRemaining(monsterId));
    }

    public static void OnBattleStart(string activeMonsterId, MonsterDataSO wild, int wildLevel)
    {
        var rt = Runtime;
        if (rt == null) return;
        rt.OnBattleStart(activeMonsterId, wild, wildLevel);
    }

    public static void OnBattleEnd(string activeMonsterId, bool victory, MonsterDataSO wild, int wildLevel)
    {
        var rt = Runtime;
        if (rt == null) return;
        rt.OnBattleEnd(activeMonsterId, victory, wild, wildLevel);
    }

    public static void OnMonsterLeveled(string monsterId, int newLevel)
    {
        var rt = Runtime;
        if (rt == null) return;
        rt.OnMonsterLeveled(monsterId, newLevel);
    }

    public static void OnMonsterCaptured(string monsterId, MonsterType type, int level, bool isShiny)
    {
        var rt = Runtime;
        if (rt == null) return;
        rt.OnMonsterCaptured(monsterId, type, level, isShiny);
    }

    public static void OnMonsterEvolved(string newMonsterId)
    {
        var rt = Runtime;
        if (rt == null) return;
        rt.OnMonsterEvolved(newMonsterId);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Battle stat mods + stat values
    // ─────────────────────────────────────────────────────────────────────────────

    public static TitleStatMods GetBattleStatMods(string monsterId)
    {
        var rt = Runtime;
        if (rt == null) return default;
        return rt.GetBattleStatMods(monsterId);
    }

    public static float GetStatValue(string ownedId, MonsterDataSO def, int level, string statKind, TitleContext ctx, float baseValue)
    {
        var rt = Runtime;
        if (rt == null) return baseValue;
        return rt.GetStatValueRouter(ownedId, def, level, statKind, ctx, baseValue);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // DEV/Editor debug helpers (used by Battle UI overlays)
    // ─────────────────────────────────────────────────────────────────────────────
    public static int Debug_GetTurnBoosterStacks(string monsterId)
    {
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        var rt = Runtime;
        if (rt == null) return 0;
        return rt.Debug_GetTurnBoosterStacks(monsterId);
        #else
        return 0;
        #endif
    }

    public static string Debug_GetActiveBattleMonsterId()
    {
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        var rt = Runtime;
        return rt != null ? rt.ActiveBattleMonsterId : "";
        #else
        return "";
        #endif
    }

    public static int Debug_GetTurnIndex()
    {
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        var rt = Runtime;
        return rt != null ? rt.CurrentTurnIndex : 0;
        #else
        return 0;
        #endif
    }

    public static float GetCreditMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        var rt = Runtime;
        if (rt == null) return 1f;

        var titles = GetTitles(monsterId);
        if (titles == null || titles.Count == 0)
        {
            DevLog.Log($"[TitlesAdapter] Equipped for {monsterId}: none");
        }
        else
        {
            string list = "";
            for (int i = 0; i < titles.Count; i++)
            {
                var t = titles[i];
                if (i > 0) list += ",";
                if (t == null) list += "null";
                else
                {
                    string tid = string.IsNullOrEmpty(t.titleId) ? "<no-id>" : t.titleId;
                    string typ = t.GetType().Name;
                    list += $"{tid}({typ})";
                }
            }
            DevLog.Log($"[TitlesAdapter] Equipped for {monsterId}: {list}");
        }

        float mult = rt.GetcreditMultOnVictory(monsterId, wild, wildLevel);

        if (Mathf.Approximately(mult, 1f) && titles != null)
        {
            for (int i = 0; i < titles.Count; i++)
            {
                var t = titles[i] as CreditBonusOnVictoryTitleSO;
                if (t == null) continue;
                try
                {
                    var ty = t.GetType();
                    var f = ty.GetField("CreditMultiplier");
                    if (f != null)
                    {
                        var val = Convert.ToSingle(f.GetValue(t));
                        DevLog.Log($"[TitlesAdapter] Direct read CreditMultiplier for {monsterId} => {val}");
                        return Mathf.Max(0f, val);
                    }
                    var p = ty.GetProperty("CreditMultiplier");
                    if (p != null)
                    {
                        var val = Convert.ToSingle(p.GetValue(t, null));
                        DevLog.Log($"[TitlesAdapter] Direct read CreditMultiplier(prop) for {monsterId} => {val}");
                        return Mathf.Max(0f, val);
                    }
                }
                catch (Exception ex)
                {
                    DevLog.Log($"[TitlesAdapter] Failed direct read of CreditMultiplier: {ex.Message}");
                }
            }
        }

        return Mathf.Max(0f, mult);
    }

    public static float GetGrowthCoreMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        var rt = Runtime;
        if (rt == null) return 1f;
        return Mathf.Max(0f, rt.GetGrowthCoreMultOnVictory(monsterId, wild, wildLevel));
    }

    public static float GetCaptureChanceMult(string leadMonsterId)
    {
        var rt = Runtime;
        if (rt == null) return 1f;
        return 1f;
    }

    public static float GetJobRateMult(string workerOwnedOrDefId, JobType site)
    {
        var rt = Runtime;
        if (rt == null) return 1f;
        return Mathf.Max(0f, rt.GetJobRateMult(workerOwnedOrDefId, site));
    }


    public static float GetJobFatigueMult(string ownedId, MonsterDataSO def, int level, JobType site)
    {
        var rt = Runtime;
        if (rt == null) return 1f;
        return Mathf.Max(0f, rt.GetJobFatigueMultiplier(ownedId, def, level, site));
    }

    public static float GetJobAuraPercent(string ownedId, MonsterDataSO def, int level, JobType site)
    {
        var rt = Runtime;
        if (rt == null) return 0f;
        return rt.GetJobAuraPercent(ownedId, def, level, site);
    }

    public static int GetJobCapacityFlat(string ownedId, MonsterDataSO def, int level, JobType site)
    {
        var rt = Runtime;
        if (rt == null) return 0;
        return rt.GetJobCapacityBonusFlat(ownedId, def, level, site);
    }

    public static Dictionary<JobType, float> BuildJobAuras(System.Collections.IEnumerable teamEnumerable)
    {
        var dict = new Dictionary<JobType, float>();
        if (teamEnumerable == null) return dict;

        foreach (var obj in teamEnumerable)
        {
            if (obj == null) continue;

            string id = null;
            try
            {
                var t = obj.GetType();
                var f = t.GetField("monsterId"); if (f != null) id = f.GetValue(obj) as string;
                var p = t.GetProperty("monsterId"); if (id == null && p != null) id = p.GetValue(obj, null) as string;
            }
            catch { }

            if (string.IsNullOrEmpty(id)) continue;

            var titles = GetTitles(id);
            if (titles == null) continue;

            for (int i = 0; i < titles.Count; i++)
            {
                var so = titles[i] as JobAuraTitleSO;
                if (!so) continue;

                var site = so.targetJobSite;
                float add = so.siteAuraPercent / 100f;

                if (dict.TryGetValue(site, out var cur)) dict[site] = cur + add;
                else dict[site] = add;
            }
        }

        return dict;
    }

    public static int GetJobCapacityBonus(JobType site)
    {

        int bonus = 0;

        var jm = JobManager.I;
        if (jm == null || jm.States == null) return 0;

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

                var titles = GetTitles(id);
                if (titles == null) continue;

                for (int ti = 0; ti < titles.Count; ti++)
                {
                    if (titles[ti] is JobCapacityBoosterTitleSO cap && cap.AppliesTo(site))
                        bonus += Mathf.Max(0, cap.capacityBonusFlat);
                }
            }

            break; 
        }

        return Mathf.Max(0, bonus);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Effectiveness mods
    // ─────────────────────────────────────────────────────────────────────────────

    public static float GetEffectivenessMult(string ownedId, MonsterDataSO def, int level)
    {
        var rt = Runtime;
        if (rt == null) return 1f;
        return Mathf.Max(0f, rt.GetEffectivenessMultiplier(ownedId, def, level));
    }

    public static float GetEffectivenessAdd(string ownedId, MonsterDataSO def, int level)
    {
        var rt = Runtime;
        if (rt == null) return 0f;
        return rt.GetEffectivenessAdd(ownedId, def, level);
    }

    public static float GetIncomingEffectivenessMult(string ownedId, MonsterDataSO def, int level, MonsterType incomingType)
    {
        var rt = Runtime;
        if (rt == null) return 1f;
        return Mathf.Max(0f, rt.GetIncomingEffectivenessMult(ownedId, def, level, incomingType));
    }

    public static float GetIncomingEffectivenessMult(string ownedId, MonsterDataSO def, int level)
    {
        var rt = Runtime;
        if (rt == null) return 1f;
        return Mathf.Max(0f, rt.GetIncomingEffectivenessMultiplier(ownedId, def, level));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Damage filter
    // ─────────────────────────────────────────────────────────────────────────────

    public static TitleDamageFilter GetDamageFilter(string ownedId, MonsterDataSO def, int level)
    {
        var rt = Runtime;
        if (rt == null) return default;
        var f = rt.GetDamageFilter(ownedId, def, level);
        return new TitleDamageFilter
        {
            cannotBeCrit  = f.cannotBeCrit,
            percentReduce = f.percentReduce,
            flatReduce    = f.flatReduce
        };
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Conditional mods (direct)
    // ─────────────────────────────────────────────────────────────────────────────

    public static TitleStatMods GetConditionalBattleMods(string id, float hpPct, int alliesAlive, int winStreak)
    {
        var rt = Runtime;
        if (rt == null) return default;

        var ctx = new TitleContext(id, hpPct, alliesAlive, winStreak);
        return rt.GetConditionalBattleMods(ctx);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Global victory multipliers (optional)
    // ─────────────────────────────────────────────────────────────────────────────

    public static float GetVictoryCreditMult()
    {
        var rt = Runtime;
        if (rt == null) return 1f;
        return 1f;
    }

    public static float GetVictoryXPMult()
    {
        var rt = Runtime;
        if (rt == null) return 1f;
        return 1f;
    }

}
