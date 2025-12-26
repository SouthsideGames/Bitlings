using System;
using System.Collections.Generic;
using UnityEngine;

public static class MonsterCatalog
{
    private static List<MonsterDataSO> _all;                    // union list (library + unlocked packs)
    private static Dictionary<string, MonsterDataSO> _byId;     // union map

    public static IReadOnlyList<MonsterDataSO> All
    {
        get { EnsureBuilt(); return _all; }
    }

    public static MonsterDataSO GetById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        EnsureBuilt();
        _byId.TryGetValue(id, out var def);
        return def;
    }

    public static bool Contains(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        EnsureBuilt();
        return _byId.ContainsKey(id);
    }

    public static void Invalidate()
    {
        _all = null;
        _byId = null;
    }

    private static void EnsureBuilt()
    {
        if (_all != null && _byId != null) return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        _all = new List<MonsterDataSO>(256);

        // 1) Base library
        var lib = MonsterLibraryLocator.Lib; // loads Resources/MonsterLibrary.asset
        if (lib != null && lib.monsters != null)
        {
            foreach (var m in lib.monsters)
            {
                if (m == null || string.IsNullOrEmpty(m.id)) continue;
                if (seen.Add(m.id)) _all.Add(m);
            }
        }

        // 2) Unlocked pack monsters (discovered pool)
        var data = SaveManager.Data;
        if (data != null && data.unlockedPacks != null && data.unlockedPacks.Count > 0)
        {
            var packLib = MonsterPackLibraryLocator.Lib;
            if (packLib != null)
            {
                packLib.Warmup();

                for (int i = 0; i < data.unlockedPacks.Count; i++)
                {
                    var packId = data.unlockedPacks[i];
                    if (string.IsNullOrEmpty(packId)) continue;

                    var pack = packLib.Get(packId);
                    if (pack == null || pack.monsters == null) continue;

                    for (int j = 0; j < pack.monsters.Count; j++)
                    {
                        var def = pack.monsters[j];
                        if (def == null || string.IsNullOrEmpty(def.id)) continue;
                        if (seen.Add(def.id)) _all.Add(def);
                    }
                }
            }
        }

        // Build lookup
        _byId = new Dictionary<string, MonsterDataSO>(_all.Count, StringComparer.Ordinal);
        for (int i = 0; i < _all.Count; i++)
        {
            var m = _all[i];
            if (m != null && !string.IsNullOrEmpty(m.id))
                _byId[m.id] = m;
        }
    }
}
