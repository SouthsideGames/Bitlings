using UnityEngine;

[CreateAssetMenu(fileName = "DualStatBoosterTitle", menuName = "Data/Titles/Dual Stat Booster", order = 210)]
[Tooltip("Used to define Titles that apply two stat modifiers at all times (unconditional dual stat bonuses).")]
public sealed class DuoStatBoosterTitleSO : TitleSO
{
    [Header("Primary Effect")]
    [Tooltip("The first stat affected by this Title.")]
    public StatKind statA = StatKind.Attack;

    [Tooltip("How the primary stat is modified (Add/Subtract in absolute terms, Multiply/Divide as a factor).")]
    public OpKind opA = OpKind.Add;

    [Tooltip("Value applied to the primary stat.")]
    public float valueA = 1f;

    [Header("Secondary Effect")]
    [Tooltip("The second stat affected by this Title.")]
    public StatKind statB = StatKind.Defense;

    [Tooltip("How the secondary stat is modified (Add/Subtract/Multiply/Divide).")]
    public OpKind opB = OpKind.Subtract;

    [Tooltip("Value applied to the secondary stat.")]
    public float valueB = 1f;

    [Header("Notes")]
    [Tooltip("If true, applies both A and B whenever either stat is evaluated.")]
    public bool enabled = true;
}
