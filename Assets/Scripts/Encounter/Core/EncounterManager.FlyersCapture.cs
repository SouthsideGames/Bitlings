using UnityEngine;
using System.Collections.Generic;
using System;
using Random = UnityEngine.Random;

public partial class EncounterManager
{
    // ================= LURES / LUCK / SHINY / CAPTURE BAND =====================

    public IReadOnlyList<FlyerBiasData> ActiveLures => SaveManager.Data?.activeFlyers;

    public void AddFlyer(MonsterType type, float bonus = 0.30f, int durationHours = 2)
    {
        if (SaveManager.Data == null) return;

        bonus = Mathf.Clamp(bonus, 0f, 2f);
        durationHours = Mathf.Max(1, durationHours);

        long now = SaveManager.NowUnix();
        long expiry = now + durationHours * 3600L;

        if (SaveManager.Data.activeFlyers == null)
            SaveManager.Data.activeFlyers = new List<FlyerBiasData>();

        SaveManager.Data.activeFlyers.Clear();
        SaveManager.Data.activeFlyers.Add(new FlyerBiasData
        {
            type = type,
            bonus = bonus,
            expireUnix = expiry
        });

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
    }

    private Dictionary<MonsterType, float> BuildFlyerTypeMultipliers()
    {
        var map = new Dictionary<MonsterType, float>();
        var cur = CurrentFlyer;
        if (cur == null) return map;

        float mult = Mathf.Clamp(1f + Mathf.Max(0f, cur.bonus), 1f, 3f);
        map[cur.type] = mult;
        return map;
    }

    public MonsterDataSO PickWildConsideringFlyers()
    {
        var lib = MonsterLibraryLocator.Lib;
        if (lib == null || lib.monsters == null || lib.monsters.Length == 0)
            return null;

        var pool = new List<MonsterDataSO>(lib.monsters.Length + 16);
        var added = new HashSet<string>();

        // 1) Base: anything in the library with spawnWeight > 0 is always eligible
        for (int i = 0; i < lib.monsters.Length; i++)
        {
            var m = lib.monsters[i];
            if (m == null || string.IsNullOrEmpty(m.id)) continue;
            if (m.spawnWeight <= 0f) continue;

            if (added.Add(m.id))
                pool.Add(m);
        }

        // 2) Add discovered monsters (packs) to expand the pool (if they’re not already in it)
        var data = SaveManager.Data;
        if (data != null)
        {
            data.discoveredMonsterIds ??= new HashSet<string>();

            foreach (var id in data.discoveredMonsterIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (added.Contains(id)) continue;

                var def = MonsterLibraryLocator.GetById(id);
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                if (def.spawnWeight <= 0f) continue;

                if (added.Add(def.id))
                    pool.Add(def);
            }
        }

        if (pool.Count == 0)
        {
            for (int i = 0; i < lib.monsters.Length; i++)
            {
                var m = lib.monsters[i];
                if (m == null || string.IsNullOrEmpty(m.id)) continue;
                pool.Add(m);
            }
            if (pool.Count == 0) return null;
            return pool[Random.Range(0, pool.Count)];
        }

        var typeMult = BuildFlyerTypeMultipliers();
        float flyerBonus01 = GetActiveFlyerBonus01();

        // min/max base weight for "scarcity" calc (luck favors scarce = lower base weight)
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
            if (flyerBonus01 > 0f && maxBase > minBase)
            {
                float scarcity01 = Mathf.Clamp01((maxBase - baseW) / (maxBase - minBase));
                float luckMult = 1f + flyerBonus01 * scarcity01;
                mult *= luckMult;
            }

            // Shiny Orb: boost any shiny variants
            float shinyMult = GetActiveShinyBoostMult();
            if (shinyMult > 1f && IsShinyMonster(m))
                mult *= shinyMult;

            float finalW = baseW * mult;
            if (float.IsNaN(finalW) || float.IsInfinity(finalW)) return 0f;
            return Mathf.Max(0f, finalW);
        }

        return PickByWeight(pool, GetFinalWeight);
    }

    public FlyerBiasData CurrentFlyer
    {
        get
        {
            var list = SaveManager.Data?.activeFlyers;
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
            var list = SaveManager.Data?.activeFavorBoosts;
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

    private WorkOrderData CurrentCaptureBand
    {
        get
        {
            var list = SaveManager.Data?.activeWorkOrders;
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

    private float GetActiveFlyerBonus01()
    {
        var cur = CurrentLuck;
        if (cur == null) return 0f;
        return Mathf.Clamp01(cur.bonus);
    }

    public bool HasActiveShinyBoost()
    {
        return CurrentShinyBoost != null;
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

    public long GetFlyerSecondsRemaining()
    {
        var cur = CurrentFlyer;
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
    void TryCatch(MonsterDataSO def, int level) => TryCatchWithResult(def, level, out _);

    /// <summary>
    /// Attempts to capture and returns success/failure.
    /// Also outputs the finalChance used for logging/UI if desired.
    /// </summary>
    bool TryCatchWithResult(MonsterDataSO def, int level, out float finalChance)
    {
        finalChance = 0f;

        if (!def) return false;
        var data = SaveManager.Data;
        var lib = MonsterLibraryLocator.Lib;
        if (data == null || !lib) return false;

        if (def.uncatchable)
        {
            EmitStatus("(Capture skipped — uncatchable.)", LogScope.Encounter);
            return false;
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

        float bandBonus = GetActiveCaptureBonus01() * 0.25f;
        float scarcity01 = 1f - t;
        float luckBonus = GetActiveFlyerBonus01() * 0.20f * Mathf.Clamp01(scarcity01 * 1.25f);
        float lureBonus = 0f;
        var lure = CurrentFlyer;
        if (lure != null && lure.type == def.type)
            lureBonus = Mathf.Clamp01(lure.bonus) * 0.10f;
        float streakBonus = Mathf.Clamp01(CurrentWinStreak / 20f) * 0.05f;

        finalChance = Mathf.Clamp01(baseChance + bandBonus + luckBonus + lureBonus + streakBonus);

        float roll = Random.value;
        bool success = roll <= finalChance;

        // ---------------------------------------------------------------------
        // SHINY DETERMINATION (FIXED):
        // If the encounter was presented as shiny, the OwnedMonsterData must store isShiny=true,
        // even if the species is not "shiny-flagged" by definition.
        // ---------------------------------------------------------------------

        // Encounter-scoped shiny presentation (either still active, or captured from battle end)
        bool encounterWasShiny = _currentWildIsShiny || _lastWildWasShiny;

        // Legacy/species shiny flag OR encounter presented shiny
        bool isShiny = IsShinyMonster(def) || encounterWasShiny;

        // If no shiny art exists, do not mark shiny (prevents "shiny" with normal visuals).
        if (isShiny && def.shinyIcon == null)
            isShiny = false;

        // Shiny cheat
        if (SaveManager.Data != null && SaveManager.Data.forceShinyCapturesRemaining > 0)
        {
            isShiny = true;

            // still respect art availability
            if (def.shinyIcon == null)
                isShiny = false;
        }

        FieldOpsTracker.RecordCaptureAttempt(def, success, isShiny);

        if (success)
        {
            AudioManager.I?.PlaySfx(SfxType.CaptureSuccess);

            var om = new OwnedMonsterData
            {
                monsterId = def.id,
                level = Mathf.Max(1, level),
                currentHP = -1,
                currentXP = 0,
                ownedUID = Guid.NewGuid().ToString("N"),

                isShiny = isShiny,
                shinyTier = isShiny ? 1 : 0
            };

            // Consume shiny cheat only on successful capture (your existing behavior)
            if (isShiny && SaveManager.Data != null && SaveManager.Data.forceShinyCapturesRemaining > 0)
            {
                SaveManager.Data.forceShinyCapturesRemaining =
                    Mathf.Max(0, SaveManager.Data.forceShinyCapturesRemaining - 1);
            }

            // IMPORTANT: consume the "last wild was shiny" sticky flag after it is used for capture,
            // so it cannot leak into later capture attempts.
            _lastWildWasShiny = false;

            data.owned ??= new List<OwnedMonsterData>();
            data.owned.Add(om);

            data.ownedIds ??= new HashSet<string>(); data.ownedIds.Add(def.id);
            data.seenTypes ??= new HashSet<MonsterType>(); data.seenTypes.Add(def.type);

            SaveManager.Save();
            GameEvents.OnResourcesChanged?.Invoke();

            GameEvents.MonsterCaptured?.Invoke(def.id, def.type);

            BattleLogger.Log(
                $"🎉 Capture success! {def.displayName} (Lv {level}) joined your roster. [p={Mathf.RoundToInt(finalChance * 100f)}%]",
                LogScope.Encounter
            );
            EmitStatus($"Captured {def.displayName}! (Lv {level})", LogScope.Encounter);
        }
        else
        {
            BattleLogger.Log(
                $"Capture failed on {def.displayName} (Lv {level}). [p={Mathf.RoundToInt(finalChance * 100f)}%, roll={Mathf.RoundToInt(roll * 100f)}%]",
                LogScope.Encounter
            );
            EmitStatus($"Capture failed. {def.displayName} escaped.", LogScope.Encounter);
        }

        return success;
    }

    // ── Shiny / Unique helpers ─────────────────────────────────────────────────

    private bool IsShinyMonster(MonsterDataSO m)
    {
        if (!m) return false;

        try
        {
            var t = m.GetType();

            var f = t.GetField("isShiny",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (f != null)
            {
                var val = f.GetValue(m);
                if (val is bool b) return b;
            }

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

            // 1) Check rarity enum/string
            object rarityObj = null;

            var fR = t.GetField("rarity",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (fR != null) rarityObj = fR.GetValue(m);

            var pR = t.GetProperty("rarity",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (rarityObj == null && pR != null && pR.CanRead)
                rarityObj = pR.GetValue(m, null);

            if (rarityObj != null)
            {
                string name = rarityObj.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    name = name.ToLowerInvariant();
                    if (name.Contains("legend") || name.Contains("myth"))
                        return true;
                }
            }

            var fU = t.GetField("isUniqueEncounter",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (fU != null)
            {
                var val = fU.GetValue(m);
                if (val is bool b && b) return true;
            }

            var fU2 = t.GetField("isUnique",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (fU2 != null)
            {
                var val = fU2.GetValue(m);
                if (val is bool b && b) return true;
            }

            var pU = t.GetProperty("isUniqueEncounter",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (pU != null && pU.CanRead)
            {
                var val = pU.GetValue(m, null);
                if (val is bool b && b) return true;
            }

            var pU2 = t.GetProperty("isUnique",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (pU2 != null && pU2.CanRead)
            {
                var val = pU2.GetValue(m, null);
                if (val is bool b && b) return true;
            }
        }
        catch { }

        return false;
    }
}
