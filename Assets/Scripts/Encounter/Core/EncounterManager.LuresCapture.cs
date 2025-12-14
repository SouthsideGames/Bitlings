using UnityEngine;
using System.Collections.Generic;
using System;
using Random = UnityEngine.Random;

public partial class EncounterManager
{
    // ================= LURES / LUCK / SHINY / CAPTURE BAND =====================

    public IReadOnlyList<LureBiasData> ActiveLures => SaveManager.Data?.activeLures;

    public void AddLure(MonsterType type, float bonus = 0.30f, int durationHours = 2)
    {
        if (SaveManager.Data == null) return;

        bonus = Mathf.Clamp(bonus, 0f, 2f);
        durationHours = Mathf.Max(1, durationHours);

        long now = SaveManager.NowUnix();
        long expiry = now + durationHours * 3600L;

        if (SaveManager.Data.activeLures == null)
            SaveManager.Data.activeLures = new List<LureBiasData>();

        SaveManager.Data.activeLures.Clear();
        SaveManager.Data.activeLures.Add(new LureBiasData
        {
            type = type,
            bonus = bonus,
            expireUnix = expiry
        });

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
    }

    private Dictionary<MonsterType, float> BuildLureTypeMultipliers()
    {
        var map = new Dictionary<MonsterType, float>();
        var cur = CurrentLure;
        if (cur == null) return map;

        float mult = Mathf.Clamp(1f + Mathf.Max(0f, cur.bonus), 1f, 3f);
        map[cur.type] = mult;
        return map;
    }

    public MonsterDataSO PickWildConsideringLures()
    {
        var lib = MonsterLibraryLocator.Lib;
        if (lib == null || lib.monsters == null || lib.monsters.Length == 0)
            return null;

        List<MonsterDataSO> pool = new List<MonsterDataSO>(lib.monsters.Length);
       for (int i = 0; i < lib.monsters.Length; i++)
        {
            var m = lib.monsters[i];
            if (m == null || string.IsNullOrEmpty(m.id)) continue;
            if (m.spawnWeight <= 0f) continue;

            if (!IsMonsterDiscovered(m)) continue;

            pool.Add(m);
        }

        if (pool.Count == 0)
        {
            for (int i = 0; i < lib.monsters.Length; i++)
            {
                var m = lib.monsters[i];
                if (m != null && !string.IsNullOrEmpty(m.id)) pool.Add(m);
            }
            if (pool.Count == 0) return null;
            return pool[Random.Range(0, pool.Count)];
        }

        var typeMult = BuildLureTypeMultipliers();
        float luckBonus01 = GetActiveLuckBonus01();

        float minBase = float.MaxValue;
        float maxBase = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            float b = Mathf.Max(0f, pool[i].spawnWeight);
            if (b < minBase) minBase = b;
            if (b > maxBase) maxBase = b;
        }

        float GetFinalWeight(MonsterDataSO m)
        {
            float baseW = Mathf.Max(0f, m.spawnWeight);
            if (baseW <= 0f) return 0f;

            float mult = 1f;

            // Lure type bias
            if (typeMult != null && typeMult.TryGetValue(m.type, out float tMult))
                mult *= Mathf.Max(0f, tMult);

            // WyrmDen rarity weighting
            mult *= JobBalance.GetWyrmDenRarityWeightMult(m.rarity);

            // Luck: favors scarce monsters
            if (luckBonus01 > 0f && maxBase > minBase)
            {
                float scarcity01 = Mathf.Clamp01((maxBase - baseW) / (maxBase - minBase));
                float luckMult = 1f + luckBonus01 * scarcity01;
                mult *= luckMult;
            }

            // 🔹 Shiny Orb: boost any shiny variants
            float shinyMult = GetActiveShinyBoostMult();
            if (shinyMult > 1f && IsShinyMonster(m))
            {
                mult *= shinyMult;
            }

            float finalW = baseW * mult;
            if (float.IsNaN(finalW) || float.IsInfinity(finalW)) return 0f;
            return Mathf.Max(0f, finalW);
        }

        return PickByWeight(pool, GetFinalWeight);
    }

    public LureBiasData CurrentLure
    {
        get
        {
            var list = SaveManager.Data?.activeLures;
            if (list == null || list.Count == 0) return null;
            var cur = list[0];
            if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
            {
                list.Clear();
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
                return null;
            }
            return cur;
        }
    }

    private LuckBoostData CurrentLuck
    {
        get
        {
            var list = SaveManager.Data?.activeLuckBoosts;
            if (list == null || list.Count == 0) return null;
            var cur = list[0];
            if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
            {
                list.Clear();
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
                return null;
            }
            return cur;
        }
    }

    private ShinyBoostData CurrentShinyBoost
    {
        get
        {
            var list = SaveManager.Data?.activeShinyBoosts;
            if (list == null || list.Count == 0) return null;
            var cur = list[0];
            if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
            {
                list.Clear();
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
                return null;
            }
            return cur;
        }
    }

    private CaptureBandData CurrentCaptureBand
    {
        get
        {
            var list = SaveManager.Data?.activeCaptureBands;
            if (list == null || list.Count == 0) return null;
            var cur = list[0];
            if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
            {
                list.Clear();
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
                return null;
            }
            return cur;
        }
    }

    private float GetActiveLuckBonus01()
    {
        var cur = CurrentLuck;
        if (cur == null) return 0f;
        return Mathf.Clamp01(cur.bonus);
    }

    private float GetActiveShinyBoostMult()
    {
        var cur = CurrentShinyBoost;
        if (cur == null) return 1f;
        return Mathf.Max(1f, cur.bonus);
    }

    private float GetActiveCaptureBonus01()
    {
        var cur = CurrentCaptureBand;
        if (cur == null) return 0f;
        return Mathf.Clamp01(cur.bonus);
    }

    public long GetLureSecondsRemaining()
    {
        var cur = CurrentLure;
        if (cur == null) return -1;
        long now = SaveManager.NowUnix();
        long rem = cur.expireUnix - now;
        return Math.Max(0L, rem);
    }

    private static T PickByWeight<T>(IList<T> items, System.Func<T, float> getWeight)
    {
        float total = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            float w = Mathf.Max(0f, getWeight(items[i]));
            if (!float.IsNaN(w) && !float.IsInfinity(w)) total += w;
        }

        if (total <= 0f)
            return items.Count > 0 ? items[Random.Range(0, items.Count)] : default;

        float roll = Random.Range(0f, total);
        float acc = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            float w = Mathf.Max(0f, getWeight(items[i]));
            if (w <= 0f) continue;
            acc += w;
            if (roll <= acc) return items[i];
        }
        return items[items.Count - 1];
    }

    // ====== Capture logic ======
    void TryCatch(MonsterDataSO def, int level)
    {
        if (!def) return;
        var data = SaveManager.Data;
        var lib  = MonsterLibraryLocator.Lib;
        if (data == null || !lib) return;

        if (def.uncatchable)
        {
            EmitStatus("(Capture skipped — uncatchable.)", LogScope.Encounter);
            return;
        }

        // Base chance from spawn weight → [15%, 65%]
        float minW = float.MaxValue, maxW = 0f;
        for (int i = 0; i < lib.monsters.Length; i++)
        {
            var m = lib.monsters[i];
            if (!m) continue;
            float w = Mathf.Max(0f, m.spawnWeight);
            if (w < minW) minW = w;
            if (w > maxW) maxW = w;
        }
        if (minW == float.MaxValue || maxW <= 0f || minW >= maxW)
        {
            minW = 0f; maxW = 1f;
        }

        float t = Mathf.Clamp01(
            (Mathf.Max(0f, def.spawnWeight) - minW) /
            Mathf.Max(0.0001f, (maxW - minW))
        );
        float baseChance = Mathf.Lerp(0.15f, 0.65f, t);

        float bandBonus  = GetActiveCaptureBonus01() * 0.25f;
        float scarcity01 = 1f - t;
        float luckBonus  = GetActiveLuckBonus01() * 0.20f * Mathf.Clamp01(scarcity01 * 1.25f);
        float lureBonus  = 0f;
        var lure = CurrentLure;
        if (lure != null && lure.type == def.type)
            lureBonus = Mathf.Clamp01(lure.bonus) * 0.10f;
        float streakBonus = Mathf.Clamp01(CurrentWinStreak / 20f) * 0.05f;

        float finalChance = Mathf.Clamp01(baseChance + bandBonus + luckBonus + lureBonus + streakBonus);

        float roll = Random.value;
        bool success = roll <= finalChance;

        bool isShiny = IsShinyMonster(def);
        FieldOpsTracker.RecordCaptureAttempt(def, success, isShiny);

        if (success)
        {
            if (AudioManager.I)
                AudioManager.I.PlaySfx(SfxType.CaptureSuccess);

            var om = new OwnedMonsterData
            {
                monsterId = def.id,
                level     = Mathf.Max(1, level),
                currentHP = -1,
                currentXP = 0,
                ownedUID  = Guid.NewGuid().ToString("N")
            };
            data.owned ??= new List<OwnedMonsterData>();
            data.owned.Add(om);

            data.ownedIds  ??= new HashSet<string>();      data.ownedIds.Add(def.id);
            data.seenTypes ??= new HashSet<MonsterType>(); data.seenTypes.Add(def.type);

            SaveManager.Save();
            GameEvents.OnResourcesChanged?.Invoke();

            GameEvents.MonsterCaptured?.Invoke(def.id, def.type);

            BattleLogger.Log(
                $"🎉 Capture success! {def.displayName} (Lv {level}) joined your roster. [p={Mathf.RoundToInt(finalChance * 100f)}%]",
                LogScope.Encounter
            );
            EmitStatus($"Captured {def.displayName}! (Lv {level})", LogScope.Encounter);

            // NEW: capture success UI feedback (wild panel glow / banner)
            if (EncounterPanelUI.I)
                EncounterPanelUI.I.OnCaptureSuccess(def, IsShinyMonster(def));
        }
        else
        {
            BattleLogger.Log(
                $"Capture failed on {def.displayName} (Lv {level}). [p={Mathf.RoundToInt(finalChance * 100f)}%, roll={Mathf.RoundToInt(roll * 100f)}%]",
                LogScope.Encounter
            );
            EmitStatus($"Capture failed. {def.displayName} escaped.", LogScope.Encounter);

            // NEW: capture fail UI feedback (shake / ESCAPED)
            if (EncounterPanelUI.I)
                EncounterPanelUI.I.OnCaptureFailed(def);
        }
    }


    // ── Shiny / Unique helpers ─────────────────────────────────────────────────

    private bool IsShinyMonster(MonsterDataSO m)
    {
        if (!m) return false;

        try
        {
            var t = m.GetType();

            // Try field "isShiny"
            var f = t.GetField("isShiny",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (f != null)
            {
                var val = f.GetValue(m);
                if (val is bool b) return b;
            }

            // Try property "isShiny"
            var p = t.GetProperty("isShiny",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (p != null && p.CanRead)
            {
                var val = p.GetValue(m, null);
                if (val is bool b) return b;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Unique encounters = Legendary or Mythic monsters.
    /// We detect this by reading a "rarity" field/property and comparing its name,
    /// plus legacy boolean flags (isUniqueEncounter / isUnique) as fallback.
    /// </summary>
    private bool IsUniqueMonster(MonsterDataSO m)
    {
        if (!m) return false;

        try
        {
            var t = m.GetType();

            // First, try a "rarity" field/property and treat Legendary/Mythic as unique
            object rarityObj = null;

            var rf = t.GetField("rarity",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (rf != null)
                rarityObj = rf.GetValue(m);

            if (rarityObj == null)
            {
                var rp = t.GetProperty("rarity",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (rp != null && rp.CanRead)
                    rarityObj = rp.GetValue(m, null);
            }

            if (rarityObj != null)
            {
                string rarityName = rarityObj.ToString();
                if (string.Equals(rarityName, "Legendary", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rarityName, "Mythic",    StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rarityName, "Mythical",  StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Legacy: explicit flags
            var f1 = t.GetField("isUniqueEncounter",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (f1 != null)
            {
                var v = f1.GetValue(m);
                if (v is bool b1 && b1) return true;
            }

            var p1 = t.GetProperty("isUniqueEncounter",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (p1 != null && p1.CanRead)
            {
                var v = p1.GetValue(m, null);
                if (v is bool b1p && b1p) return true;
            }

            var f2 = t.GetField("isUnique",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (f2 != null)
            {
                var v = f2.GetValue(m);
                if (v is bool b2 && b2) return true;
            }

            var p2 = t.GetProperty("isUnique",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (p2 != null && p2.CanRead)
            {
                var v = p2.GetValue(m, null);
                if (v is bool b2p && b2p) return true;
            }
        }
        catch { }

        return false;
    }
}
