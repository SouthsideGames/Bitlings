using UnityEngine;

/// <summary>
/// Base curves ScriptableObject shared by all ceremony types.
/// Subclasses extend this to add ceremony-specific easing curves.
/// </summary>
[CreateAssetMenu(fileName = "CeremonyCurves", menuName = "Data/Ceremony/Base Curves")]
public class CeremonyCurves : ScriptableObject
{
    public LeanTweenType portraitScaleIn = LeanTweenType.easeOutQuad;
    public LeanTweenType vignetteIn     = LeanTweenType.easeInQuad;
    public LeanTweenType nameTextFloat  = LeanTweenType.easeOutCubic;
    public LeanTweenType namePunchIn    = LeanTweenType.easeOutBack;
    public LeanTweenType fadeOut        = LeanTweenType.easeInCubic;
}
