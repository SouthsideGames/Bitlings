using UnityEngine;

[CreateAssetMenu(fileName = "CoinBonusOnVictory", menuName = "Data/Titles/Coin Bonus On Victory", order = 100)]
public class CoinBonusOnVictoryTitleSO : TitleSO
{
    [Header("Coins")]
    [Tooltip("Final coins on victory are multiplied by this value (e.g., 1.25 = +25%).")]
    public float coinMultiplier = 1.10f; // matches TitleManager.TryReadFloat fallback names
}
