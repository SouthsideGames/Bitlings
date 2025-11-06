using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Jobs/Capacity +Flat", fileName = "JobCapacityBoosterTitle")]
[Tooltip("Used to define Titles that increase the storage capacity of a specific job site while the wearer is assigned there.")]
public sealed class JobCapacityBoosterTitleSO : TitleSO
{
    [Header("Target")]
    [Tooltip("Job site whose storage capacity will be increased.")]
    public JobType targetJobSite;

    [Header("Capacity Increase")]
    [Tooltip("Flat amount added to the site's storage capacity while assigned.")]
    public int capacityBonusFlat = 0;

    public bool AppliesTo(JobType site) => site == targetJobSite;
}
