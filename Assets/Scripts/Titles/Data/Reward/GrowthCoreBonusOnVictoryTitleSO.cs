using UnityEngine;

[CreateAssetMenu(fileName = "GrowthCoreBonusOnVictory", menuName = "Data/Titles/Growth Core Bonus On Victory", order = 101)]
[Tooltip("Used to define Titles that boost growth core rewards gained after winning a battle.")]
public class GrowthCoreBonusOnVictoryTitleSO : TitleSO
{
    [Header("Growth Core Reward Multiplier")]
    [Tooltip("Growth core gain on victory is multiplied by this value (e.g., 1.20 = +20%).")]
    public float growthCoreMultiplier = 1.10f;
}
