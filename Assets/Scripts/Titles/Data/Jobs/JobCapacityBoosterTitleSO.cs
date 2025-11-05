using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Jobs/Capacity +Flat", fileName = "JobCapacityBoosterTitle")]
public sealed class JobCapacityBoosterTitleSO : TitleSO
{
    [Header("Only applies while this monster is assigned at this site")]
    public JobType targetJobSite;

    [Header("Increase site storage capacity by a flat amount")]
    public int capacityBonusFlat = 0;

    public bool AppliesTo(JobType site) => site == targetJobSite;
}
