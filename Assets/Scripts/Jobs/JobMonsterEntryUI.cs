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

    [Header("Assignment UI (Optional)")]
    public TextMeshProUGUI assignedText;

    [Header("Fatigue UI (Optional)")]
    [SerializeField] private GameObject fatiguedRoot;
    [SerializeField] private TextMeshProUGUI fatiguedLabel;
    [SerializeField] private CanvasGroup dimGroup;
    [SerializeField, Range(0.15f, 1f)] private float fatiguedAlpha = 0.55f;

    void Awake()
    {
        // Hide once at creation so prefabs don't default-show it.
        ClearAssignmentUI();
    }

    void OnDisable()
    {
        // Optional: if you reuse entries via pooling, reset on disable.
        ClearAssignmentUI();
    }

    public void SetFatigued(bool isFatigued, string etaText = null)
    {
        if (fatiguedRoot) fatiguedRoot.SetActive(isFatigued);

        if (fatiguedLabel)
        {
            if (!isFatigued) fatiguedLabel.text = "";
            else fatiguedLabel.text = string.IsNullOrEmpty(etaText) ? "FATIGUED" : $"FATIGUED • {etaText}";
        }

        if (button) button.interactable = !isFatigued;

        if (dimGroup) dimGroup.alpha = isFatigued ? fatiguedAlpha : 1f;
        else if (icon)
        {
            var c = icon.color;
            c.a = isFatigued ? fatiguedAlpha : 1f;
            icon.color = c;
        }
    }

    public void SetAssignment(JobType job, int slotIndex, bool hide)
    {
        if (!assignedText) return;

        if (hide || job == JobType.None)
        {
            ClearAssignmentUI();
            return;
        }

        assignedText.gameObject.SetActive(true);
        assignedText.text = $"Assigned: {job} (Slot {slotIndex + 1})";
    }

    private void ClearAssignmentUI()
    {
        if (!assignedText) return;
        assignedText.text = "";
        assignedText.gameObject.SetActive(false);
    }
}
