using UnityEngine;

[CreateAssetMenu(fileName = "RetirementCurves", menuName = "Data/Ceremony/Retirement Curves")]
public sealed class RetirementCurves : CeremonyCurves
{
    public LeanTweenType trophyPunchIn = LeanTweenType.easeOutBack;
    public LeanTweenType portraitFadeOut = LeanTweenType.easeInCubic;
}
