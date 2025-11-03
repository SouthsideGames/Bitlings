using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Stat Booster", fileName = "StatBoosterTitle")]
public sealed class StatBoosterTitleSO : TitleSO
{
    [Header("Boost")]
    public StatKind stat = StatKind.Attack;
    public OpKind operation = OpKind.Add;
    public float value = 1f; // Add/Subtract in absolute terms; Multiply/Divide as factor
}
