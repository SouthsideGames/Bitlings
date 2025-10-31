using UnityEngine;

[CreateAssetMenu(fileName = "JobRateBooster", menuName = "Data/Titles/Job Rate Booster", order = 103)]
public class JobRateBoosterTitleSO : TitleSO
{
    [Header("Jobs")]
    [Tooltip("Production rate multiplier while this monster is assigned to any job site (e.g., 1.15 = +15%).")]
    public float rateMultiplier = 1.10f; // TitleManager reads this field directly

    [Tooltip("(Optional) If set to something other than None, the boost only applies at this job type.")]
    public JobType restrictTo = JobType.None;
}
