using UnityEngine;

[CreateAssetMenu(fileName = "CreditBonusOnVictory", menuName = "Data/Titles/Reward/Credit Bonus On Victory", order = 100)]
[Tooltip("Used to define Titles that increase credit rewards earned after a victorious battle.")]
public class CreditBonusOnVictoryTitleSO : TitleSO
{
    [Header("Credit Reward Multiplier")]
    [Tooltip("Final credits gained on victory are multiplied by this value (e.g., 1.25 = +25%).")]
    public float CreditMultiplier = 1.10f;
}
