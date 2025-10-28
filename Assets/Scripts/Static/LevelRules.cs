using UnityEngine;

public static class LevelRules
{
    public const int MaxLevel = 50;

    public static int XPToNext(int currentLevel)
    {
        if (currentLevel >= MaxLevel) return int.MaxValue;
        return Mathf.CeilToInt(100f * Mathf.Pow(1.25f, currentLevel - 1));
    }
}
