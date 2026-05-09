using UnityEngine;

/// <summary>
/// Easing curves for the Promotion ceremony.
/// Extends CeremonyCurves with promotion-specific animations.
/// </summary>
[CreateAssetMenu(fileName = "PromotionCurves", menuName = "Data/Ceremony/Promotion Curves")]
public sealed class PromotionCurves : CeremonyCurves
{
    public LeanTweenType badgePunchIn = LeanTweenType.easeOutBack;
    public LeanTweenType confettiFade = LeanTweenType.easeInQuad;
}
