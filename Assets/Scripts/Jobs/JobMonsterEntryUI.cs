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

    [Header("Rest / Cooldown UI (Optional)")]
    [Tooltip("Optional fill image (radial or horizontal) showing recovery progress 0..1.")]
    [SerializeField] private Image restFill;
    [Tooltip("Optional lock root shown while resting/cooling down.")]
    [SerializeField] private GameObject restLockRoot;
    [Tooltip("Optional small text shown while resting/cooling down (e.g., '3h 46m').")]
    [SerializeField] private TextMeshProUGUI restTimeText;

    [Header("Tooltip (Optional)")]
    [Tooltip("If assigned, long-press will show details (rest/cooldown/assignment).")]
    [SerializeField] private TooltipTrigger tooltip;

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

        // Also drive the optional rest UI (some screens treat "fatigued" as "resting").
        SetRestingUI(isFatigued, etaText, progress01: null);
    }

    /// <summary>
    /// Preferred API for the JobAssign panel: show lock + timer + optional progress fill.
    /// </summary>
    public void SetResting(bool isResting, string etaText, float progress01)
    {
        if (button) button.interactable = !isResting;

        if (dimGroup) dimGroup.alpha = isResting ? fatiguedAlpha : 1f;
        else if (icon)
        {
            var c = icon.color;
            c.a = isResting ? fatiguedAlpha : 1f;
            icon.color = c;
        }

        // Keep existing overlay aligned if present.
        if (fatiguedRoot) fatiguedRoot.SetActive(isResting);
        if (fatiguedLabel)
        {
            if (!isResting) fatiguedLabel.text = "";
            else fatiguedLabel.text = string.IsNullOrEmpty(etaText) ? "RESTING" : $"RESTING • {etaText}";
        }

        SetRestingUI(isResting, etaText, progress01);
    }

    public void SetTooltip(string title, string subtitle)
    {
        if (!tooltip) return;
        tooltip.message = title;
        tooltip.subtitle = subtitle;
    }

    private void SetRestingUI(bool isResting, string etaText, float? progress01)
    {
        if (restLockRoot) restLockRoot.SetActive(isResting);

        if (restTimeText)
        {
            restTimeText.gameObject.SetActive(isResting);
            restTimeText.text = isResting ? (string.IsNullOrEmpty(etaText) ? "" : etaText) : "";
        }

        if (restFill)
        {
            restFill.gameObject.SetActive(isResting);
            float v = progress01.HasValue ? Mathf.Clamp01(progress01.Value) : 0f;
            restFill.fillAmount = v;
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

        if (tooltip)
        {
            tooltip.message = "Assigned";
            tooltip.subtitle = assignedText.text;
        }
    }

    private void ClearAssignmentUI()
    {
        if (!assignedText) return;
        assignedText.text = "";
        assignedText.gameObject.SetActive(false);
    }
}
