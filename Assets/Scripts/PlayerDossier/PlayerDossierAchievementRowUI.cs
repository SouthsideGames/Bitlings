using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class PlayerDossierAchievementRowUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image bgImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descText;

    [SerializeField] private Slider progressSlider;
    [SerializeField] private TextMeshProUGUI progressText;

    [Header("State Colors")]
    [SerializeField] private Color unlockedColor = new Color(0.21f, 0.83f, 0.17f, 1f);
    [SerializeField] private Color lockedColor   = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Header("Badges")]
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
            progressSlider.gameObject.SetActive(!row.unlocked);
            progressSlider.minValue = 0;
            progressSlider.maxValue = goal;
            progressSlider.value = value;
        }

        if (progressText)
            progressText.text = row.unlocked ? $"{goal}/{goal}" : $"{value}/{goal}";

        if (bgImage)   bgImage.color = row.unlocked ? unlockedColor : lockedColor;
        if (newBadge)  newBadge.SetActive(row.unlocked && row.isNew);
    }
}
