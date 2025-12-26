// Assets/Scripts/Static/Upgrades.cs
using UnityEngine;

/// <summary>
/// Legacy placeholder (the old Tap/Idle/Crit upgrades).
/// The real progression is now handled by FeatureId + FeatureUnlockManager.
/// </summary>
public enum UpgradeType
{
    None = 0
}

[System.Obsolete("Use FeatureId + FeatureUnlockManager instead.")]
public static class Upgrades
{
    public const int BaseCost = 20;
    public const float Growth = 1.6f;

    public static int CostForLevel(int level)
        => Mathf.CeilToInt(BaseCost * Mathf.Pow(Growth, Mathf.Max(0, level)));
}
