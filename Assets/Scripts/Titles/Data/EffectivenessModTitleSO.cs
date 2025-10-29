using UnityEngine;

[CreateAssetMenu(menuName = "Data/Titles/Effectiveness Mult", fileName = "EffectivenessModTitle")]
public sealed class EffectivenessModTitleSO : TitleSO
{
    [Header("Multiply final type effectiveness (e.g., 1.1 = +10%)")]
    public float effectivenessMultiplier = 1f;
}
