using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Jobs/Fatigue Mult", fileName = "JobFatigueBoosterTitle")]
[Tooltip("Used to define Titles that modify how quickly a monster accumulates job fatigue at a specific site while assigned.")]
public sealed class JobFatigueBoosterTitleSO : TitleSO
{
    [Header("Target")]
    [Tooltip("Job site where this fatigue modifier applies.")]
    public JobType targetJobSite;

    [Header("Fatigue Rate")]
    [Tooltip("Per-hour fatigue multiplier (<1 = slower fatigue, >1 = faster fatigue).")]
    public float fatigueMultiplier = 1f;

    public bool AppliesTo(JobType site) => site == targetJobSite;
}
