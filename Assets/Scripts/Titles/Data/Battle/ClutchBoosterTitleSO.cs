using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Clutch Booster", fileName = "ClutchBoosterTitle")]
public sealed class ClutchBoosterTitleSO : TitleSO
{
    [Range(0f,1f)] public float hpBelowThreshold01 = 0.25f;
    [Tooltip("+% ATK when below threshold (set the ones you want)")]
    public float atkPct = 0f;
    public float defPct = 0f;
    public float spdPct = 0f;
    public float dmgMult = 1f; // optional: multiply outgoing damage
}
