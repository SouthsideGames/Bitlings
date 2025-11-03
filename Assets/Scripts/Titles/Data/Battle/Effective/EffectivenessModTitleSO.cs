using UnityEngine;

public enum EffectivenessMode { Multiply, Add }

[CreateAssetMenu(menuName = "Data/Titles/Effectiveness Mod", fileName = "EffectivenessModTitle")]
public sealed class EffectivenessModTitleSO : TitleSO
{
    [Header("Multiply: 1.10 = +10%  |  Add: 0.25 = +0.25 to type effectiveness")]
    public EffectivenessMode mode = EffectivenessMode.Multiply;

    // Multiply mode: 1.0 baseline
    // Add mode: 0.0 baseline (this will be added to ResolveHit()'s effectiveness number)
    public float amount = 1f;
}
