using UnityEngine;
using System.Collections.Generic;

public static class MonsterLibraryLocator
{
    private const string ResourcePath = "MonsterLibrary";
    private static MonsterLibrarySO _lib;
    private static Dictionary<string, MonsterDataSO> _byId;
    public static IReadOnlyList<MonsterDataSO> AllMonsters => MonsterCatalog.All;

    public static MonsterLibrarySO Lib
    {
        get
        {
            // Attempt to resolve if missing
            if (_lib == null)
            {
                _lib = Resources.Load<MonsterLibrarySO>(ResourcePath);
#if UNITY_EDITOR
                if (_lib == null)
                    Debug.LogWarning($"[MonsterLibraryLocator] MonsterLibrarySO not found at Resources/{ResourcePath}.asset");
#endif
                RebuildIndex();
            }
            return _lib;
        }
        set
        {
            _lib = value;
            RebuildIndex();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Lookup Methods
    // ─────────────────────────────────────────────────────────────────────────────

   public static MonsterDataSO GetById(string id)
    {
        return MonsterCatalog.GetById(id);
    }

    public static bool TryGet(string id, out MonsterDataSO def)
    {
        def = GetById(id);
        return def != null;
    }

    public static void RebuildIndex()
    {
        _byId = null;

        if (_lib == null || _lib.monsters == null)
            return;

        _byId = new Dictionary<string, MonsterDataSO>(_lib.monsters.Length);
        foreach (var m in _lib.monsters)
        {
            if (m == null || string.IsNullOrEmpty(m.id))
                continue;

            if (!_byId.ContainsKey(m.id))
                _byId.Add(m.id, m);
#if UNITY_EDITOR
            else
                Debug.LogWarning($"[MonsterLibraryLocator] Duplicate monster ID '{m.id}' found in library.");
#endif
        }
    }

    public static IReadOnlyDictionary<string, MonsterDataSO> GetAll() => _byId;

    
}
