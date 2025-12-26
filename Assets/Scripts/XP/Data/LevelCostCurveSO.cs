using UnityEngine;

[CreateAssetMenu(menuName = "Data/XP System/Level Cost Curve", fileName = "LevelCostCurve")]
public class LevelCostCurveSO : ScriptableObject
{
    [Header("If array is too short, last value repeats.")]
    [Tooltip("Cores required to go from L -> L+1. Index 0 = cost from L1 to L2.")]
    public int[] coresPerLevel = new int[] { 1,2,3,4,5,6,7,8,9,10 };

    [Header("Optional scale when past table end")]
    public float endScale = 1.15f;

    public int CoresToNextLevel(int currentLevel)
    {
        if (coresPerLevel == null || coresPerLevel.Length == 0) return 1;
        int idx = Mathf.Clamp(currentLevel - 1, 0, coresPerLevel.Length - 1);
        int baseCost = coresPerLevel[idx];
        if (currentLevel - 1 >= coresPerLevel.Length - 1)
        {
            int extra = (currentLevel - 1) - (coresPerLevel.Length - 1);
            baseCost = Mathf.CeilToInt(baseCost * Mathf.Pow(endScale, extra));
        }
        return Mathf.Max(1, baseCost);
    }
}
