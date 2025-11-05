using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Jobs/Fatigue Mult", fileName = "JobFatigueBoosterTitle")]
public sealed class JobFatigueBoosterTitleSO : TitleSO
{
    [Header("Only applies while this monster is assigned at this site")]
    public JobType targetJobSite;

    [Header("Per-hour fatigue multiplier (<1 = slower fatigue, >1 = faster)")]
    public float fatigueMultiplier = 1f;

    public bool AppliesTo(JobType site) => site == targetJobSite;
}
