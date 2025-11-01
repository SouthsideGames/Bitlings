using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Conditional Stat Booster", fileName = "ConditionalStatBoosterTitle")]
public sealed class ConditionalStatBoosterTitleSO : TitleSO
{
    [Header("Condition")]
    public ConditionKind condition = ConditionKind.HealthBelowPercent;
    [Range(0f, 1f)] public float threshold01 = 0.5f; // used for HealthBelow/Above
    public int countN = 1;                           // used for AllyCountBelow / WinStreakAbove

    [Header("Boost")]
    public StatKind stat = StatKind.Attack;
    public OpKind operation = OpKind.Add;
    public float value = 1f;
}
