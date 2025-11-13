using UnityEngine;

[CreateAssetMenu(fileName = "CoinBonusOnVictory", menuName = "Data/Titles/Reward/Coin Bonus On Victory", order = 100)]
[Tooltip("Used to define Titles that increase coin rewards earned after a victorious battle.")]
public class CoinBonusOnVictoryTitleSO : TitleSO
{
    [Header("Coin Reward Multiplier")]
    [Tooltip("Final coins gained on victory are multiplied by this value (e.g., 1.25 = +25%).")]
    public float coinMultiplier = 1.10f;
}
