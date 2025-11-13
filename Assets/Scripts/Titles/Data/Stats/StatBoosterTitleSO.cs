using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Stat/Stat Booster", fileName = "StatBoosterTitle")]
[Tooltip("Used to define Titles that apply a single, always-active stat boost to the wearer.")]
public sealed class StatBoosterTitleSO : TitleSO
{
    [Header("Stat Boost Settings")]
    [Tooltip("Which stat this Title boosts (HP, Attack, Defense, or Speed).")]
    public StatKind stat = StatKind.Attack;

    [Tooltip("How the stat is modified (Add/Subtract absolute value, Multiply/Divide as factor).")]
    public OpKind operation = OpKind.Add;

    [Tooltip("Magnitude of the modification (interpretation depends on operation type).")]
    public float value = 1f;
}
