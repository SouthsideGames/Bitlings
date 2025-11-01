using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Conditional Dual Stat Booster", fileName = "DualConditionalBoosterTitle")]
public sealed class DualConditionalBoosterTitleSO : TitleSO
{
    [Header("Condition")]
    public ConditionKind condition = ConditionKind.HealthBelowPercent;
    [Range(0f, 1f)] public float threshold01 = 0.5f; // used by HealthBelow/Above
    public int countN = 1;                            // used by AllyCountBelow / WinStreakAbove

    [Header("Boost A")]
    public StatKind statA = StatKind.Attack;
    public OpKind opA = OpKind.Add;      // Add/Sub/Mul/Div (your TitleUtility.ApplyOp handles this)
    public float valueA = 1f;

    [Header("Boost B")]
    public StatKind statB = StatKind.Defense;
    public OpKind opB = OpKind.Multiply;  // Add/Sub/Mul/Div (your TitleUtility.ApplyOp handles this)
    public float valueB = 1.10f;         // e.g., +10%
}
