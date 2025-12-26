using UnityEngine;

public static class MonsterPackLibraryLocator
{
    private const string ResourcePath = "MonsterPackLibrary"; // Resources/MonsterPackLibrary.asset
    private static MonsterPackLibrarySO _lib;

    public static MonsterPackLibrarySO Lib
    {
        get
        {
            if (_lib == null)
            {
                _lib = Resources.Load<MonsterPackLibrarySO>(ResourcePath);
#if UNITY_EDITOR
                if (_lib == null)
                    Debug.LogWarning($"[MonsterPackLibraryLocator] Not found at Resources/{ResourcePath}.asset");
#endif
            }
            return _lib;
        }
        set
        {
            _lib = value;
        }
    }
}
