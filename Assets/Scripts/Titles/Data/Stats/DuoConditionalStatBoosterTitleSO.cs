using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Conditional Dual Stat Booster", fileName = "DualConditionalBoosterTitle")]
[Tooltip("Used to define Titles that boost two stats simultaneously under certain conditions (e.g., low HP, ally count, win streak).")]
public sealed class DuoConditionalStatBoosterTitleSO : TitleSO
{
    [Header("Condition Settings")]
    [Tooltip("Type of condition that triggers the dual stat bonuses.")]
    public ConditionKind condition = ConditionKind.HealthBelowPercent;

    [Tooltip("Threshold for HP-based conditions (0–1). Example: 0.5 = below 50% HP.")]
    [Range(0f, 1f)] public float threshold01 = 0.5f;

    [Tooltip("Used for conditions like AllyCountBelow or WinStreakAbove.")]
    public int countN = 1;

    [Header("Primary Stat Boost")]
    [Tooltip("First stat affected when the condition is met.")]
    public StatKind statA = StatKind.Attack;

    [Tooltip("Operation for the first stat (Add, Subtract, Multiply, Divide).")]
    public OpKind opA = OpKind.Add;

    [Tooltip("Value applied to the first stat.")]
    public float valueA = 1f;

    [Header("Secondary Stat Boost")]
    [Tooltip("Second stat affected when the condition is met.")]
    public StatKind statB = StatKind.Defense;

    [Tooltip("Operation for the second stat (Add, Subtract, Multiply, Divide).")]
    public OpKind opB = OpKind.Multiply;

    [Tooltip("Value applied to the second stat (e.g., 1.10 = +10%).")]
    public float valueB = 1.10f;
}
