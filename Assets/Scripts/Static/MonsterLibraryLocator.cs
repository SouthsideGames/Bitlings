using UnityEngine;
using System.Collections.Generic;

public static class MonsterLibraryLocator
{
    private const string ResourcePath = "MonsterLibrary"; // Assets/Resources/MonsterLibrary.asset
    private static MonsterLibrarySO _lib;
    private static Dictionary<string, MonsterDataSO> _byId;

    /// <summary>Globally accessible library. Loads once from Resources if needed.</summary>
    public static MonsterLibrarySO Lib
    {
        get
        {
            if (_lib == null)
            {
                _lib = Resources.Load<MonsterLibrarySO>(ResourcePath);
#if UNITY_EDITOR
                if (_lib == null)
                    Debug.LogError($"MonsterLibrarySO not found at Resources/{ResourcePath}.asset");
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

    public static MonsterDataSO GetById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId == null) RebuildIndex();
        return _byId != null && _byId.TryGetValue(id, out var def) ? def : Lib?.GetById(id);
    }

    public static bool TryGet(string id, out MonsterDataSO def)
    {
        def = GetById(id);
        return def != null;
    }

    public static void RebuildIndex()
    {
        _byId = null;
        if (_lib == null || _lib.monsters == null) return;

        _byId = new Dictionary<string, MonsterDataSO>(_lib.monsters.Length);
        foreach (var m in _lib.monsters)
        {
            if (m == null || string.IsNullOrEmpty(m.id)) continue;
            if (!_byId.ContainsKey(m.id)) _byId.Add(m.id, m);
        }
    }
}
