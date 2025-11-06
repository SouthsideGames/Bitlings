using UnityEngine;

[CreateAssetMenu(fileName = "ConditionalJobRateBooster", menuName = "Data/Titles/Conditional Job Rate Booster", order = 103)]
[Tooltip("Used to define Titles that boost a monster's job production rate while actively assigned to a job site. Optionally restrict to a specific job type.")]
public class ConditionalJobRateBoosterTitleSO : TitleSO
{
    [Header("Rate Boost")]
    [Tooltip("Production rate multiplier while this monster is assigned to any job site (e.g., 1.15 = +15%).")]
    public float rateMultiplier = 1.10f; // TitleManager reads this field directly

    [Header("Restriction (Optional)")]
    [Tooltip("If not None, the boost only applies when assigned at this specific job site.")]
    public JobType restrictTo = JobType.None;
}
