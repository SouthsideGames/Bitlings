using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Battle Start Flat", fileName = "BattleStartFlatTitle")]
public sealed class BattleStartFlatTitleSO : TitleSO
{
    public BattleStatKind stat = BattleStatKind.ATK;
    [Tooltip("Flat bonus applied for durationTurns")]
    public int flatAmount = 10;
    [Tooltip("Turns this bonus lasts (0/1 = this turn only)")]
    public int durationTurns = 1;
}
