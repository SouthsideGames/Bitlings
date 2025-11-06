using UnityEngine;

public enum EffectivenessMode { Multiply, Add }

[CreateAssetMenu(menuName = "Data/Titles/Effectiveness Mod", fileName = "EffectivenessModTitle")]
[Tooltip("Used to define Titles that modify the effectiveness of type matchups during damage calculation.")]
public sealed class EffectivenessModTitleSO : TitleSO
{
    [Header("Effectiveness Modification")]
    [Tooltip("Choose how this modifier applies: Multiply = scales existing effectiveness, Add = adjusts by a flat value.")]
    public EffectivenessMode mode = EffectivenessMode.Multiply;

    [Tooltip("Multiply mode: 1.0 baseline | Add mode: 0.0 baseline. Example: Multiply 1.10 = +10% effectiveness, Add 0.25 = +0.25 added.")]
    public float amount = 1f;
}