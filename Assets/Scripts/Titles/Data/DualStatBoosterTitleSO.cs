using UnityEngine;

[CreateAssetMenu(fileName = "DualStatBoosterTitle",menuName = "Data/Titles/Dual Stat Booster",order = 210)]
public sealed class DualStatBoosterTitleSO : TitleSO
{
    [Header("Primary Effect")]
    public StatKind statA = StatKind.Attack;
    public OpKind   opA   = OpKind.Add;     // Add/Subtract in absolute; Multiply/Divide as factor
    public float    valueA = 1f;

    [Header("Secondary Effect")]
    public StatKind statB = StatKind.Defense;
    public OpKind   opB   = OpKind.Subtract;
    public float    valueB = 1f;

    [Header("Notes")]
    [Tooltip("If true, applies both A and B when the corresponding stat is being evaluated.")]
    public bool enabled = true;
}
