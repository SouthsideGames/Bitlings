using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Battle/Effectiveness Nullify", fileName = "EffectivenessNullifyTitle")]
[Tooltip("Used to define Titles that reduce or nullify incoming type effectiveness damage multipliers.")]
public sealed class EffectivenessNullifyTitleSO : TitleSO
{
    [Header("Incoming Effectiveness Control")]
    [Tooltip("Multiplier applied to incoming type effectiveness. Values <1 reduce weakness; 0 ignores type advantage entirely.")]
    [Range(0f, 2f)]
    public float incomingEffectivenessMultiplier = 1f;
}
