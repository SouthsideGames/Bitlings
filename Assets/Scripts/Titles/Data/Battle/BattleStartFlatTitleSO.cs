using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Battle Start Flat", fileName = "BattleStartFlatTitle")]
[Tooltip("Used to define Titles that grant a temporary flat stat bonus at the start of battle.")]
public sealed class BattleStartFlatTitleSO : TitleSO
{
    [Header("Flat Stat Bonus")]
    [Tooltip("Which stat this bonus applies to (ATK, DEF, SPD, HP).")]
    public BattleStatKind stat = BattleStatKind.ATK;

    [Tooltip("Flat bonus value applied for the specified number of turns.")]
    public int flatAmount = 10;

    [Tooltip("How many turns the bonus lasts (0/1 = this turn only).")]
    public int durationTurns = 1;
}
