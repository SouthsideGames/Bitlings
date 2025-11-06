using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Jobs/Site Aura %", fileName = "JobAuraTitle")]
[Tooltip("Used to define Titles that provide an additive aura percentage to a specific job site's overall output while the wearer is assigned there.")]
public sealed class JobAuraTitleSO : TitleSO
{
    [Header("Target")]
    [Tooltip("Job site where this aura applies.")]
    public JobType targetJobSite;

    [Header("Aura Effect")]
    [Tooltip("Additive percent applied to the site's output (e.g., 5 = +5%).")]
    public float siteAuraPercent = 0f;

    public bool AppliesTo(JobType site)
    {
        return site == targetJobSite;
    }
}
