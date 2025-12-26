using UnityEngine;

public enum BattleStatKind { ATK, DEF, SPD, HP }

[CreateAssetMenu(menuName = "Data/Titles/Battle/Turn Booster", fileName = "TurnBoosterTitle")]
[Tooltip("Used to define Titles that increase a chosen stat by a percentage each turn, stacking over time.")]
public sealed class TurnBoosterTitleSO : TitleSO
{
    [Header("Turn Scaling")]
    [Tooltip("Which stat to increase each turn (ATK, DEF, SPD, or HP).")]
    public BattleStatKind stat = BattleStatKind.ATK;

    [Tooltip("Percent bonus gained per turn (e.g., 5 = +5% per turn).")]
    public float percentPerTurn = 5f;

    [Tooltip("Maximum number of turns (stacks) this bonus can accumulate.")]
    public int maxStacks = 5;
}
