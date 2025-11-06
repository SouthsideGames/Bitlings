using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Battle Start Shield", fileName = "BattleStartShieldTitle")]
[Tooltip("Used to define Titles that grant a starting shield based on the monster's Max HP when battle begins.")]
public sealed class BattleStartShieldTitleSO : TitleSO
{
    [Header("Shield Properties")]
    [Tooltip("Percent of Max HP granted as a shield when battle starts (e.g., 15 = 15%).")]
    public float shieldPct = 15f;
}
