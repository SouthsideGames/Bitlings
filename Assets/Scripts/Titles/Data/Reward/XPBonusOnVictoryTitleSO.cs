using UnityEngine;

[CreateAssetMenu(fileName = "XPBonusOnVictory", menuName = "Data/Titles/XP Bonus On Victory", order = 101)]
public class XPBonusOnVictoryTitleSO : TitleSO
{
    [Header("Experience")]
    [Tooltip("XP on victory is multiplied by this value (e.g., 1.20 = +20%).")]
    public float xpMultiplier = 1.10f; // TitleManager reads xpMultiplier
}
