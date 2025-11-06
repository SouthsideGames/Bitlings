using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Conditional Stat Booster", fileName = "ConditionalStatBoosterTitle")]
[Tooltip("Used to define Titles that boost specific stats when certain conditions are met, such as low HP or win streaks.")]
public sealed class ConditionalStatBoosterTitleSO : TitleSO
{
    [Header("Condition Settings")]
    [Tooltip("Type of condition that activates the stat boost (e.g., HealthBelowPercent, AllyCountBelow, WinStreakAbove).")]
    public ConditionKind condition = ConditionKind.HealthBelowPercent;

    [Tooltip("Threshold value (0–1) used for HP-based conditions. Example: 0.5 = below 50% HP.")]
    [Range(0f, 1f)] public float threshold01 = 0.5f;

    [Tooltip("Used by count-based conditions like AllyCountBelow or WinStreakAbove.")]
    public int countN = 1;

    [Header("Boost Settings")]
    [Tooltip("Which stat will be modified when the condition is met.")]
    public StatKind stat = StatKind.Attack;

    [Tooltip("How the stat will be modified (Add, Subtract, Multiply, Divide).")]
    public OpKind operation = OpKind.Add;

    [Tooltip("Magnitude of the boost (interpretation depends on operation type).")]
    public float value = 1f;
}
