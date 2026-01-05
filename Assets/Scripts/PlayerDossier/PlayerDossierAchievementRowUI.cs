using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class PlayerDossierAchievementRowUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descText;

    [SerializeField] private Slider progressSlider;
    [SerializeField] private TextMeshProUGUI progressText;

    [Header("State Badges")]
    [SerializeField] private GameObject lockedGroup;    // optional: dim overlay
    [SerializeField] private GameObject unlockedGroup;  // optional: checkmark
    [SerializeField] private GameObject newBadge;       // optional: "NEW"

    public void Bind(AchievementRowSnapshot row)
    {
        if (row == null)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        if (iconImage) iconImage.sprite = row.icon;
        if (nameText)  nameText.text = row.name;
        if (descText)  descText.text = row.description;

        int goal  = Mathf.Max(1, row.goal);
        int value = Mathf.Clamp(row.value, 0, goal);

        if (progressSlider)
        {
            progressSlider.minValue = 0;
            progressSlider.maxValue = goal;
            progressSlider.value = value;
        }

        if (progressText)
            progressText.text = row.unlocked ? $"{goal}/{goal}" : $"{value}/{goal}";

        if (lockedGroup)   lockedGroup.SetActive(!row.unlocked);
        if (unlockedGroup) unlockedGroup.SetActive(row.unlocked);
        if (newBadge)      newBadge.SetActive(row.unlocked && row.isNew);
    }
}
