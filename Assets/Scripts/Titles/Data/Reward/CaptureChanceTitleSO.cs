using UnityEngine;

[CreateAssetMenu(fileName = "CaptureChance", menuName = "Data/Titles/Capture Chance Boost", order = 102)]
public class CaptureChanceTitleSO : TitleSO
{
    [Header("Capture")]
    [Tooltip("Capture chance is multiplied by this value (e.g., 1.15 = +15%).")]
    public float chanceMultiplier = 1.10f;
}
