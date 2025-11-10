using UnityEngine;
using System.Collections.Generic;

public static class MonsterLibraryLocator
{
    private const string ResourcePath = "MonsterLibrary"; // → Assets/Resources/MonsterLibrary.asset
    private static MonsterLibrarySO _lib;
    private static Dictionary<string, MonsterDataSO> _byId;


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

    /// <summary>Gets a MonsterDataSO by ID, using cached dictionary if available.</summary>
    public static MonsterDataSO GetById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId == null) RebuildIndex();

        // Primary lookup
        if (_byId != null && _byId.TryGetValue(id, out var def))
            return def;

        // Secondary fallback (direct query from Lib)
        return _lib != null ? _lib.GetById(id) : null;
    }

    /// <summary>Returns true if the given monster ID exists in the library.</summary>
    public static bool TryGet(string id, out MonsterDataSO def)
    {
        def = GetById(id);
        return def != null;
    }

    /// <summary>Clears and rebuilds the ID→MonsterData dictionary.</summary>
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

    /// <summary>For editor or runtime diagnostics. Returns all loaded monsters.</summary>
    public static IReadOnlyDictionary<string, MonsterDataSO> GetAll() => _byId;
}
