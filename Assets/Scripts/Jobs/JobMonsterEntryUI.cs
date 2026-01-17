using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class JobMonsterEntryUI : MonoBehaviour
{
    [Header("Wiring")]
    public Button button;
    public Image icon;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI scoreText;
    public Image typeIcon;

    [Header("Fatigue UI (Optional)")]
    [Tooltip("Root object for a small 'FATIGUED' badge/overlay.")]
    [SerializeField] private GameObject fatiguedRoot;

    [Tooltip("Optional label inside fatiguedRoot (e.g. 'FATIGUED • 3m').")]
    [SerializeField] private TextMeshProUGUI fatiguedLabel;

    [Tooltip("Optional CanvasGroup to dim the whole entry when fatigued.")]
    [SerializeField] private CanvasGroup dimGroup;

    [SerializeField, Range(0.15f, 1f)] private float fatiguedAlpha = 0.55f;

    /// <summary>
    /// Presentation helper: toggles fatigued visuals and disables interaction if fatigued.
    /// Safe to call even if optional refs are unassigned.
    /// </summary>
    public void SetFatigued(bool isFatigued, string etaText = null)
    {
        if (fatiguedRoot) fatiguedRoot.SetActive(isFatigued);

        if (fatiguedLabel)
        {
            if (!isFatigued) fatiguedLabel.text = "";
            else fatiguedLabel.text = string.IsNullOrEmpty(etaText) ? "FATIGUED" : $"FATIGUED • {etaText}";
        }

        if (button) button.interactable = !isFatigued;

        if (dimGroup)
        {
            dimGroup.alpha = isFatigued ? fatiguedAlpha : 1f;
        }
        else
        {
            if (icon)
            {
                var c = icon.color;
                c.a = isFatigued ? fatiguedAlpha : 1f;
                icon.color = c;
            }
        }
    }
}
