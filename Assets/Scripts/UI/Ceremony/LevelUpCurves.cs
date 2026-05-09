using UnityEngine;

/// <summary>
/// Easing curves for the Level-Up ceremony.
/// Extends CeremonyCurves with level-up specific animations.
/// </summary>
[CreateAssetMenu(fileName = "LevelUpCurves", menuName = "Data/Ceremony/Level Up Curves")]
public sealed class LevelUpCurves : CeremonyCurves
{
    public LeanTweenType statLineStagger = LeanTweenType.easeOutQuad;
    public LeanTweenType lightSweep      = LeanTweenType.easeOutQuad;
}
