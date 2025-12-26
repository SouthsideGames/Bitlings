// Assets/Scripts/Monster/Packs/Seasons/MonsterPackSeasonLocator.cs
using UnityEngine;

public static class MonsterPackSeasonLocator
{
    private const string ResourcePath = "MonsterPackSeasons"; // Resources/MonsterPackSeasons.asset
    private static MonsterPackSeasonRotationSO _seasons;

    public static MonsterPackSeasonRotationSO Seasons
    {
        get
        {
            if (_seasons == null)
            {
                _seasons = Resources.Load<MonsterPackSeasonRotationSO>(ResourcePath);
#if UNITY_EDITOR
                if (_seasons == null)
                    Debug.LogWarning($"[MonsterPackSeasonLocator] Not found at Resources/{ResourcePath}.asset");
#endif
            }
            return _seasons;
        }
        set { _seasons = value; }
    }
}
