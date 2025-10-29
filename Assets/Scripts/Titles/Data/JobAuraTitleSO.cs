using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Jobs/Site Aura %", fileName = "JobAuraTitle")]
public sealed class JobAuraTitleSO : TitleSO
{
    [Header("Additive aura percent to site output (e.g., 5 = +5%)")]
    public float siteAuraPercent = 0f;
}
