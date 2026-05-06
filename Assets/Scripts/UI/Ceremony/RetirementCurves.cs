using UnityEngine;

[CreateAssetMenu(fileName = "RetirementCurves", menuName = "Data/Retirement/Retirement Curves")]
public sealed class RetirementCurves : ScriptableObject
{
    public LeanTweenType portraitScaleIn = LeanTweenType.easeOutQuad;
    public LeanTweenType nameTextFloat = LeanTweenType.easeOutCubic;
    public LeanTweenType trophyPunchIn = LeanTweenType.easeOutBack;
    public LeanTweenType vignetteIn = LeanTweenType.easeInQuad;
    public LeanTweenType portraitFadeOut = LeanTweenType.easeInCubic;
    public LeanTweenType lightSweep = LeanTweenType.easeOutQuad;
}
