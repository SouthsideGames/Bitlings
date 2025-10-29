using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Damage Filter", fileName = "DamageFilterTitle")]
public sealed class DamageFilterTitleSO : TitleSO
{
    [Header("Incoming Damage Filters")]
    public int flatReduce = 0;           // subtract after defense
    public float percentMultiplier = 1f; // multiply after flat (0.9 = 10% less)
    public bool cannotBeCrit = false;    // true => negate crits against wearer
}
