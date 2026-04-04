using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class AchievementItemUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI descLabel;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private TextMeshProUGUI progressLabel;

    [Header("State Colors")]
    [SerializeField] private Color unlockedColor = new Color(0.21f, 0.83f, 0.17f, 1f);
    [SerializeField] private Color lockedColor   = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Header("Badges")]
    [SerializeField] private GameObject newBadge;

    public void Bind(AchievementEntrySO entry, AchievementProgressData prog)
    {
        if (entry == null) return;

        bool unlocked = prog != null && prog.unlocked;
        bool showSecret = entry.secretUntilUnlocked && !unlocked;

        if (iconImage) iconImage.sprite = entry.icon;

        if (nameLabel) nameLabel.text = showSecret ? "???" : entry.displayName;
        if (descLabel) descLabel.text = showSecret ? "Unlock this achievement to reveal details." : entry.description;

        int value = prog != null ? prog.value : 0;
        int goal = Mathf.Max(1, entry.goal);

        if (progressSlider)
        {
            progressSlider.minValue = 0;
            progressSlider.maxValue = goal;
            progressSlider.value = Mathf.Clamp(value, 0, goal);
        }

        if (progressLabel)
        {
            if (unlocked) progressLabel.text = $"{goal}/{goal}";
            else progressLabel.text = $"{Mathf.Clamp(value, 0, goal)}/{goal}";
        }

        if (iconImage) iconImage.color = unlocked ? unlockedColor : lockedColor;

        if (newBadge)
            newBadge.SetActive(unlocked && prog != null && !prog.seen);
    }
}
