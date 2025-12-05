// SaveDebugTools.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

public static class SaveDebugTools
{
    /// Path: Application.persistentDataPath/PlayerSaveAudit.json
    public static string AuditPath =>
        Path.Combine(Application.persistentDataPath, "PlayerSaveAudit.json");

    /// Call this after any save-changing action. Produces a pretty JSON snapshot
    /// of key player data, all owned monsters, team composition, and resources.
    public static void ExportAuditJson(bool pretty = true)
    {
        try
        {
            var dataObj = SaveManager.Data;
            if (dataObj == null)
            {
                Debug.LogWarning("[SaveDebugTools] No SaveManager.Data present.");
                return;
            }

            var snap = BuildSnapshot(dataObj);

            string json = JsonUtility.ToJson(snap, pretty);

            Directory.CreateDirectory(Path.GetDirectoryName(AuditPath));
            File.WriteAllText(AuditPath, json);
            Debug.Log($"[SaveDebugTools] Wrote audit → {AuditPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveDebugTools] Export failed: {ex}");
        }
    }

    private static AuditSnapshot BuildSnapshot(object dataObj)
    {
        var snap = new AuditSnapshot
        {
            version     = Application.version,
            savedAtUnix = SaveManager.NowUnix(),
            winStreak   = TryGetWinStreak(),
            resources   = TryGetResources(),
            coins       = TryGetCoins(),
            team        = new List<AuditTeamSlot>(),
            owned       = new List<AuditOwned>(),
            titles      = TryGetTitlesMap(),     // NOTE: JsonUtility won't serialize Dictionary (non-blocking)
            jobs        = TryGetJobAssignments() // NOTE: same as above
        };

        // --- Read team/owned via reflection (no dynamic) ---
        var team  = GetFieldOrPropValue<List<OwnedMonsterData>>(dataObj, "team");
        var owned = GetFieldOrPropValue<List<OwnedMonsterData>>(dataObj, "owned");

        if (team == null)  team  = new List<OwnedMonsterData>();
        if (owned == null) owned = new List<OwnedMonsterData>();

        // Team
        for (int i = 0; i < team.Count; i++)
        {
            var t = team[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId))
            {
                snap.team.Add(new AuditTeamSlot { slot = i, empty = true });
                continue;
            }

            snap.team.Add(new AuditTeamSlot
            {
                slot      = i,
                monsterId = t.monsterId,
                level     = Mathf.Max(1, t.level),
                ownedUID  = SafeOwnedUID(t),
                currentHP = Mathf.Max(0, t.currentHP),
                currentXP = Mathf.Max(0, t.currentXP)
            });
        }

        // Owned (with base/final stat snapshot)
        foreach (var o in owned)
        {
            if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;

            var entry = new AuditOwned
            {
                ownedUID     = SafeOwnedUID(o),
                monsterId    = o.monsterId,
                level        = Mathf.Max(1, o.level),
                currentHP    = Mathf.Max(0, o.currentHP),
                currentXP    = Mathf.Max(0, o.currentXP),
                lastHPUnix   = o.lastHPUnix,
                lastBucketId = o.lastBucketId,

                // Saved persistent training/evolution bonuses (if present on your model)
                bonusHP      = GetIntField(o, "bonusHP"),
                bonusATK     = GetIntField(o, "bonusATK"),
                bonusDEF     = GetIntField(o, "bonusDEF"),
                bonusSPD     = GetIntField(o, "bonusSPD"),
                isShiny      = GetBoolField(o, "isShiny"),
            };

            // Compute base/final stats at export time:
            //   base = definition @ current level
            //   final = base + saved training bonuses (+ flat equip for ATK)
            try
            {
                var def = MonsterLibraryLocator.GetById(o.monsterId);
                int lvl = Mathf.Max(1, o.level);

                if (def)
                {
                    entry.baseHP  = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl));
                    entry.baseATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, lvl, 0, 0));
                    entry.baseDEF = BattleCalc.CalcDefense(def, lvl);
                    entry.baseSPD = BattleCalc.CalcSpeed(def, lvl);

                    int flatAtkBonus = GetIntField(o, "flatAtkBonus"); // optional equip flat

                    entry.finalHP  = Mathf.Max(1, entry.baseHP  + entry.bonusHP);
                    entry.finalATK = Mathf.Max(1, entry.baseATK + entry.bonusATK + Mathf.Max(0, flatAtkBonus));
                    entry.finalDEF = Mathf.Max(0, entry.baseDEF + entry.bonusDEF);
                    entry.finalSPD = Mathf.Max(1, entry.baseSPD + entry.bonusSPD);
                }
                else
                {
                    Debug.LogWarning($"[SaveDebugTools] No MonsterDataSO found for '{o.monsterId}'. Base/final stats left at 0.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveDebugTools] Stat compute failed for '{o.monsterId}': {ex.Message}");
            }

            snap.owned.Add(entry);
        }

        // Species index (duplicates check)
        var seen = new HashSet<string>();
        snap.speciesIndex = new List<AuditSpeciesIndex>();
        foreach (var o in snap.owned)
        {
            if (seen.Add(o.monsterId))
                snap.speciesIndex.Add(new AuditSpeciesIndex { monsterId = o.monsterId });
        }

        return snap;
    }

    // ---------- helpers (reflection-safe) ----------
    private static T GetFieldOrPropValue<T>(object obj, string name) where T : class
    {
        if (obj == null) return null;
        var t = obj.GetType();

        try
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (f != null)
            {
                var val = f.GetValue(obj) as T;
                if (val != null) return val;
            }
        }
        catch { }

        try
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (p != null)
            {
                var val = p.GetValue(obj, null) as T;
                if (val != null) return val;
            }
        }
        catch { }

        return null;
    }

    private static int GetIntField(object obj, string fieldName)
    {
        if (obj == null) return 0;
        try
        {
            var f = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(int))
                return (int)f.GetValue(obj);
        }
        catch { }
        return 0;
    }

    private static bool GetBoolField(object obj, string fieldName)
    {
        if (obj == null) return false;
        try
        {
            var f = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(bool))
                return (bool)f.GetValue(obj);
        }
        catch { }
        return false;
    }

    private static string SafeOwnedUID(OwnedMonsterData o)
    {
        try
        {
            var f = typeof(OwnedMonsterData).GetField("ownedUID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                string v = f.GetValue(o) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        catch { }
        // fallback to a stable synthetic: MonsterId#Level#lastHPUnix
        return $"{o.monsterId}#{Mathf.Max(1, o.level)}#{o.lastHPUnix}";
    }

    private static int TryGetWinStreak()
    {
        try
        {
            var em = EncounterManager.I;
            if (!em) return 0;
            var t = em.GetType();
            var p = t.GetProperty("CurrentWinStreak") ?? t.GetProperty("WinStreak");
            if (p != null && p.PropertyType == typeof(int)) return (int)p.GetValue(em, null);

            var m = t.GetMethod("GetWinStreak", BindingFlags.Public | BindingFlags.Instance);
            if (m != null && m.ReturnType == typeof(int)) return (int)m.Invoke(em, null);
        }
        catch { }
        return 0;
    }

    private static Dictionary<string,int> TryGetResources()
    {
        var map = new Dictionary<string,int>();
        try
        {
            if (!ResourceManager.I) return map;
            foreach (ResourceType rt in Enum.GetValues(typeof(ResourceType)))
                map[rt.ToString()] = ResourceManager.I.Get(rt);
        }
        catch { }
        return map;
    }

    private static int TryGetCoins()
    {
        try
        {
            return ResourceManager.I ? ResourceManager.I.Get(ResourceType.Coin) : 0;
        }
        catch { return 0; }
    }

    private static Dictionary<string,string> TryGetTitlesMap()
    {
        var map = new Dictionary<string,string>();
        try
        {
            var tm = TitleManager.I;
            if (tm == null) return map;

            // Try common shapes: Dictionary<string,string> ActiveTitleByOwned / activeByOwned / BoundTitlesByOwned
            var t = tm.GetType();

            foreach (var name in new[] { "ActiveTitleByOwned", "activeByOwned", "BoundTitlesByOwned" })
            {
                var dict = GetFieldOrPropDictStringString(tm, name);
                if (dict != null)
                {
                    foreach (var kv in dict)
                        map[kv.Key] = kv.Value;
                    return map;
                }
            }
        }
        catch { }
        return map;
    }

    private static Dictionary<string,string> GetFieldOrPropDictStringString(object obj, string name)
    {
        if (obj == null) return null;
        var t = obj.GetType();

        try
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && typeof(System.Collections.IDictionary).IsAssignableFrom(f.FieldType))
            {
                return ConvertToStringStringDict(f.GetValue(obj));
            }
        }
        catch { }

        try
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && typeof(System.Collections.IDictionary).IsAssignableFrom(p.PropertyType))
            {
                return ConvertToStringStringDict(p.GetValue(obj, null));
            }
        }
        catch { }

        return null;
    }

    private static Dictionary<string,string> ConvertToStringStringDict(object dictObj)
    {
        var result = new Dictionary<string,string>();
        if (dictObj is System.Collections.IDictionary id)
        {
            foreach (var key in id.Keys)
            {
                var k = key != null ? key.ToString() : "";
                var v = id[key] != null ? id[key].ToString() : "";
                if (!string.IsNullOrEmpty(k)) result[k] = v;
            }
        }
        return result;
    }

    private static Dictionary<string,string> TryGetJobAssignments()
    {
        var map = new Dictionary<string,string>();
        try
        {
            var jm = JobManager.I;
            if (jm == null) return map;

            // Try common shapes: Dictionary<string, JobType> CurrentAssignments / currentAssignments / Assignments
            foreach (var name in new[] { "CurrentAssignments", "currentAssignments", "Assignments" })
            {
                var dictObj = GetFieldOrPropAsIDictionary(jm, name);
                if (dictObj != null)
                {
                    foreach (var k in dictObj.Keys)
                    {
                        var key = k != null ? k.ToString() : "";
                        var val = dictObj[k] != null ? dictObj[k].ToString() : "";
                        if (!string.IsNullOrEmpty(key)) map[key] = val;
                    }
                    return map;
                }
            }
        }
        catch { }
        return map;
    }

    private static System.Collections.IDictionary GetFieldOrPropAsIDictionary(object obj, string name)
    {
        if (obj == null) return null;
        var t = obj.GetType();

        try
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && typeof(System.Collections.IDictionary).IsAssignableFrom(f.FieldType))
                return (System.Collections.IDictionary)f.GetValue(obj);
        }
        catch { }

        try
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && typeof(System.Collections.IDictionary).IsAssignableFrom(p.PropertyType))
                return (System.Collections.IDictionary)p.GetValue(obj, null);
        }
        catch { }

        return null;
    }

    // ---------- data containers (JSON) ----------
    [Serializable] private class AuditSnapshot
    {
        public string version;
        public long   savedAtUnix;
        public int    winStreak;
        public Dictionary<string,int>    resources; // NOTE: JsonUtility won't serialize Dictionary
        public int coins;
        public List<AuditTeamSlot> team;
        public List<AuditOwned>    owned;
        public List<AuditSpeciesIndex> speciesIndex;
        public Dictionary<string,string> titles; // NOTE: JsonUtility won't serialize Dictionary
        public Dictionary<string,string> jobs;   // NOTE: JsonUtility won't serialize Dictionary
    }

    [Serializable] private class AuditTeamSlot
    {
        public int    slot;
        public bool   empty;
        public string ownedUID;
        public string monsterId;
        public int    level;
        public int    currentHP;
        public int    currentXP;
    }

    [Serializable] private class AuditOwned
    {
        public string ownedUID;
        public string monsterId;
        public int    level;
        public int    currentHP;
        public int    currentXP;
        public long   lastHPUnix;
        public string lastBucketId;

        // Persistent bonuses saved on OwnedMonsterData (if present)
        public int    bonusHP;
        public int    bonusATK;
        public int    bonusDEF;
        public int    bonusSPD;
        public bool   isShiny;

        // Base stats computed from defs (at current level)
        public int    baseHP;
        public int    baseATK;
        public int    baseDEF;
        public int    baseSPD;

        // Final = base + persistent bonuses (+ equip ATK flat)
        public int    finalHP;
        public int    finalATK;
        public int    finalDEF;
        public int    finalSPD;
    }

    [Serializable] private class AuditSpeciesIndex
    {
        public string monsterId;
    }
}
