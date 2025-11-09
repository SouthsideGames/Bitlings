using UnityEngine;
using System.Linq;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Data/Monster/Monsters Library", fileName = "MonsterLibrary")]
public class MonsterLibrarySO : ScriptableObject
{
    [SerializeField] public MonsterDataSO[] monsters;

    [Header("Availability")]
    [SerializeField] private MonsterPackLibrarySO packLibrary;
    [SerializeField] private bool respectPackGating = true;

    public IEnumerable<MonsterDataSO> All =>
        monsters?.Where(m => m != null && !string.IsNullOrEmpty(m.id)) ?? Enumerable.Empty<MonsterDataSO>();

    public IEnumerable<MonsterDataSO> AllAvailable => All.Where(IsAvailable);

    public MonsterDataSO GetById(string id)
        => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(m => m.id == id);

    public MonsterDataSO GetRandom()
    {
        var pool = All.ToArray();
        if (pool.Length == 0) return null;
        return pool[Random.Range(0, pool.Length)];
    }

    public MonsterDataSO GetRandomAvailable()
    {
        var pool = AllAvailable.ToArray();
        if (pool.Length == 0) return GetRandom();
        return pool[Random.Range(0, pool.Length)];
    }

    public MonsterDataSO[] GetAllOfType(MonsterType type, bool onlyAvailable = true)
    {
        var src = onlyAvailable ? AllAvailable : All;
        return src.Where(m => m.type == type).ToArray();
    }

    public int CountOfType(MonsterType type, bool onlyAvailable = true)
    {
        var src = onlyAvailable ? AllAvailable : All;
        return src.Count(m => m.type == type);
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
        var src = onlyAvailable ? AllAvailable : All;
        var pool = src.Where(m => m.spawnWeight > 0).ToArray();

        if (pool.Length == 0)
        {
            var backup = src.ToArray();
            if (backup.Length == 0) return null;
            return backup[Random.Range(0, backup.Length)];
        }

        float total = 0f;
        float[] weights = new float[pool.Length];

        for (int i = 0; i < pool.Length; i++)
        {
            float baseW = Mathf.Max(0, pool[i].spawnWeight);
            float mult = 1f;
            if (typeMultipliers != null && typeMultipliers.TryGetValue(pool[i].type, out var m))
                mult = Mathf.Max(0f, m);
            float w = baseW * mult;
            weights[i] = w;
            total += w;
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
