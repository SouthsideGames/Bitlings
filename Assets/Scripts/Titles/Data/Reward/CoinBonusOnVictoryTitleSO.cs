using UnityEngine;

[CreateAssetMenu(fileName = "creditBonusOnVictory", menuName = "Data/Titles/Reward/credit Bonus On Victory", order = 100)]
[Tooltip("Used to define Titles that increase credit rewards earned after a victorious battle.")]
public class creditBonusOnVictoryTitleSO : TitleSO
{
    [Header("credit Reward Multiplier")]
    [Tooltip("Final credits gained on victory are multiplied by this value (e.g., 1.25 = +25%).")]
    public float creditMultiplier = 1.10f;
}
