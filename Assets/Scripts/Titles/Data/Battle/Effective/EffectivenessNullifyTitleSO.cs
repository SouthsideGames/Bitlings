using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Effectiveness Nullify", fileName = "EffectivenessNullifyTitle")]
public sealed class EffectivenessNullifyTitleSO : TitleSO
{
    [Header("Multiply incoming effectiveness (e.g., 0.5 = halve type weakness)")]
    [Tooltip("Values <1 reduce type advantage; 0 ignores type effectiveness completely.")]
    [Range(0f, 2f)]
    public float incomingEffectivenessMultiplier = 1f;
}