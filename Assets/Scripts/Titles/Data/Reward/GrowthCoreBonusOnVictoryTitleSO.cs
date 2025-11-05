using UnityEngine;

[CreateAssetMenu(fileName = "GrowthCoreBonusOnVictory", menuName = "Data/Titles/Growth Core Bonus On Victory", order = 101)]
public class GrowthCoreBonusOnVictoryTitleSO : TitleSO
{
    [Header("Experience")]
    [Tooltip("Growth on victory is multiplied by this value (e.g., 1.20 = +20%).")]
    public float growthCoreMultiplier = 1.10f; // TitleManager reads xpMultiplier
}
