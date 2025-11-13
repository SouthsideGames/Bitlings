using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Battle/Damage Filter", fileName = "DamageFilterTitle")]
[Tooltip("Used to define Titles that reduce or modify incoming damage to the monster wearing it.")]
public sealed class DamageFilterTitleSO : TitleSO
{
    [Header("Incoming Damage Filters")]
    [Tooltip("Flat amount of damage reduced after defense is applied.")]
    public int flatReduce = 0;

    [Tooltip("Multiplier applied to remaining damage after flat reduction. Example: 0.9 = 10% less damage.")]
    public float percentMultiplier = 1f;

    [Tooltip("If true, the wearer cannot receive critical hits.")]
    public bool cannotBeCrit = false;
}
