// Assets/Scripts/Titles/TitleSO.cs
using UnityEngine;

public abstract class TitleSO : ScriptableObject
{
    [Header("Identity")]
    public string titleId;          // unique key
    public string displayName;      // shown in UI
    [TextArea] public string description;
}

[CreateAssetMenu(menuName = "Data/Titles/Stat Booster", fileName = "StatBoosterTitle")]
public sealed class StatBoosterTitleSO : TitleSO
{
    [Header("Boost")]
    public StatKind stat = StatKind.Attack;
    public OpKind operation = OpKind.Add;
    public float value = 1f; // Add/Subtract in absolute terms; Multiply/Divide as factor
}

[CreateAssetMenu(menuName = "Data/Titles/Conditional Stat Booster", fileName = "ConditionalBoosterTitle")]
public sealed class ConditionalBoosterTitleSO : TitleSO
{
    [Header("Condition")]
    public ConditionKind condition = ConditionKind.HealthBelowPercent;
    [Range(0f,1f)] public float threshold01 = 0.5f; // used for HealthBelow/Above
    public int countN = 1;                          // used for AllyCountBelow / WinStreakAbove

    [Header("Boost")]
    public StatKind stat = StatKind.Attack;
    public OpKind operation = OpKind.Add;
    public float value = 1f;
}

[CreateAssetMenu(menuName = "Data/Titles/Effectiveness Mult", fileName = "EffectivenessModTitle")]
public sealed class EffectivenessModTitleSO : TitleSO
{
    [Header("Multiply final type effectiveness (e.g., 1.1 = +10%)")]
    public float effectivenessMultiplier = 1f;
}

[CreateAssetMenu(menuName = "Data/Titles/Damage Filter", fileName = "DamageFilterTitle")]
public sealed class DamageFilterTitleSO : TitleSO
{
    [Header("Incoming Damage Filters")]
    public int flatReduce = 0;           // subtract after defense
    public float percentMultiplier = 1f; // multiply after flat (0.9 = 10% less)
    public bool cannotBeCrit = false;    // true => negate crits against wearer
}

[CreateAssetMenu(menuName = "Data/Titles/Jobs/Fatigue Mult", fileName = "JobFatigueBoosterTitle")]
public sealed class JobFatigueBoosterTitleSO : TitleSO
{
    [Header("While assigned to a Job site")]
    public float fatigueMultiplier = 1f; // <1 less fatigue, >1 more fatigue
}

[CreateAssetMenu(menuName = "Data/Titles/Jobs/Site Aura %", fileName = "JobAuraTitle")]
public sealed class JobAuraTitleSO : TitleSO
{
    [Header("Additive aura percent to site output (e.g., 5 = +5%)")]
    public float siteAuraPercent = 0f;
}

[CreateAssetMenu(menuName = "Data/Titles/Jobs/Capacity +Flat", fileName = "JobCapacityBoosterTitle")]
public sealed class JobCapacityBoosterTitleSO : TitleSO
{
    [Header("Increase site storage capacity by a flat amount")]
    public int capacityBonusFlat = 0;
}
