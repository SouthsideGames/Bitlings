using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple UI row for the Player Dossier > Ranks page.
/// </summary>
public sealed class PromotionRankRowUI : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private TextMeshProUGUI rankLabel;
    [SerializeField] private TextMeshProUGUI xpLabel;
    [SerializeField] private TextMeshProUGUI rewardLabel;
    [SerializeField] private Image stateIcon;

    [Header("Optional State Sprites")]
    [SerializeField] private Sprite unlockedSprite;
    [SerializeField] private Sprite lockedSprite;
    [SerializeField] private Sprite currentSprite;
    [SerializeField] private Image passedImage;

    public void Bind(int rank, bool isUnlocked, bool isCurrent, int totalXpToReach, string displayName, string rewardSummary)
    {
        if (rankLabel)
        {
            string name = string.IsNullOrEmpty(displayName) ? $"Rank {rank}" : displayName;
            rankLabel.text = isCurrent ? $"{name}  <color=#7CFF7C>(CURRENT)</color>" : name;
        }

        if (xpLabel)
        {
            if (rank <= 1) xpLabel.text = "XP: 0";
            else xpLabel.text = $"XP: {Mathf.Max(0, totalXpToReach)}";
        }

        if (rewardLabel)
            rewardLabel.text = string.IsNullOrEmpty(rewardSummary) ? "" : rewardSummary;

        if (stateIcon)
        {
            bool isPassed = isUnlocked && !isCurrent;
            if (isCurrent && currentSprite != null) stateIcon.sprite = currentSprite;
            else if (isUnlocked && unlockedSprite != null) stateIcon.sprite = unlockedSprite;
            else if (!isUnlocked && lockedSprite != null) stateIcon.sprite = lockedSprite;

            // Keep state icon fully visible; passed dimming is handled by passedImage.
            stateIcon.color = new Color(stateIcon.color.r, stateIcon.color.g, stateIcon.color.b, 1f);
        }

        if (passedImage)
        {
            bool isPassed = isUnlocked && !isCurrent;
            float alpha = isPassed ? 0.3f : 1f;
            passedImage.color = new Color(passedImage.color.r, passedImage.color.g, passedImage.color.b, alpha);
        }
    }
}
