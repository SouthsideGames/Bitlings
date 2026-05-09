using UnityEngine;

[CreateAssetMenu(fileName = "EvolutionCurves", menuName = "Data/Ceremony/Evolution Curves")]
public sealed class EvolutionCurves : CeremonyCurves
{
    public LeanTweenType portraitFlashIn = LeanTweenType.easeOutQuad;
    public LeanTweenType newFormReveal   = LeanTweenType.easeOutCubic;
}
