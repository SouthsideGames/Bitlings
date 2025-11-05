using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Jobs/Site Aura %", fileName = "JobAuraTitle")]
public sealed class JobAuraTitleSO : TitleSO
{
    [Header("Target job site where aura applies")]
    public JobType targetJobSite;

    [Header("Additive aura percent to site output (e.g., 5 = +5%)")]
    public float siteAuraPercent = 0f;

    public bool AppliesTo(JobType site)
    {
        return site == targetJobSite;
    }
}
