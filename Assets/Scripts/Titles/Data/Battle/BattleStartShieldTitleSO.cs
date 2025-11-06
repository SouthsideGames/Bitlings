using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Battle Start Shield", fileName = "BattleStartShieldTitle")]
public sealed class BattleStartShieldTitleSO : TitleSO
{
    [Tooltip("Percent of Max HP as shield, e.g., 15 = 15%")]
    public float shieldPct = 15f;
}