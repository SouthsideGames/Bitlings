using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Jobs/Fatigue Mult", fileName = "JobFatigueBoosterTitle")]
public sealed class JobFatigueBoosterTitleSO : TitleSO
{
    [Header("While assigned to a Job site")]
    public float fatigueMultiplier = 1f; // <1 less fatigue, >1 more fatigue
}
