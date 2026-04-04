using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Data/Monster/Monsters Library", fileName = "MonsterLibrary")]
public class MonsterLibrarySO : ScriptableObject
{
    [SerializeField] public MonsterDataSO[] monsters;

    [Header("Availability")]
    [SerializeField] private MonsterPackLibrarySO packLibrary;
    [SerializeField] private bool respectPackGating = true;

    // ─── Cached arrays to avoid per-call LINQ allocations ───
    private MonsterDataSO[] _validCache;
    private Dictionary<string, MonsterDataSO> _idLookup;

    private MonsterDataSO[] GetValidMonsters()
    {
        if (_validCache != null) return _validCache;
        if (monsters == null || monsters.Length == 0) { _validCache = System.Array.Empty<MonsterDataSO>(); return _validCache; }
        int count = 0;
        for (int i = 0; i < monsters.Length; i++)
            if (monsters[i] != null && !string.IsNullOrEmpty(monsters[i].id)) count++;
        _validCache = new MonsterDataSO[count];
        int idx = 0;
        for (int i = 0; i < monsters.Length; i++)
            if (monsters[i] != null && !string.IsNullOrEmpty(monsters[i].id))
                _validCache[idx++] = monsters[i];
        return _validCache;
    }

    private Dictionary<string, MonsterDataSO> GetIdLookup()
    {
        if (_idLookup != null) return _idLookup;
        var valid = GetValidMonsters();
        _idLookup = new Dictionary<string, MonsterDataSO>(valid.Length, System.StringComparer.Ordinal);
        for (int i = 0; i < valid.Length; i++)
            _idLookup[valid[i].id] = valid[i];
        return _idLookup;
    }

    public IEnumerable<MonsterDataSO> All => GetValidMonsters();

    public IEnumerable<MonsterDataSO> AllAvailable
    {
        get
        {
            var valid = GetValidMonsters();
            // Return filtered without LINQ allocation — callers iterate via foreach
            for (int i = 0; i < valid.Length; i++)
                if (IsAvailable(valid[i])) yield return valid[i];
        }
    }

    public MonsterDataSO GetById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return GetIdLookup().TryGetValue(id, out var def) ? def : null;
    }

    public MonsterDataSO GetRandom()
    {
        var pool = GetValidMonsters();
        if (pool.Length == 0) return null;
        return pool[Random.Range(0, pool.Length)];
    }

    public MonsterDataSO GetRandomAvailable()
    {
        // Build temp list from available monsters to avoid LINQ
        var valid = GetValidMonsters();
        int count = 0;
        for (int i = 0; i < valid.Length; i++)
            if (IsAvailable(valid[i])) count++;
        if (count == 0) return GetRandom();

        int pick = Random.Range(0, count);
        int seen = 0;
        for (int i = 0; i < valid.Length; i++)
        {
            if (!IsAvailable(valid[i])) continue;
            if (seen == pick) return valid[i];
            seen++;
        }
        return GetRandom();
    }

    public MonsterDataSO[] GetAllOfType(MonsterType type, bool onlyAvailable = true)
    {
        var valid = GetValidMonsters();
        int count = 0;
        for (int i = 0; i < valid.Length; i++)
        {
            if (valid[i].type != type) continue;
            if (onlyAvailable && !IsAvailable(valid[i])) continue;
            count++;
        }
        var result = new MonsterDataSO[count];
        int idx = 0;
        for (int i = 0; i < valid.Length; i++)
        {
            if (valid[i].type != type) continue;
            if (onlyAvailable && !IsAvailable(valid[i])) continue;
            result[idx++] = valid[i];
        }
        return result;
    }

    public int CountOfType(MonsterType type, bool onlyAvailable = true)
    {
        var valid = GetValidMonsters();
        int count = 0;
        for (int i = 0; i < valid.Length; i++)
        {
            if (valid[i].type != type) continue;
            if (onlyAvailable && !IsAvailable(valid[i])) continue;
            count++;
        }
        return count;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Availability / Pack Gating
    // ─────────────────────────────────────────────────────────────────────────────

    public bool IsAvailable(MonsterDataSO def)
    {
        if (def == null) return false;
        if (!respectPackGating || packLibrary == null) return true;

        var packs = packLibrary.Packs;
        if (packs == null || packs.Count == 0) return true;

        // Use SaveManager-backed unlocks (null-safe)
        var unlockedSet = (SaveManager.Data?.unlockedPacks != null)
            ? new HashSet<string>(SaveManager.Data.unlockedPacks)
            : s_emptySet;

        bool gated = false;

        for (int i = 0; i < packs.Count; i++)
        {
            var p = packs[i];
            if (p == null || p.monsters == null) continue;

            // If this pack contains the monster, it’s gated by this pack
            for (int j = 0; j < p.monsters.Count; j++)
            {
                if (p.monsters[j] == def)
                {
                    // As soon as we find the containing pack, if it's unlocked → available
                    if (unlockedSet.Contains(p.id)) return true;
                    gated = true;
                    // Keep searching in case the same monster appears in another unlocked pack
                    break;
                }
            }
        }

        // Not found in any pack (not gated) → available; otherwise gated & locked → unavailable
        return !gated;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Weighted Random (Type Bonus)
    // ─────────────────────────────────────────────────────────────────────────────

    public MonsterDataSO GetRandomWeightedWithTypeBonus(
        Dictionary<MonsterType, float> typeMultipliers, bool onlyAvailable = true)
    {
        var valid = GetValidMonsters();

        // Build pool of spawnable monsters (spawnWeight > 0) without LINQ
        int poolCount = 0;
        for (int i = 0; i < valid.Length; i++)
        {
            if (valid[i].spawnWeight <= 0) continue;
            if (onlyAvailable && !IsAvailable(valid[i])) continue;
            poolCount++;
        }

        if (poolCount == 0)
        {
            // Fallback: any valid monster
            int fallbackCount = 0;
            for (int i = 0; i < valid.Length; i++)
                if (!onlyAvailable || IsAvailable(valid[i])) fallbackCount++;
            if (fallbackCount == 0) return null;
            int pick = Random.Range(0, fallbackCount);
            int seen = 0;
            for (int i = 0; i < valid.Length; i++)
            {
                if (onlyAvailable && !IsAvailable(valid[i])) continue;
                if (seen == pick) return valid[i];
                seen++;
            }
            return null;
        }

        // Build weights array
        var pool = new MonsterDataSO[poolCount];
        float[] weights = new float[poolCount];
        float total = 0f;
        int idx = 0;

        for (int i = 0; i < valid.Length; i++)
        {
            if (valid[i].spawnWeight <= 0) continue;
            if (onlyAvailable && !IsAvailable(valid[i])) continue;

            pool[idx] = valid[i];
            float baseW = Mathf.Max(0, valid[i].spawnWeight);
            float mult = 1f;
            if (typeMultipliers != null && typeMultipliers.TryGetValue(valid[i].type, out var m))
                mult = Mathf.Max(0f, m);
            float w = baseW * mult;
            weights[idx] = w;
            total += w;
            idx++;
        }

        if (total <= 0f) return pool[Random.Range(0, pool.Length)];

        float roll = Random.Range(0f, total);
        float running = 0f;
        for (int i = 0; i < pool.Length; i++)
        {
            running += weights[i];
            if (roll <= running) return pool[i];
        }

        return pool[pool.Length - 1];
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Internal
    // ─────────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> s_emptySet = new HashSet<string>();
}
