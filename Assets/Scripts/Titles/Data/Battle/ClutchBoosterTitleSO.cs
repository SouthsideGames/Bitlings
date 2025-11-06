using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Clutch Booster", fileName = "ClutchBoosterTitle")]
[Tooltip("Used to define Titles that boost stats when the monster's HP falls below a certain threshold.")]
public sealed class ClutchBoosterTitleSO : TitleSO
{
    [Header("Activation Threshold")]
    [Tooltip("HP fraction (0–1) below which the bonuses activate. Example: 0.25 = activates below 25% HP.")]
    [Range(0f, 1f)]
    public float hpBelowThreshold01 = 0.25f;

    [Header("Stat Bonuses When Below Threshold")]
    [Tooltip("Percent ATK increase when below threshold.")]
    public float atkPct = 0f;

    [Tooltip("Percent DEF increase when below threshold.")]
    public float defPct = 0f;

    [Tooltip("Percent SPD increase when below threshold.")]
    public float spdPct = 0f;

    [Tooltip("Optional multiplier applied to outgoing damage while below threshold.")]
    public float dmgMult = 1f;
}
