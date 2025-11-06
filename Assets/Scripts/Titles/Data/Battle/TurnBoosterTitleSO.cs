using UnityEngine;

public enum BattleStatKind { ATK, DEF, SPD, HP }

[CreateAssetMenu(menuName = "Data/Titles/Turn Booster", fileName = "TurnBoosterTitle")]
public sealed class TurnBoosterTitleSO : TitleSO
{
    public BattleStatKind stat = BattleStatKind.ATK;
    [Tooltip("+% per turn, e.g., 5 = +5% per turn")]
    public float percentPerTurn = 5f;
    [Tooltip("Max stacks (turns to scale)")]
    public int maxStacks = 5;
}
